using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(PlayerInputReader), typeof(PlayerGroundProbe))]
[RequireComponent(typeof(PlayerFrontDetector), typeof(PlayerClimbMotor), typeof(PlayerAnimationBridge))]
public class PlayerMovement : MonoBehaviour
{
    const float MoveInputDeadZoneSqr = 0.01f;
    const float DirectionDeadZoneSqr = 0.001f;
    const float GroundStickVelocity = -1.5f;
    const float MaxFallSpeed = -30f;

    [Header("References")]
    [SerializeField] Transform playerCamera;
    [SerializeField] PlayerInputReader input;
    [SerializeField] PlayerGroundProbe groundProbe;
    [SerializeField] PlayerFrontDetector frontDetector;
    [SerializeField] PlayerClimbMotor climbMotor;
    [SerializeField] PlayerAnimationBridge animationBridge;
    [SerializeField] PlayerDebugHud debugHud;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 3f;
    [SerializeField] float runSpeed = 8f;
    [SerializeField] float acceleration = 8f;
    [SerializeField] float turnSpeed = 1500f;
    [SerializeField, Min(0.05f)] float gaitTransitionDuration = 0.4f;
    [SerializeField, Min(0f)] float gaitPressCooldown = 1f;
    [SerializeField, Min(0.05f)] float walkStopDuration = 0.4f;
    [SerializeField, Min(0.05f)] float runStopDuration = 0.55f;

    public bool IsRunning => currentState == MoveState.Grounded && groundGait.Mode == 2;
    readonly GroundGaitTransition groundGait = new GroundGaitTransition();
    public float RunCameraWeight => currentState == MoveState.Grounded
        ? Mathf.InverseLerp(walkSpeed, runSpeed, animationBridge.LocomotionSpeed) : 0f;

    [Header("Jump & Gravity")]
    [SerializeField] float jumpHeight = 3f;
    [SerializeField] float gravity = -48f;
    [SerializeField] float wallJumpAngleDegrees = 55f;
    [SerializeField] float wallJumpForceMultiplier = 1.22f;
    [SerializeField] float groundSmoothing = 10f;
    [SerializeField] float stationaryJumpTakeoffDelay = 0.1f;
    [SerializeField] float stationaryJumpVisualDrop = 0.25f;
    [SerializeField] float stationaryJumpVisualRecoverDuration = 0.18f;

    Rigidbody rb;
    CapsuleCollider capsule;
    PhysicsMaterial originalMaterial;
    PhysicsMaterial movementMaterial;
    Vector2 moveInput;
    Quaternion currentMoveRotation;
    Vector3 currentVelocity;
    Vector3 lockedAirVelocity;
    Vector3 airFacingDirection;
    Vector3 previousGroundVelocity;
    float stationaryJumpTimer;
    float stationaryJumpVisualOffset;
    bool hasLockedAirVelocity;

    enum MoveState
    {
        Grounded,
        JumpPreparing,
        Airborne,
        Climbing,
        Mounting
    }

    MoveState currentState = MoveState.Grounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (input == null) input = GetComponent<PlayerInputReader>();
        if (groundProbe == null) groundProbe = GetComponent<PlayerGroundProbe>();
        if (frontDetector == null) frontDetector = GetComponent<PlayerFrontDetector>();
        if (climbMotor == null) climbMotor = GetComponent<PlayerClimbMotor>();
        if (animationBridge == null) animationBridge = GetComponent<PlayerAnimationBridge>();
        if (debugHud == null) debugHud = GetComponent<PlayerDebugHud>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;
        if (playerCamera == null || input == null || groundProbe == null || frontDetector == null || climbMotor == null || animationBridge == null)
        {
            Debug.LogError("PlayerMovement needs a camera, input, ground/front probes, climb motor and animation bridge.", this);
            enabled = false;
            return;
        }

        // The controller owns acceleration and braking. Contact friction must not
        // apply a second, frame-dependent brake against moving floors and walls.
        capsule = GetComponent<CapsuleCollider>();
        if (capsule != null && movementMaterial == null)
        {
            originalMaterial = capsule.sharedMaterial;
            movementMaterial = new PhysicsMaterial("Player controller contact")
            {
                staticFriction = 0f, dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum
            };
            capsule.sharedMaterial = movementMaterial;
        }
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        currentMoveRotation = Quaternion.LookRotation(transform.forward);
    }

    void OnDestroy()
    {
        if (movementMaterial == null) return;
        if (capsule != null) capsule.sharedMaterial = originalMaterial;
        if (Application.isPlaying) Destroy(movementMaterial);
        else DestroyImmediate(movementMaterial);
    }

    void Update()
    {
        moveInput = input.MoveInput;

        if (currentState == MoveState.Grounded)
        {
            Vector3 camForward = playerCamera.forward;
            Vector3 camRight = playerCamera.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 targetMoveDirection = camForward * moveInput.y + camRight * moveInput.x;
            if (HasDirection(targetMoveDirection))
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetMoveDirection);
                currentMoveRotation = Quaternion.RotateTowards(currentMoveRotation, targetRotation, turnSpeed * Time.deltaTime);
            }
        }
    }

    void FixedUpdate()
    {
        // Proximity to a floor must not carry a jumping or climbing player.
        groundProbe.Probe();
        groundProbe.SampleGroundVelocity(rb.position);

        currentVelocity = rb.linearVelocity;

        frontDetector.Probe(rb.position, GetForwardSource().forward);

        switch (currentState)
        {
            case MoveState.Grounded:
                UpdateGrounded();
                break;

            case MoveState.JumpPreparing:
                UpdateJumpPreparing();
                break;

            case MoveState.Airborne:
                UpdateAirborne();
                break;

            case MoveState.Climbing:
                UpdateClimbing();
                break;

            case MoveState.Mounting:
                UpdateMounting();
                break;
        }

        UpdateAnimationState();
        rb.linearVelocity = currentVelocity;
        previousGroundVelocity = currentState == MoveState.Grounded || currentState == MoveState.JumpPreparing
            ? groundProbe.CurrentGroundVelocity : Vector3.zero;
    }

    void LateUpdate()
    {
        UpdateDebugHud();
    }

    // Runs normal floor movement, jumping, and the transition into falling.
    void UpdateGrounded()
    {
        ClearAirVelocityLock();

        // Losing support takes precedence over a queued jump.
        if (!IsGrounded())
        {
            input.ConsumeJump();
            LockAirVelocityFromCurrent();
            animationBridge.PlayJump(HasMoveInput(moveInput));
            SwitchState(MoveState.Airborne);
            ApplyGravity();
            return;
        }

        UpdateGroundVelocity(moveInput);

        if (input.ConsumeJump())
        {
            bool hadMoveInputAtJump = HasMoveInput(moveInput);

            ClearModelVerticalOffset();
            animationBridge.PlayJump(hadMoveInputAtJump);

            if (!hadMoveInputAtJump && stationaryJumpTakeoffDelay > 0f)
            {
                stationaryJumpTimer = stationaryJumpTakeoffDelay;
                SwitchState(MoveState.JumpPreparing);
                return;
            }

            ExecuteJump();
            SwitchState(MoveState.Airborne);
            return;
        }

        // Ground pressure points uphill on a slope. It must never turn an idle model.
        if (HasMoveInput(moveInput))
            animationBridge.RotateTowardsVelocity(Vector3.ProjectOnPlane(
                currentVelocity - groundProbe.CurrentGroundVelocity, groundProbe.GroundNormal), turnSpeed);
    }

    // World velocity = motion relative to the floor + motion of the floor itself.
    void UpdateGroundVelocity(Vector2 movementInput)
    {
        Vector3 normal = groundProbe.GroundNormal;
        Vector3 direction = Vector3.ProjectOnPlane(currentMoveRotation * Vector3.forward, normal).normalized;
        Vector3 relative = Vector3.ProjectOnPlane(currentVelocity - previousGroundVelocity, normal);
        float speed = groundGait.Step(HasMoveInput(movementInput), input.RunHeld,
            relative.magnitude, walkSpeed, runSpeed,
            HasMoveInput(movementInput) ? gaitTransitionDuration
                : Mathf.Lerp(walkStopDuration, runStopDuration, Mathf.InverseLerp(walkSpeed, runSpeed, relative.magnitude)),
            gaitPressCooldown, Time.fixedDeltaTime);
        // Preserve analog input strength and smooth steering without a second speed lag.
        Vector3 heading = Vector3.Slerp(relative.sqrMagnitude > 0.001f ? relative.normalized : direction,
            direction, acceleration * Time.fixedDeltaTime).normalized;
        relative = heading * speed * (HasMoveInput(movementInput) ? movementInput.magnitude : 1f);
        if (!HasMoveInput(movementInput) && relative.sqrMagnitude < 0.0001f)
            relative = Vector3.zero;
        currentVelocity = groundProbe.CurrentGroundVelocity + relative + normal * GroundStickVelocity;
    }

    // Holds the player briefly for the standing jump anticipation frames.
    void UpdateJumpPreparing()
    {
        input.ConsumeJump();

        if (!IsGrounded())
        {
            LockAirVelocityFromCurrent();
            SwitchState(MoveState.Airborne);
            return;
        }

        UpdateGroundVelocity(Vector2.zero);

        float preparationProgress = stationaryJumpTakeoffDelay > 0f
            ? 1f - Mathf.Clamp01(stationaryJumpTimer / stationaryJumpTakeoffDelay)
            : 1f;
        SetModelVerticalOffset(Mathf.Lerp(0f, -stationaryJumpVisualDrop, preparationProgress));

        stationaryJumpTimer -= Time.fixedDeltaTime;
        if (stationaryJumpTimer > 0f)
            return;

        SetModelVerticalOffset(-stationaryJumpVisualDrop);
        ExecuteJump();
        SwitchState(MoveState.Airborne);
    }

    // Keeps the jump-start horizontal velocity until landing or wall contact.
    void UpdateAirborne()
    {
        input.ConsumeJump();

        MaintainLockedAirVelocity();
        ApplyGravity();

        if (stationaryJumpVisualOffset < -0.001f)
        {
            float recoverDuration = Mathf.Max(0.01f, stationaryJumpVisualRecoverDuration);
            SetModelVerticalOffset(Mathf.MoveTowards(stationaryJumpVisualOffset, 0f, Time.fixedDeltaTime * stationaryJumpVisualDrop / recoverDuration));
        }

        // Preserve takeoff heading; support velocity and slope pressure are not steering.
        animationBridge.RotateTowardsVelocity(airFacingDirection, turnSpeed);

        if (climbMotor.TryAutoStick(frontDetector.LowerHit, currentVelocity, rb))
        {
            SwitchState(MoveState.Climbing);
            return;
        }

        if (IsGrounded() && currentVelocity.y <= 0f)
            SwitchState(MoveState.Grounded);
    }

    // Handles wall movement, wall jump, ledge mounting, and falling from the wall.
    void UpdateClimbing()
    {
        // Discard contact impulses; this state computes its own velocity every tick.
        currentVelocity = Vector3.zero;
        if (!climbMotor.HasWall)
        {
            DetachAndEnterAirborne();
            ApplyGravity();
            return;
        }
        climbMotor.RefreshWallContact();
        animationBridge.FaceClimbSurface(climbMotor.SurfaceNormal);
        if (input.ConsumeJump())
        {
            StartWallJump();
            return;
        }
        frontDetector.Probe(climbMotor.TargetPosition, -climbMotor.SurfaceNormal);
        int landingMask = groundProbe.GroundLayer.value | climbMotor.ClimbableLayer.value;

        if (TryStartLedgeMount(landingMask))
            return;

        bool stillOnWall = climbMotor.MoveOnWall(moveInput);
        animationBridge.FaceClimbSurface(climbMotor.SurfaceNormal);
        if (!stillOnWall)
        {
            frontDetector.Probe(rb.position, GetForwardSource().forward);
            if (TryStartLedgeMount(landingMask))
                return;

            DetachAndEnterAirborne();
            return;
        }

        if (!climbMotor.HasWall)
        {
            DetachAndEnterAirborne();
            return;
        }

        currentVelocity = (climbMotor.TargetPosition - rb.position) / Time.fixedDeltaTime;
        if (IsGrounded())
            SwitchState(MoveState.Grounded);
    }

    // Finishes the ledge mount and returns control to grounded movement.
    void UpdateMounting()
    {
        currentVelocity = Vector3.zero;
        input.ConsumeJump();

        if (!climbMotor.HasMountSurface)
        {
            SwitchState(MoveState.Airborne);
            ApplyGravity();
            return;
        }

        bool finished = climbMotor.UpdateMount();
        currentVelocity = (climbMotor.TargetPosition - rb.position) / Time.fixedDeltaTime;
        if (finished)
            SwitchState(MoveState.Grounded);
    }

    // Pushes the player away from the climbed wall and into the air.
    void StartWallJump()
    {
        Vector3 surfaceNormal = climbMotor.SurfaceNormal;
        Vector3 awayFromWall = Vector3.ProjectOnPlane(surfaceNormal, Vector3.up);
        if (!HasDirection(awayFromWall))
            awayFromWall = HasDirection(surfaceNormal) ? surfaceNormal : -transform.forward;
        awayFromWall.Normalize();

        float jumpSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity) * wallJumpForceMultiplier;
        float wallJumpAngleRadians = wallJumpAngleDegrees * Mathf.Deg2Rad;
        Vector3 jumpDirection = awayFromWall * Mathf.Cos(wallJumpAngleRadians) + Vector3.up * Mathf.Sin(wallJumpAngleRadians);
        currentVelocity = jumpDirection * jumpSpeed;

        groundProbe.ClearGround();

        ClearModelVerticalOffset();
        animationBridge.PlayJump(true);

        ClearAirVelocityLock();
        LockAirVelocityFromCurrent();
        airFacingDirection = awayFromWall;
        SwitchState(MoveState.Airborne);
    }

    // Horizontal velocity already includes the support: do not add it a second time.
    void ExecuteJump()
    {
        currentVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        groundProbe.ClearGround();

        LockAirVelocityFromCurrent();
    }

    // Changes the high-level movement state and applies the needed entry cleanup.
    void SwitchState(MoveState newState)
    {
        if (currentState == newState)
            return;

        if (currentState == MoveState.Climbing)
            climbMotor.DetachFromWall();

        currentState = newState;
        groundGait.Reset(Vector3.ProjectOnPlane(currentVelocity - groundProbe.CurrentGroundVelocity,
            groundProbe.GroundNormal).magnitude);

        switch (currentState)
        {
            case MoveState.Grounded:
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                break;

            case MoveState.JumpPreparing:
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                break;

            case MoveState.Airborne:
                if (!hasLockedAirVelocity)
                    LockAirVelocityFromCurrent();
                break;

            case MoveState.Climbing:
                animationBridge.FaceClimbSurface(climbMotor.SurfaceNormal);
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                currentVelocity = Vector3.zero;
                rb.linearVelocity = Vector3.zero;
                groundProbe.ClearGround();
                break;

            case MoveState.Mounting:
                currentVelocity = Vector3.zero;
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                break;
        }
    }

    // Sends movement facts to the animation bridge.
    void UpdateAnimationState()
    {
        /* Берем скорость игрока минус groundProbe.CurrentGroundVelocity. Проецируем результат на плоск. groundProbe.GroundNormal и берем длину вектора */
        Vector3 groundRelativeVelocity = Vector3.ProjectOnPlane(currentVelocity - groundProbe.CurrentGroundVelocity, groundProbe.GroundNormal);
        float locomotionSpeed = groundRelativeVelocity.magnitude;

        bool moving = HasMoveInput(moveInput);
        bool climbingAnimation = currentState == MoveState.Climbing;
        bool climbingMovement = climbingAnimation && moving;
        bool groundedAnimation = currentState == MoveState.Grounded;
        animationBridge.SetMovementState(groundedAnimation, currentVelocity.y, locomotionSpeed, climbingAnimation, climbingMovement);
        if (groundedAnimation) animationBridge.SetGroundSpeedImmediately(locomotionSpeed);
    }

    // Shows the current movement and animation state in the debug HUD.
    void UpdateDebugHud()
    {

        if (debugHud == null || !debugHud.IsVisible)
            return;

        debugHud.UpdateDebug(
            IsGrounded(),
            currentVelocity.y,
            frontDetector.UpperObjectName,
            frontDetector.LowerObjectName,
            currentState.ToString(),
            groundProbe.CurrentGround,
            groundProbe.CurrentGroundVelocity,
            groundProbe.GroundNormal,
            transform.parent,
            animationBridge.GetDebugInfo()
        );
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Checks whether the input is strong enough to count as movement.
    static bool HasMoveInput(Vector2 inputVector)
    {
        return inputVector.sqrMagnitude > MoveInputDeadZoneSqr;
    }

    // Checks whether a direction is strong enough to be useful.
    static bool HasDirection(Vector3 direction)
    {
        return direction.sqrMagnitude > DirectionDeadZoneSqr;
    }

    // Returns only the sideways part of a velocity.
    static Vector3 GetHorizontalVelocity(Vector3 velocity)
    {
        return new Vector3(velocity.x, 0f, velocity.z);
    }

    // Replaces only the sideways part of the current velocity.
    void SetHorizontalVelocity(Vector3 horizontalVelocity)
    {
        currentVelocity.x = horizontalVelocity.x;
        currentVelocity.z = horizontalVelocity.z;
    }

    // Applies grounded stickiness or falling gravity to the current velocity.
    void ApplyGravity()
    {
        if (IsGrounded() && currentVelocity.y <= 0f)
            currentVelocity.y = Mathf.Lerp(currentVelocity.y, GroundStickVelocity, groundSmoothing * Time.fixedDeltaTime);
        else
            currentVelocity.y += gravity * Time.fixedDeltaTime;

        currentVelocity.y = Mathf.Max(currentVelocity.y, MaxFallSpeed);
    }

    // Returns whether the ground probe currently sees valid ground.
    bool IsGrounded()
    {
        return groundProbe.IsGrounded;
    }

    // Keeps the model offset field and the animation bridge in sync.
    void SetModelVerticalOffset(float offset)
    {
        stationaryJumpVisualOffset = offset;
        animationBridge.SetModelVerticalOffset(stationaryJumpVisualOffset);
    }

    // Resets the temporary visual crouch offset.
    void ClearModelVerticalOffset()
    {
        SetModelVerticalOffset(0f);
    }

    // Chooses the visual forward direction when the animated model exists.
    Transform GetForwardSource()
    {
        return animationBridge.ModelTransform;
    }

    // Tries to begin ledge mounting and switches state when it succeeds.
    bool TryStartLedgeMount(int landingMask)
    {

        if (!climbMotor.TryStartMount(transform, GetForwardSource(), frontDetector.UpperHit, frontDetector.LowerHit, landingMask, groundProbe.MaxGroundAngle))
            return false;

        SwitchState(MoveState.Mounting);
        return true;
    }

    // Leaves the wall and continues with airborne movement.
    void DetachAndEnterAirborne()
    {
        LockAirVelocityFromCurrent();
        SwitchState(MoveState.Airborne);
    }

    // Stores the current sideways velocity for air control locking.
    void LockAirVelocityFromCurrent()
    {
        lockedAirVelocity = GetHorizontalVelocity(currentVelocity);
        airFacingDirection = GetForwardSource().forward;
        hasLockedAirVelocity = true;
    }

    // Reapplies the stored sideways velocity while the player is airborne.
    void MaintainLockedAirVelocity()
    {
        if (!hasLockedAirVelocity)
            LockAirVelocityFromCurrent();

        SetHorizontalVelocity(lockedAirVelocity);
    }

    // Clears the stored airborne sideways velocity.
    void ClearAirVelocityLock()
    {
        hasLockedAirVelocity = false;
        lockedAirVelocity = Vector3.zero;
    }
}


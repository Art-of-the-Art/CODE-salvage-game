using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
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
    [SerializeField] float deceleration = 12f;
    [SerializeField] float turnSpeed = 1500f;

    [Header("Jump & Gravity")]
    [SerializeField] float jumpHeight = 3f;
    [SerializeField] float gravity = -30f;
    [SerializeField] float wallJumpAngleDegrees = 55f;
    [SerializeField] float wallJumpForceMultiplier = 1.2f;
    [SerializeField] float groundSmoothing = 10f;
    [SerializeField] float stationaryJumpTakeoffDelay = 0.37f;
    [SerializeField] float stationaryJumpVisualDrop = 0.25f;
    [SerializeField] float stationaryJumpVisualRecoverDuration = 0.18f;

    Rigidbody rb;
    Vector2 moveInput;
    Vector3 targetMoveDirection;
    Quaternion currentMoveRotation;
    Vector3 currentVelocity;
    Vector3 lockedAirVelocity;
    Vector3 pendingStationaryJumpGroundVelocity;
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
        input = GetComponent<PlayerInputReader>();
        groundProbe = GetComponent<PlayerGroundProbe>();
        frontDetector = GetComponent<PlayerFrontDetector>();
        climbMotor = GetComponent<PlayerClimbMotor>();
        animationBridge = GetComponent<PlayerAnimationBridge>();
        debugHud = GetComponent<PlayerDebugHud>();

        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        currentMoveRotation = Quaternion.LookRotation(transform.forward);
    }

    void Update()
    {
        moveInput = ReadMoveInput();

        if (currentState == MoveState.Grounded)
        {
            Vector3 camForward = playerCamera.forward;
            Vector3 camRight = playerCamera.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            targetMoveDirection = camForward * moveInput.y + camRight * moveInput.x;
            if (HasDirection(targetMoveDirection))
                targetMoveDirection.Normalize();

            if (HasDirection(targetMoveDirection))
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetMoveDirection);
                currentMoveRotation = Quaternion.RotateTowards(currentMoveRotation, targetRotation, turnSpeed * Time.deltaTime);
            }
        }
    }

    void FixedUpdate()
    {
        groundProbe.Probe();
        groundProbe.ApplyGroundDelta(rb);

        currentVelocity = rb.linearVelocity;

        frontDetector.Probe(transform, GetForwardSource());

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
    }

    void LateUpdate()
    {
        UpdateDebugHud();
    }

    // Runs normal floor movement, jumping, and the transition into falling.
    void UpdateGrounded()
    {
        ClearAirVelocityLock();

        Vector3 moveDirection = currentMoveRotation * Vector3.forward;
        if (groundProbe.IsGrounded)
        {
            Vector3 projectedMove = Vector3.ProjectOnPlane(moveDirection, groundProbe.GroundNormal);
            if (HasDirection(projectedMove))
                moveDirection = projectedMove.normalized;
        }

        float speed = input.RunHeld ? runSpeed : walkSpeed;
        Vector3 targetVelocity = moveDirection * speed * moveInput.magnitude;
        Vector3 currentHorizontal = GetHorizontalVelocity(currentVelocity);
        float lerpFactor = HasMoveInput(moveInput) ? acceleration : deceleration;
        Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, targetVelocity, lerpFactor * Time.fixedDeltaTime);
        SetHorizontalVelocity(newHorizontal);
        ApplyGravity();

        if (input.ConsumeJump())
        {
            bool hadMoveInputAtJump = HasMoveInput(moveInput);
            Vector3 inheritedGroundVelocity = Vector3.ProjectOnPlane(groundProbe.CurrentGroundVelocity, Vector3.up);

            ClearModelVerticalOffset();
            animationBridge.PlayJump(hadMoveInputAtJump);

            if (!hadMoveInputAtJump && stationaryJumpTakeoffDelay > 0f)
            {
                pendingStationaryJumpGroundVelocity = inheritedGroundVelocity;
                stationaryJumpTimer = stationaryJumpTakeoffDelay;
                SwitchState(MoveState.JumpPreparing);
                return;
            }

            ExecuteJump(inheritedGroundVelocity);
            SwitchState(MoveState.Airborne);
            return;
        }

        animationBridge.RotateTowardsVelocity(currentVelocity, turnSpeed);

        if (!IsGrounded())
        {
            LockAirVelocityFromCurrent();
            animationBridge.PlayJump(HasMoveInput(moveInput));
            SwitchState(MoveState.Airborne);
        }
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

        pendingStationaryJumpGroundVelocity = Vector3.ProjectOnPlane(groundProbe.CurrentGroundVelocity, Vector3.up);

        Vector3 newHorizontal = Vector3.Lerp(GetHorizontalVelocity(currentVelocity), Vector3.zero, deceleration * Time.fixedDeltaTime);
        SetHorizontalVelocity(newHorizontal);
        ApplyGravity();

        float preparationProgress = stationaryJumpTakeoffDelay > 0f
            ? 1f - Mathf.Clamp01(stationaryJumpTimer / stationaryJumpTakeoffDelay)
            : 1f;
        SetModelVerticalOffset(Mathf.Lerp(0f, -stationaryJumpVisualDrop, preparationProgress));

        stationaryJumpTimer -= Time.fixedDeltaTime;
        if (stationaryJumpTimer > 0f)
            return;

        SetModelVerticalOffset(-stationaryJumpVisualDrop);
        ExecuteJump(pendingStationaryJumpGroundVelocity);
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

        animationBridge.RotateTowardsVelocity(currentVelocity, turnSpeed);

        if (climbMotor.TryAutoStick(frontDetector.LowerHit, currentVelocity, rb, transform))
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
        if (input.ConsumeJump())
        {
            StartWallJump();
            return;
        }


        climbMotor.ApplyWallDelta(transform);
        climbMotor.SolveClimbRotation(transform, turnSpeed);

        int landingMask = 0;
        landingMask |= groundProbe.GroundLayer.value;
        landingMask |= climbMotor.ClimbableLayer.value;

        if (TryStartLedgeMount(landingMask))
            return;

        bool stillOnWall = climbMotor.MoveOnWall(transform, moveInput);
        if (!stillOnWall)
        {
            frontDetector.Probe(transform, GetForwardSource());
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

        if (IsGrounded())
            SwitchState(MoveState.Grounded);
    }

    // Finishes the ledge mount and returns control to grounded movement.
    void UpdateMounting()
    {
        input.ConsumeJump();

        Vector3 velocityOverride = Vector3.zero;
        if (climbMotor.UpdateMount(transform, out velocityOverride))
        {
            currentVelocity = velocityOverride;
            SwitchState(MoveState.Grounded);
        }
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

        climbMotor.DetachFromWall();

        ClearAirVelocityLock();
        LockAirVelocityFromCurrent();
        SwitchState(MoveState.Airborne);
    }

    // Applies jump speed and inherits horizontal velocity from moving ground.
    void ExecuteJump(Vector3 inheritedGroundVelocity)
    {
        currentVelocity.x += inheritedGroundVelocity.x;
        currentVelocity.z += inheritedGroundVelocity.z;
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
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                currentVelocity = Vector3.zero;
                rb.linearVelocity = Vector3.zero;
                groundProbe.ClearGround();
                break;

            case MoveState.Mounting:
                ClearAirVelocityLock();
                ClearModelVerticalOffset();
                break;
        }
    }

    // Sends movement facts to the animation bridge.
    void UpdateAnimationState()
    {

        bool moving = HasMoveInput(moveInput);
        bool climbingAnimation = currentState == MoveState.Climbing;
        bool climbingMovement = climbingAnimation && moving;
        bool groundedAnimation = currentState == MoveState.Grounded;
        bool runningAnimation = groundedAnimation && moving && input.RunHeld;
        bool walkingAnimation = groundedAnimation && moving && !runningAnimation;

        animationBridge.SetMovementState(groundedAnimation, currentVelocity.y, walkingAnimation, runningAnimation, climbingAnimation, climbingMovement);
    }

    // Shows the current movement and animation state in the debug HUD.
    void UpdateDebugHud()
    {

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

    // Reads the current movement input.
    Vector2 ReadMoveInput()
    {
        return input.MoveInput;
    }

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

        if (!climbMotor.TryStartMount(transform, GetForwardSource(), frontDetector.UpperHit, frontDetector.LowerHit, landingMask))
            return false;

        SwitchState(MoveState.Mounting);
        return true;
    }

    // Leaves the wall and continues with airborne movement.
    void DetachAndEnterAirborne()
    {
        climbMotor.DetachFromWall();
        LockAirVelocityFromCurrent();
        SwitchState(MoveState.Airborne);
    }

    // Stores the current sideways velocity for air control locking.
    void LockAirVelocityFromCurrent()
    {
        lockedAirVelocity = GetHorizontalVelocity(currentVelocity);
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


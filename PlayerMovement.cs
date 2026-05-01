using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerMovement : MonoBehaviour
{
    // -------------------------
    // Declarations
    // -------------------------

    [Header("References and objects")]
    [SerializeField] Transform playerCamera;
    [SerializeField] TMP_Text interfaceText;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 15f;
    [SerializeField] float acceleration = 8f;
    [SerializeField] float deceleration = 12f;
    [SerializeField] float turnSpeed = 1500f;

    [Header("Jump & Gravity")]
    [SerializeField] float jumpHeight = 4f;
    [SerializeField] float gravity = -30f;
    [SerializeField] float groundSmoothing = 10f;

    [Header("Checks and detections")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] LayerMask detectionLayer;
    [SerializeField] LayerMask climbableLayer;
    [SerializeField] float groundCheckDistance = 1.2f;
    [SerializeField] float detectionDistance = 1.2f;
    [SerializeField] float UpperRayHeightOffset = 0.8f;
    [SerializeField] float LowerRayHeightOffset = 0.25f;

    [Header("Climbing")]
    [SerializeField] float autoStickVelocityThreshold = 0.6f;
    [SerializeField] float wallOffset = 2f;
    [SerializeField] float climbSpeed = 4f;
    [SerializeField] float mountForwardOffset = 0.6f;
    [SerializeField] 


    // Objects to affect:
    Rigidbody rb;
    Animator anim;

    // INPUT:
    Vector2 moveInput;
    bool jumpRequested;

    // DIRECTION
    Vector3 targetMoveDirection;
    Vector3 lastMoveDirection = Vector3.forward;
    Quaternion currentMoveRotation;
    Vector3 currentVelocity;

    // Front detection
    RaycastHit UpperFrontHit;
    RaycastHit LowerFrontHit;
    string UpperFrontObjectName = "None";
    string LowerFrontObjectName = "None";
    bool isGrounded;

    // Surfaces and climbing
    Vector3 surfaceNormal;
    Vector3 surfacePoint;
    Transform currentWall;
    Vector3 lastWallPosition;
    Quaternion lastWallRotation;
    Vector3 wallUp;
    Vector3 wallRight;
    Vector3 delta;

    enum MoveState
    {
        Grounded,
        Airborne,
        Climbing
    }
    MoveState currentState = MoveState.Grounded;

    void switchState(MoveState newState)
    {
        // --- Выход из текущего состояния ---
        switch (currentState)
        {
            case MoveState.Grounded:
            // ничего
            break;
            case MoveState.Airborne:
            // ничего
            break;
            case MoveState.Climbing:
            currentWall = null;
            break;
        }

        // --- Вход в новое состояние ---
        switch (newState)
        {
            case MoveState.Grounded:
            // ничего
            break;
            case MoveState.Airborne:
            // ничего
            break;
            case MoveState.Climbing:
            currentVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            break;
        }

        currentState = newState;
    }

    // --------------------------------------
    //
    // Awake, Update, FixedUpdate and Input
    //
    // --------------------------------------

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponentInChildren<Animator>();

        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (playerCamera == null)
            playerCamera = Camera.main.transform;

        currentMoveRotation = Quaternion.LookRotation(transform.forward);
    }

    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void Update()
    {
        UpdateUI();
        ReadInput();
        SolveDirection();
        SolveRotation();
    }

    void FixedUpdate()
    {
        CheckGround();
        currentVelocity = rb.linearVelocity;
        anim.SetBool("isRunning", moveInput.sqrMagnitude > 0.01f);

        switch(currentState)
        {
            case MoveState.Grounded:
                SolveVelocity();
                ApplyGravity();
                ApplyJump();
                SolveModelRotation();
                //if (moveInput.sqrMagnitude > 0.01f)
                //    ApplyAutoStick();
                if (!isGrounded)
                    switchState(MoveState.Airborne);
                break;

            case MoveState.Airborne:
                SolveVelocity();
                ApplyGravity();
                SolveModelRotation();
                ApplyAutoStick();
                if (isGrounded && currentVelocity.y <= 0)
                    switchState(MoveState.Grounded);
                break;

            case MoveState.Climbing:
                ApplyWallDelta();
                MoveOnWall();
                SolveClimbRotation();
                if(currentWall == null)
                    switchState(MoveState.Airborne);
                if (isGrounded)
                    switchState(MoveState.Grounded);
                break;
        }
        rb.linearVelocity = currentVelocity;
        HandleFrontDetection();
    }

    void ReadInput() // Every update cheks pressed buttons
    {
        switch(currentState)
        {
            case MoveState.Grounded:
                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                    jumpRequested = true;
                break;

            case MoveState.Airborne:
                break;

            case MoveState.Climbing:
                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                    DetachFromWall();
                break;
        }
    }

    // --------------------------------------
    //
    // Gameplay Functions
    //
    // --------------------------------------

    void SolveDirection()
    {
        Vector3 camForward = playerCamera.forward;
        Vector3 camRight = playerCamera.right;

        camForward.y = 0;
        camRight.y = 0;

        camForward.Normalize();
        camRight.Normalize();

        targetMoveDirection =
            camForward * moveInput.y +
            camRight * moveInput.x;

        if (targetMoveDirection.sqrMagnitude > 0.001f)
            targetMoveDirection.Normalize();
    }

    void SolveRotation() // Smoothly rotates the character towards the target move direction
    {
        if (targetMoveDirection.sqrMagnitude < 0.001f)
            return; // No significant input, keep current rotation

        Quaternion targetRotation = Quaternion.LookRotation(targetMoveDirection);

        currentMoveRotation = 
            Quaternion.RotateTowards(
                currentMoveRotation,
                targetRotation,
                turnSpeed * Time.deltaTime
            );

        // lastMoveDirection = currentMoveRotation * Vector3.forward; - убрал чтобы поворачивать игрока в сторону движения
    }

    void SolveModelRotation()
    {
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
        if ((horizontalVelocity.sqrMagnitude) >=0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(new Vector3(currentVelocity.x, 0, currentVelocity.z));
            anim.transform.rotation = Quaternion.RotateTowards(anim.transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
            lastMoveDirection = targetRotation * Vector3.forward;
        };
    }

    void SolveVelocity() // Calculates and updates velocity based on the current direction and speed
    {
        Vector3 moveDir = currentMoveRotation * Vector3.forward;

        Vector3 targetVelocity = moveDir * moveSpeed * moveInput.magnitude;

        Vector3 currentHorizontal = new Vector3(currentVelocity.x, 0, currentVelocity.z);

        float lerpFactor =
            moveInput.sqrMagnitude > 0.01f
            ? acceleration
            : deceleration;

        Vector3 newHorizontal =
            Vector3.Lerp(
                currentHorizontal,
                targetVelocity,
                lerpFactor * Time.fixedDeltaTime
            );

        currentVelocity.x = newHorizontal.x;
        currentVelocity.z = newHorizontal.z;
    }

    void SolveClimbRotation()
    {
        if (currentWall == null) return;

        Quaternion targetRotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
    }

    void ApplyGravity() // Applies gravity to the vertical velocity, with smoothing when grounded
                        // if Player isGrounded and falling, it will transition to a small downward velocity to keep the character grounded
    {
        if (isGrounded && currentVelocity.y <= 0)
        {
            currentVelocity.y =
                Mathf.Lerp(
                    currentVelocity.y,
                    -1.5f,
                    groundSmoothing * Time.fixedDeltaTime
                );
        }
        else
        {
            currentVelocity.y += gravity * Time.fixedDeltaTime;
        }

        currentVelocity.y = Mathf.Max(currentVelocity.y, -30f);
    }

    void ApplyJump()    // If jump button is pressed, calculates the initial jump velocity needed to reach 
                        // ...the desired jump height and applies it to the vertical velocity
    {
        if (!jumpRequested) return;

        currentVelocity.y =
            Mathf.Sqrt(jumpHeight * -2f * gravity);

        jumpRequested = false;
        isGrounded = false;
    }

    void ApplyAutoStick() // If the player is close to the ground and moving downwards, apply a small downward velocity to keep them grounded
    {
        if (LowerFrontHit.collider == null)
            return;
        else if (LowerFrontHit.collider != null 
            && ((1 << LowerFrontHit.collider.gameObject.layer) & climbableLayer) != 0
            && Vector3.Angle(LowerFrontHit.normal, Vector3.up) > 60f
            && Vector3.Dot(currentVelocity, -LowerFrontHit.normal) >= autoStickVelocityThreshold
            )
            AttachToWall(LowerFrontHit);
    }

    void CheckGround() // Raycasts downward to check if the character is grounded, update isGrounded accordingly
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        float rayLength = 1.0f;
        RaycastHit hit;
        bool hitSomething = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out hit,
            rayLength,
            groundLayer
        );

        isGrounded = hitSomething;

        Debug.DrawRay(
            rayOrigin,
            Vector3.down * rayLength,
            isGrounded ? Color.green : Color.red
        );

        if (hitSomething)
            Debug.Log($"HIT: {hit.collider.name}, distance: {hit.distance:F2}");
        else
            Debug.Log($"MISS! rayOrigin.y = {rayOrigin.y:F2}, playerPos.y = {transform.position.y:F2}");
    }

    void AttachToWall(RaycastHit hit)
    {
        surfaceNormal = hit.normal;
        transform.rotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up); // Rotate the player to face away from the wall
        surfacePoint = hit.point;
        currentWall = hit.transform;
        
        lastWallPosition = currentWall.position;
        lastWallRotation = currentWall.rotation;

        currentVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;

        transform.position = surfacePoint + surfaceNormal * wallOffset; // Adjust the offset as needed
        switchState(MoveState.Climbing);
    }

    void DetachFromWall()
    {
        currentWall = null;
        switchState(MoveState.Airborne);
    }

    void ApplyWallDelta()
    {
        if (currentWall == null) return;

        Vector3 positionDelta = currentWall.position - lastWallPosition;
        Quaternion rotationDelta = currentWall.rotation * Quaternion.Inverse(lastWallRotation);
        transform.rotation = rotationDelta * transform.rotation;

        // Корректировка позиции при вращении стены
        Vector3 localOffset = transform.position - lastWallPosition;
        Vector3 rotatedOffset = rotationDelta * localOffset;
        positionDelta += (rotatedOffset - localOffset);

        transform.position += positionDelta;
        surfaceNormal = rotationDelta * surfaceNormal;

        lastWallPosition = currentWall.position;
        lastWallRotation = currentWall.rotation;
        return;
    }

    void MoveOnWall()
    {
        wallUp = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal).normalized;
        wallRight = Vector3.Cross(surfaceNormal, wallUp).normalized;
        delta = (wallRight * moveInput.x + wallUp * moveInput.y) * climbSpeed * Time.fixedDeltaTime;
        transform.position += delta;

        if(Physics.Raycast(transform.position, -surfaceNormal, out RaycastHit hit, wallOffset * 1.5f, climbableLayer))
        {
            surfaceNormal = hit.normal;
            surfacePoint = hit.point;
            transform.position = hit.point + hit.normal * wallOffset;
        }
        else
        {
            DetachFromWall();
            return;
        }
    }


    // --------------------------------------
    //
    // Service functions non-gameplay related
    //
    // --------------------------------------
    void HandleFrontDetection()
    {
        UpperFrontObjectName = "None";
        LowerFrontObjectName = "None";

        Vector3 upperOrigin =
            transform.position +
            Vector3.up * UpperRayHeightOffset;

        Vector3 lowerOrigin =
            transform.position +
            Vector3.up * LowerRayHeightOffset;

        if (Physics.Raycast(
            upperOrigin,
            anim.transform.forward,
            out UpperFrontHit,
            detectionDistance,
            detectionLayer))
        {
            UpperFrontObjectName =
                UpperFrontHit.collider.name;
        }

        if (Physics.Raycast(
            lowerOrigin,
            anim.transform.forward,
            out LowerFrontHit,
            detectionDistance,
            detectionLayer))
        {
            LowerFrontObjectName =
                LowerFrontHit.collider.name;
        }
    }

    void UpdateUI() // Interface text update 
    {
        if (interfaceText == null) return;

        interfaceText.text =
            $"Grounded: {isGrounded}\n" +
            $"VelY: {currentVelocity.y:F2}\n" +
            $"Upper: {UpperFrontObjectName}\n" +
            $"Lower: {LowerFrontObjectName}\n" +
            $"Parent: {(transform.parent ? transform.parent.name : "none")}";
    }
}

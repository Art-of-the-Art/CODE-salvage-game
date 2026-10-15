// Автор прочитал и понимает что тут происходит.
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Linked Objects")]
    public Transform player;

    [Header("Sensitivity")]
    public float sensitivity = 0.12f;

    [Header("Limits")]
    public float minPitch = -50f;
    public float maxPitch = 65f;
    public float defaultDistance = 5f;
    public float heightOffset = 4f;

    [Header("Running")]
    [Min(0f)] public float runDistanceHeightFraction = 0.25f;
    [Min(1f)] public float runFovMultiplier = 1.2f;
    [Min(0.01f)] public float runEffectSmoothTime = 0.2f;
    Camera viewCamera;
    PlayerMovement movement;
    PlayerInputReader input;
    float baseFov;
    float modelHeight;
    float runBlend;
    float runBlendVelocity;

    [Header("Running Follow")]
    [Min(0f)] public float maxRunLagDistance = 1.4f;
    [Min(0.01f)] public float runLagBuildDuration = 1f;
    [Min(0.01f)] public float runLagReturnDuration = 1f;

    [Header("Collisions")]
    public float collisionRadius = 0.5f;
    public float collisionSmoothing = 10f;

    [Header("Aiming")]
    public float aimFov = 30f;
    public float aimDistance = 3f;
    public float aimBlendDuration = 0.1f;

    [Header("Lock-On")]
    [SerializeField, Min(0f)] float lockOnFollowSpeed = 240f;
    PlayerLockOn lockOn;

    float pitch;
    float yaw;
    float currentDistance;
    LayerMask collisionMask;
    PlayerGroundProbe groundProbe;
    PlayerClimbMotor climbMotor;
    Transform trackedSupport;
    Quaternion previousSupportRotation;
    float runLagElapsed;
    float runLagWeight;
    readonly RaycastHit[] collisionHits = new RaycastHit[24];
    [SerializeField] Vector3 aimShoulderOffset = new Vector3(0.3f, 0.2f, 0f);
    [SerializeField, Range(0.01f, 1f)] float aimSensitivityMultiplier = 0.4f;
    float aimBlend;
    float baseSensitivity;

    void Awake()
    {
        collisionMask = LayerMask.GetMask("Default", "Environment", "Ground", "Terrain");
    }

    void Start()
    {
        if (player == null)
        {
            Debug.LogError("ThirdPersonCamera needs a player Transform.", this);
            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        groundProbe = player.GetComponent<PlayerGroundProbe>();
        climbMotor = player.GetComponent<PlayerClimbMotor>();
        yaw = player.eulerAngles.y;
        pitch = 0f;
        currentDistance = defaultDistance;
        movement = player.GetComponent<PlayerMovement>();
        input = player.GetComponent<PlayerInputReader>();
        lockOn = player.GetComponent<PlayerLockOn>();
        baseSensitivity = sensitivity;
        // Main uses a Pivot with a child Camera; tests may put both on one object.
        viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera != null) baseFov = viewCamera.fieldOfView;
        // Renderer bounds include the whole visible model, unlike the collision capsule.
        var renderers = player.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            modelHeight = bounds.size.y;
        }
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        InheritSupportYaw();
        UpdateRunLag();

        bool isAiming = input != null && input.IsAiming;
        float targetAimBlend;
        if (isAiming) targetAimBlend = 1f;
        else targetAimBlend = 0f;
        aimBlend = Mathf.MoveTowards(aimBlend, targetAimBlend, Time.deltaTime / Mathf.Max(0.01f, aimBlendDuration));
        
        runBlend = Mathf.SmoothDamp(runBlend, movement != null ? movement.RunCameraWeight : 0f, ref runBlendVelocity, runEffectSmoothTime);
        float runWhileAiming = runBlend * (1f - aimBlend);
        float normalFov = baseFov * Mathf.Lerp(1f, runFovMultiplier, runWhileAiming);
        float targetFov = Mathf.Lerp(normalFov, aimFov, aimBlend);
        if (viewCamera != null)
            viewCamera.fieldOfView = targetFov;

        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        if (lockOn != null)
            mouseDelta = lockOn.ConstrainLookInput(mouseDelta);


        float currentSensitivity = Mathf.Lerp(
            baseSensitivity,
            baseSensitivity * aimSensitivityMultiplier,
            aimBlend);
        yaw += mouseDelta.x * currentSensitivity;
        pitch -= mouseDelta.y * currentSensitivity;
        UpdateLockOnTracking();
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivotPoint = player.position + Vector3.up * heightOffset;
        Vector3 localShoulder = aimShoulderOffset * aimBlend;
        pivotPoint += cameraRotation * localShoulder;
        Vector3 cameraForward = cameraRotation * Vector3.forward;
        Vector3 castDirection = -cameraForward;

        float targetDistance = FindAllowedDistance(pivotPoint, castDirection);

        if (targetDistance < currentDistance)
            currentDistance = targetDistance;
        else
            currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * collisionSmoothing);

        transform.SetPositionAndRotation(pivotPoint - cameraForward * currentDistance, cameraRotation);
    }

    // A turning support rotates the view, while translation already comes from the player root.
    void InheritSupportYaw()
    {
        Transform support = GetCurrentSupportTransform();

        if (support == null)
        {
            trackedSupport = null;
            return;
        }

        if (support != trackedSupport)
        {
            trackedSupport = support;
            previousSupportRotation = support.rotation;
            return;
        }

        Vector3 previousForward = Vector3.ProjectOnPlane(previousSupportRotation * Vector3.forward, Vector3.up);
        Vector3 currentForward = Vector3.ProjectOnPlane(support.forward, Vector3.up);
        if (previousForward.sqrMagnitude > 0.0001f && currentForward.sqrMagnitude > 0.0001f)
            yaw += Vector3.SignedAngle(previousForward, currentForward, Vector3.up);

        previousSupportRotation = support.rotation;
    }

    void UpdateRunLag()
    {
        if (movement != null && movement.IsRunning)
        {
            runLagElapsed += Time.deltaTime;
            runLagWeight = EvaluateRunLagCycle(runLagElapsed);
            return;
        }

        runLagElapsed = 0f;
        runLagWeight = Mathf.MoveTowards(runLagWeight, 0f, Time.deltaTime / runLagReturnDuration);
    }

    float EvaluateRunLagCycle(float elapsed)
    {
        if (elapsed < runLagBuildDuration)
            return Mathf.SmoothStep(0f, 1f, elapsed / runLagBuildDuration);

        float returnTime = elapsed - runLagBuildDuration;
        return returnTime < runLagReturnDuration
            ? Mathf.SmoothStep(1f, 0f, returnTime / runLagReturnDuration)
            : 0f;
    }

    Transform GetCurrentSupportTransform()
    {
        if (groundProbe != null && groundProbe.IsGrounded)
            return groundProbe.CurrentSupportTransform;
        return climbMotor != null ? climbMotor.CurrentSupportTransform : null;
    }

    Collider GetCurrentSupportCollider()
    {
        if (groundProbe != null && groundProbe.IsGrounded)
            return groundProbe.CurrentGroundCollider;
        return climbMotor != null ? climbMotor.CurrentSupportCollider : null;
    }

    float FindAllowedDistance(Vector3 pivotPoint, Vector3 castDirection)
    {
        float runWhileAiming = 1f - aimBlend;
        float normalDistance = defaultDistance + modelHeight * runDistanceHeightFraction * runBlend * runWhileAiming + maxRunLagDistance * runLagWeight * runWhileAiming;
        float wantedDistance = Mathf.Lerp(normalDistance, aimDistance, aimBlend);
        float allowedDistance = wantedDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            pivotPoint,
            collisionRadius,
            castDirection,
            collisionHits,
            wantedDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = collisionHits[i];
            if (ShouldIgnore(hit.collider))
                continue;

            float hitDistance = Mathf.Clamp(hit.distance - collisionRadius, collisionRadius, wantedDistance);
            allowedDistance = Mathf.Min(allowedDistance, hitDistance);
        }

        return allowedDistance;
    }

    void OnDisable()
    {
        if (viewCamera != null && baseFov > 0f) viewCamera.fieldOfView = baseFov;
        runBlend = runBlendVelocity = 0f;
        runLagElapsed = runLagWeight = 0f;
        trackedSupport = null;
    }

    // The robot carrying the player is part of the camera target, not an obstacle behind it.
    bool ShouldIgnore(Collider candidate)
    {
        if (candidate == null)
            return true;
        if (candidate.transform == player || candidate.transform.IsChildOf(player))
            return true;
        Collider supportCollider = GetCurrentSupportCollider();
        if (supportCollider == null)
            return false;

        Rigidbody supportBody = supportCollider.attachedRigidbody;
        return supportBody != null && candidate.attachedRigidbody == supportBody;
    }

    void UpdateLockOnTracking()
    {
        if (lockOn == null)
            return;

        if (!lockOn.TryGetViewportError(out Vector2 viewportError))
            return;

        float followStep = lockOnFollowSpeed * Time.deltaTime;

        yaw += viewportError.x * followStep;
        pitch -= viewportError.y * followStep;
    }
}

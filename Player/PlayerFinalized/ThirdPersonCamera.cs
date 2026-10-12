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
    float baseFov;
    float modelHeight;
    float runBlend;
    float runBlendVelocity;

    [Header("Collisions")]
    public float collisionRadius = 0.5f;
    public float collisionSmoothing = 10f;

    float pitch;
    float yaw;
    float currentDistance;
    LayerMask collisionMask;
    PlayerGroundProbe groundProbe;
    Transform trackedSupport;
    Quaternion previousSupportRotation;
    readonly RaycastHit[] collisionHits = new RaycastHit[24];

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
        yaw = player.eulerAngles.y;
        pitch = 0f;
        currentDistance = defaultDistance;
        movement = player.GetComponent<PlayerMovement>();
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
        runBlend = Mathf.SmoothDamp(runBlend, movement != null ? movement.RunCameraWeight : 0f,
            ref runBlendVelocity, runEffectSmoothTime);
        if (viewCamera != null) viewCamera.fieldOfView = baseFov * Mathf.Lerp(1f, runFovMultiplier, runBlend);

        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Vector3 pivotPoint = player.position + Vector3.up * heightOffset;
        Quaternion cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 cameraForward = cameraRotation * Vector3.forward;
        Vector3 castDirection = -cameraForward;

        float targetDistance = FindAllowedDistance(pivotPoint, castDirection);

        if (targetDistance < currentDistance)
            currentDistance = targetDistance;
        else
            currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * collisionSmoothing);

        transform.SetPositionAndRotation(pivotPoint - cameraForward * currentDistance, cameraRotation);
    }

    // Keeps the same view direction relative to the floor while a platform turns.
    void InheritSupportYaw()
    {
        Transform support = groundProbe != null && groundProbe.IsGrounded
            ? groundProbe.CurrentSupportTransform
            : null;

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

    float FindAllowedDistance(Vector3 pivotPoint, Vector3 castDirection)
    {
        float wantedDistance = defaultDistance + modelHeight * runDistanceHeightFraction * runBlend;
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
    }

    // The robot carrying the player is part of the camera target, not an obstacle behind it.
    bool ShouldIgnore(Collider candidate)
    {
        if (candidate == null)
            return true;
        if (candidate.transform == player || candidate.transform.IsChildOf(player))
            return true;
        if (groundProbe == null || !groundProbe.IsGrounded || groundProbe.CurrentGroundCollider == null)
            return false;

        Rigidbody supportBody = groundProbe.CurrentGroundCollider.attachedRigidbody;
        return supportBody != null && candidate.attachedRigidbody == supportBody;
    }
}

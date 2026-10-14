using UnityEngine;

public class PlayerClimbMotor : MonoBehaviour
{
    [Header("Climbing")]
    [SerializeField] LayerMask climbableLayer;
    [SerializeField] float autoStickVelocityThreshold = 0.6f;
    [SerializeField] float wallOffset = 0.6f;
    [SerializeField] float climbSpeed = 4f;

    [Header("Mounting")]
    [SerializeField] float mountForwardOffset = 0.6f;
    [SerializeField] float mountUpOffset = 1.2f;
    [SerializeField] float mountSearchDownDistance = 3f;
    [SerializeField] float mountDuration = 0.6f;
    [SerializeField] float ledgeGraceDuration = 0.25f;
    [SerializeField] float ledgeExtraForwardOffset = 0.45f;
    [SerializeField] float ledgeExtraUpOffset = 0.35f;
    [SerializeField] float ledgeExtraDownDistance = 1f;

    Transform currentWall;
    Collider wallCollider;
    Vector3 wallLocalAnchor;
    Vector3 wallLocalNormal;
    Vector3 surfaceNormal;
    float lastWallContactTime;
    Transform mountSurface;
    Collider mountCollider;
    Vector3 mountStartPosition;
    Vector3 mountTargetPosition;
    float mountStartTime;
    float currentMountDuration;

    public LayerMask ClimbableLayer => climbableLayer;
    public bool HasWall => currentWall != null && wallCollider != null
        && wallCollider.enabled && wallCollider.gameObject.activeInHierarchy;
    public Vector3 TargetPosition { get; private set; }
    public bool HasMountSurface => mountSurface != null && mountCollider != null && mountCollider.enabled && mountCollider.gameObject.activeInHierarchy;
    public Vector3 SurfaceNormal => surfaceNormal;
    public Collider CurrentSupportCollider => HasWall ? wallCollider : HasMountSurface ? mountCollider : null;
    public Transform CurrentSupportTransform
    {
        get
        {
            Collider support = CurrentSupportCollider;
            return support != null && support.attachedRigidbody != null
                ? support.attachedRigidbody.transform
                : support != null ? support.transform : null;
        }
    }

    // Attaches the player to a steep climbable wall when they hit it with enough speed.
    public bool TryAutoStick(RaycastHit lowerHit, Vector3 currentVelocity, Rigidbody rb)
    {
        if (lowerHit.collider == null)
            return false;

        bool isSteep = Vector3.Angle(lowerHit.normal, Vector3.up) > 60f;
        bool movingIntoSurface = Vector3.Dot(currentVelocity, -lowerHit.normal) >= autoStickVelocityThreshold;

        if (!IsClimbable(lowerHit.collider) || !isSteep || !movingIntoSurface)
            return false;

        AttachToWall(lowerHit, rb);
        return true;
    }

    // Places the player on the wall and stops their previous physics movement.
    public void AttachToWall(RaycastHit hit, Rigidbody rb)
    {

        surfaceNormal = hit.normal;
        TargetPosition = hit.point + hit.normal * wallOffset;
        RememberWallContact(hit, TargetPosition);
        // A single initial placement. Continuous climbing below uses velocity only.
        rb.position = TargetPosition;
        rb.rotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void DetachFromWall()
    {
        currentWall = null;
        wallCollider = null;
    }

    // Reconstruct the saved contact at the wall's CURRENT physics pose.
    // No body movement occurs while probes and the next target are being calculated.
    public void RefreshWallContact()
    {
        if (!HasWall)
            return;
        PlayerGroundProbe.GetSurfacePose(wallCollider, out Vector3 position, out Quaternion rotation);
        TargetPosition = position + rotation * wallLocalAnchor;
        surfaceNormal = rotation * wallLocalNormal;
    }

    public bool MoveOnWall(Vector2 moveInput)
    {
        if (!HasWall)
            return false;
        Vector3 wallUp = GetWallUp();
        Vector3 wallRight = Vector3.Cross(surfaceNormal, wallUp).normalized;
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        Vector3 candidate = TargetPosition
            + (wallRight * moveInput.x + wallUp * moveInput.y) * climbSpeed * Time.fixedDeltaTime;

        if (Physics.Raycast(candidate, -surfaceNormal, out RaycastHit hit,
            wallOffset * 1.5f, climbableLayer, QueryTriggerInteraction.Ignore))
        {
            surfaceNormal = hit.normal;
            candidate = hit.point + hit.normal * wallOffset;
            RememberWallContact(hit, candidate);
        }
        else if (!IsWithinLedgeGrace())
            return false;
        else
            RememberAnchor(candidate);

        // Aim at the NEXT physics pose so player and robot travel together.
        TargetPosition = candidate + PlayerGroundProbe.GetPointVelocity(wallCollider, candidate) * Time.fixedDeltaTime;
        return true;
    }

    void RememberWallContact(RaycastHit hit, Vector3 bodyPosition)
    {
        currentWall = hit.transform;
        wallCollider = hit.collider;
        lastWallContactTime = Time.time;
        RememberAnchor(bodyPosition);
    }

    void RememberAnchor(Vector3 bodyPosition)
    {
        PlayerGroundProbe.GetSurfacePose(wallCollider, out Vector3 position, out Quaternion rotation);
        wallLocalAnchor = Quaternion.Inverse(rotation) * (bodyPosition - position);
        wallLocalNormal = Quaternion.Inverse(rotation) * surfaceNormal;
    }

    // Starts pulling the player onto the top of a ledge when a landing point is available.
    public bool TryStartMount(Transform body, Transform forwardTransform, RaycastHit upperHit, RaycastHit lowerHit, int landingMask, float maxLandingAngle = 55f)
    {
        if (upperHit.collider != null)
            return false;

        if (!IsClimbable(lowerHit.collider) && !HasWall)
            return false;

        Vector3 bodyPosition = HasWall ? TargetPosition : body.position;
        Vector3 forward = GetMountForward(body, forwardTransform);
        Vector3 wallUp = GetWallUp();
        Vector3[] searchOrigins =
        {
            bodyPosition + Vector3.up * mountUpOffset + forward * mountForwardOffset,
            bodyPosition + Vector3.up * (mountUpOffset + ledgeExtraUpOffset) + forward * mountForwardOffset,
            bodyPosition + Vector3.up * mountUpOffset + forward * (mountForwardOffset + ledgeExtraForwardOffset),
            bodyPosition + wallUp * mountUpOffset + forward * (mountForwardOffset + ledgeExtraForwardOffset)
        };

        RaycastHit landingHit = default;
        bool hasLanding = false;
        float searchDistance = mountSearchDownDistance + ledgeExtraDownDistance;

        for (int i = 0; i < searchOrigins.Length; i++)
        {
            if (Physics.Raycast(searchOrigins[i], Vector3.down, out landingHit, searchDistance, landingMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(landingHit.normal, Vector3.up) <= maxLandingAngle)
            {
                hasLanding = true;
                break;
            }
        }

        if (!hasLanding)
            return false;

        // Store both ends relative to the contacted collider, including moving robot parts.
        mountSurface = landingHit.collider.transform;
        mountCollider = landingHit.collider;
        PlayerGroundProbe.GetSurfacePose(mountCollider, out Vector3 position, out Quaternion rotation);
        Rigidbody playerBody = body.GetComponent<Rigidbody>();
        Vector3 start = playerBody != null ? playerBody.position : body.position;
        mountStartPosition = Quaternion.Inverse(rotation) * (start - position);
        mountTargetPosition = Quaternion.Inverse(rotation) * (landingHit.point - position);
        mountStartTime = Time.time;
        currentMountDuration = Mathf.Max(0.01f, mountDuration);
        return true;
    }

    // Moves the player from the wall to the detected top surface.
    public bool UpdateMount()
    {


        if (!HasMountSurface)
            return false;

        PlayerGroundProbe.GetSurfacePose(mountCollider, out Vector3 position, out Quaternion rotation);
        Vector3 start = position + rotation * mountStartPosition;
        Vector3 target = position + rotation * mountTargetPosition;
        float t = Mathf.Clamp01((Time.time - mountStartTime + Time.fixedDeltaTime) / currentMountDuration);
        Vector3 candidate = Vector3.Lerp(start, target, t);
        TargetPosition = candidate + PlayerGroundProbe.GetPointVelocity(mountCollider, candidate) * Time.fixedDeltaTime;
        return t >= 1f;
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Checks whether a collider belongs to a climbable layer.
    bool IsClimbable(Collider collider)
    {
        return collider != null && ((1 << collider.gameObject.layer) & climbableLayer.value) != 0;
    }

    // Keeps a ledge valid briefly after the direct wall ray loses contact.
    bool IsWithinLedgeGrace()
    {
        return HasWall && Time.time - lastWallContactTime <= ledgeGraceDuration;
    }

    // Chooses the direction the player should move toward while mounting a ledge.
    Vector3 GetMountForward(Transform body, Transform forwardTransform)
    {
        Vector3 fallback = surfaceNormal.sqrMagnitude > 0.001f ? -surfaceNormal.normalized : body.forward;
        Vector3 forward = forwardTransform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (forward.sqrMagnitude < 0.001f)
            return fallback;

        forward.Normalize();
        return Vector3.Dot(forward, fallback) < 0.2f ? fallback : forward;
    }

    // Finds the upward direction along the current wall surface.
    Vector3 GetWallUp()
    {
        Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal);
        return wallUp.sqrMagnitude > 0.001f ? wallUp.normalized : Vector3.up;
    }

}

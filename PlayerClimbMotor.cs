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
    Vector3 lastWallPosition;
    Quaternion lastWallRotation;
    Vector3 surfaceNormal;
    Vector3 surfacePoint;
    float lastWallContactTime;
    Vector3 mountStartPosition;
    Vector3 mountTargetPosition;
    float mountStartTime;
    float currentMountDuration;

    public LayerMask ClimbableLayer => climbableLayer;
    public bool HasWall => currentWall != null;
    public Vector3 SurfaceNormal => surfaceNormal;

    // Attaches the player to a steep climbable wall when they hit it with enough speed.
    public bool TryAutoStick(RaycastHit lowerHit, Vector3 currentVelocity, Rigidbody rb, Transform body)
    {
        if (lowerHit.collider == null)
            return false;

        bool isSteep = Vector3.Angle(lowerHit.normal, Vector3.up) > 60f;
        bool movingIntoSurface = Vector3.Dot(currentVelocity, -lowerHit.normal) >= autoStickVelocityThreshold;

        if (!IsClimbable(lowerHit.collider) || !isSteep || !movingIntoSurface)
            return false;

        AttachToWall(lowerHit, rb, body);
        return true;
    }

    // Places the player on the wall and stops their previous physics movement.
    public void AttachToWall(RaycastHit hit, Rigidbody rb, Transform body)
    {
        if (body == null)
            return;

        surfaceNormal = hit.normal;
        surfacePoint = hit.point;
        currentWall = hit.transform;
        lastWallPosition = currentWall.position;
        lastWallRotation = currentWall.rotation;
        lastWallContactTime = Time.time;

        body.rotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);
        body.position = surfacePoint + surfaceNormal * wallOffset;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // Stops treating the current wall as an active climb surface.
    public void DetachFromWall()
    {
        currentWall = null;
    }

    // Carries the player along when the climbed wall moves or rotates.
    public void ApplyWallDelta(Transform body)
    {
        if (currentWall == null || body == null)
            return;

        Quaternion rotationDelta = currentWall.rotation * Quaternion.Inverse(lastWallRotation);
        Vector3 positionDelta = GetSurfaceDelta(currentWall, lastWallPosition, lastWallRotation, body.position);

        body.rotation = rotationDelta * body.rotation;
        body.position += positionDelta;
        surfaceNormal = rotationDelta * surfaceNormal;

        lastWallPosition = currentWall.position;
        lastWallRotation = currentWall.rotation;
    }

    // Moves the player across the wall while keeping them snapped to the surface.
    public bool MoveOnWall(Transform body, Vector2 moveInput)
    {
        if (currentWall == null || body == null)
            return false;

        Vector3 wallUp = GetWallUp();
        Vector3 wallRight = Vector3.Cross(surfaceNormal, wallUp).normalized;
        Vector3 delta = (wallRight * moveInput.x + wallUp * moveInput.y) * climbSpeed * Time.fixedDeltaTime;
        body.position += delta;

        if (Physics.Raycast(body.position, -surfaceNormal, out RaycastHit hit, wallOffset * 1.5f, climbableLayer, QueryTriggerInteraction.Ignore))
        {
            surfaceNormal = hit.normal;
            surfacePoint = hit.point;
            currentWall = hit.transform;
            lastWallPosition = currentWall.position;
            lastWallRotation = currentWall.rotation;
            lastWallContactTime = Time.time;
            body.position = hit.point + hit.normal * wallOffset;
            return true;
        }

        return IsWithinLedgeGrace();
    }

    // Turns the player so they keep facing the climbed surface.
    public void SolveClimbRotation(Transform body, float turnSpeed)
    {
        if (currentWall == null || body == null)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);
        body.rotation = Quaternion.RotateTowards(body.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
    }

    // Starts pulling the player onto the top of a ledge when a landing point is available.
    public bool TryStartMount(Transform body, Transform forwardTransform, RaycastHit upperHit, RaycastHit lowerHit, int landingMask)
    {
        if (body == null || upperHit.collider != null)
            return false;

        if (!IsClimbable(lowerHit.collider) && currentWall == null && !IsWithinLedgeGrace())
            return false;

        Vector3 forward = GetMountForward(body, forwardTransform);
        Vector3 wallUp = GetWallUp();
        Vector3[] searchOrigins =
        {
            body.position + Vector3.up * mountUpOffset + forward * mountForwardOffset,
            body.position + Vector3.up * (mountUpOffset + ledgeExtraUpOffset) + forward * mountForwardOffset,
            body.position + Vector3.up * mountUpOffset + forward * (mountForwardOffset + ledgeExtraForwardOffset),
            body.position + wallUp * mountUpOffset + forward * (mountForwardOffset + ledgeExtraForwardOffset)
        };

        RaycastHit landingHit = default;
        bool hasLanding = false;
        float searchDistance = mountSearchDownDistance + ledgeExtraDownDistance;

        for (int i = 0; i < searchOrigins.Length; i++)
        {
            if (Physics.Raycast(searchOrigins[i], Vector3.down, out landingHit, searchDistance, landingMask, QueryTriggerInteraction.Ignore))
            {
                hasLanding = true;
                break;
            }
        }

        if (!hasLanding)
            return false;

        mountStartPosition = body.position;
        mountTargetPosition = landingHit.point;
        mountStartTime = Time.time;
        currentMountDuration = Mathf.Max(0.01f, mountDuration);
        return true;
    }

    // Moves the player from the wall to the detected top surface.
    public bool UpdateMount(Transform body, out Vector3 velocityOverride)
    {
        velocityOverride = Vector3.zero;

        if (body == null)
            return true;

        float t = (Time.time - mountStartTime) / currentMountDuration;
        if (t >= 1f)
        {
            body.position = mountTargetPosition;
            return true;
        }

        body.position = Vector3.Lerp(mountStartPosition, mountTargetPosition, t);
        return false;
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
        return currentWall != null && Time.time - lastWallContactTime <= ledgeGraceDuration;
    }

    // Chooses the direction the player should move toward while mounting a ledge.
    Vector3 GetMountForward(Transform body, Transform forwardTransform)
    {
        Vector3 fallback = surfaceNormal.sqrMagnitude > 0.001f ? -surfaceNormal.normalized : body.forward;
        Vector3 forward = forwardTransform != null ? forwardTransform.forward : fallback;
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

    // Calculates how much a moving or rotating surface should carry the player.
    static Vector3 GetSurfaceDelta(Transform surface, Vector3 lastPosition, Quaternion lastRotation, Vector3 bodyPosition)
    {
        Vector3 positionDelta = surface.position - lastPosition;
        Quaternion rotationDelta = surface.rotation * Quaternion.Inverse(lastRotation);
        Vector3 localOffset = bodyPosition - lastPosition;
        Vector3 rotatedOffset = rotationDelta * localOffset;
        return positionDelta + rotatedOffset - localOffset;
    }
}

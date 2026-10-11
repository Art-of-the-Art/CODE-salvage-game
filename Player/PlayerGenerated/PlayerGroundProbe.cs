using UnityEngine;

public class PlayerGroundProbe : MonoBehaviour
{
    [Header("Ground Check")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundCheckDistance = 1.2f;
    [SerializeField] float groundProbeRadius = 0.45f;
    [SerializeField] float groundProbeOriginOffset = 0.65f;
    [SerializeField] float maxGroundAngle = 55f;
    [SerializeField] bool debugGroundCheck;

    Rigidbody playerBody;
    Collider groundCollider;
    Transform currentGround;
    Vector3 lastGroundPosition;
    Quaternion lastGroundRotation;
    Vector3 currentGroundVelocity;

    public LayerMask GroundLayer => groundLayer;
    public float MaxGroundAngle => maxGroundAngle;
    public bool IsGrounded { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Transform CurrentGround => currentGround;
    public Collider CurrentGroundCollider => groundCollider;
    public Transform CurrentSupportTransform => groundCollider != null && groundCollider.attachedRigidbody != null
        ? groundCollider.attachedRigidbody.transform
        : currentGround;
    public Vector3 CurrentGroundVelocity => currentGroundVelocity;

    void Awake() => playerBody = GetComponent<Rigidbody>();

    // Checks whether the player is standing on a valid walkable surface.
    public void Probe()
    {
        Vector3 bodyPosition = playerBody != null ? playerBody.position : transform.position;
        Vector3 rayOrigin = bodyPosition + Vector3.up * groundProbeOriginOffset;
        bool hitSomething = Physics.SphereCast(
            rayOrigin,
            groundProbeRadius,
            Vector3.down,
            out RaycastHit hit,
            groundCheckDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );

        GroundHit = hit;
        groundCollider = hit.collider;
        IsGrounded = hitSomething && Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle
            && hit.distance <= groundProbeOriginOffset - groundProbeRadius + 0.15f;

        if (IsGrounded)
        {
            GroundNormal = hit.normal;
            SetCurrentGround(hit.transform);
        }
        else
        {
            GroundNormal = Vector3.up;
            SetCurrentGround(null);
        }

        Debug.DrawRay(rayOrigin, Vector3.down * groundCheckDistance, IsGrounded ? Color.green : Color.red);

        if (!debugGroundCheck)
            return;

        if (hitSomething)
            Debug.Log($"GROUND HIT: {hit.collider.name}, angle: {Vector3.Angle(hit.normal, Vector3.up):F1}, distance: {hit.distance:F2}");
        else
            Debug.Log($"GROUND MISS: rayOrigin.y = {rayOrigin.y:F2}, playerPos.y = {transform.position.y:F2}");
    }

    // Measure support motion only. PlayerMovement adds it to walking velocity once.
    public void SampleGroundVelocity(Vector3 bodyPosition)
    {
        currentGroundVelocity = Vector3.zero;
        if (!IsGrounded || currentGround == null)
            return;

        Rigidbody supportBody = groundCollider.attachedRigidbody;
        if (supportBody != null)
            currentGroundVelocity = GetPointVelocity(groundCollider, bodyPosition);
        else
        {
            Quaternion rotationDelta = currentGround.rotation * Quaternion.Inverse(lastGroundRotation);
            Vector3 offsetFromSurfaceOrigin = bodyPosition - lastGroundPosition;
            Vector3 surfaceDelta = currentGround.position - lastGroundPosition
                + rotationDelta * offsetFromSurfaceOrigin - offsetFromSurfaceOrigin;
            currentGroundVelocity = surfaceDelta / Time.fixedDeltaTime;
        }
        lastGroundPosition = currentGround.position;
        lastGroundRotation = currentGround.rotation;
    }

    // Shared by floor and wall contacts. Read physics poses, not interpolated visuals.
    public static void GetSurfacePose(Collider surface, out Vector3 position, out Quaternion rotation)
    {
        Rigidbody body = surface.attachedRigidbody;
        position = body != null ? body.position : surface.transform.position;
        rotation = body != null ? body.rotation : surface.transform.rotation;
    }

    public static Vector3 GetPointVelocity(Collider surface, Vector3 worldPoint)
    {
        if (surface == null || surface.attachedRigidbody == null)
            return Vector3.zero;
        Rigidbody body = surface.attachedRigidbody;
        RobotMovement robot = body.GetComponent<RobotMovement>();
        if (robot != null && robot.isActiveAndEnabled)
            return robot.GetPointVelocity(worldPoint);
        return body.GetPointVelocity(worldPoint);
    }

    // Clears the current ground contact when the player leaves the floor.
    public void ClearGround()
    {
        IsGrounded = false;
        GroundHit = default;
        groundCollider = null;
        GroundNormal = Vector3.up;
        currentGroundVelocity = Vector3.zero;
        SetCurrentGround(null);
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Remembers a new ground transform and its starting pose.
    void SetCurrentGround(Transform newGround)
    {
        if (currentGround == newGround)
            return;

        currentGround = newGround;
        currentGroundVelocity = Vector3.zero;

        if (currentGround == null)
            return;

        lastGroundPosition = currentGround.position;
        lastGroundRotation = currentGround.rotation;
    }

}

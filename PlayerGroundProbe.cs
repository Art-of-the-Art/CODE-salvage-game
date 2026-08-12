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

    Transform currentGround;
    Vector3 lastGroundPosition;
    Quaternion lastGroundRotation;
    Vector3 currentGroundVelocity;

    public LayerMask GroundLayer => groundLayer;
    public bool IsGrounded { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Transform CurrentGround => currentGround;
    public Vector3 CurrentGroundVelocity => currentGroundVelocity;

    // Checks whether the player is standing on a valid walkable surface.
    public void Probe()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * groundProbeOriginOffset;
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
        IsGrounded = hitSomething && Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle;

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

    // Moves the player together with the ground currently under their feet.
    public void ApplyGroundDelta(Rigidbody rb)
    {
        currentGroundVelocity = Vector3.zero;

        if (!IsGrounded || currentGround == null || currentGround == transform || rb == null)
            return;

        Vector3 totalDelta = GetSurfaceDelta(currentGround, lastGroundPosition, lastGroundRotation, rb.position);

        if (totalDelta.sqrMagnitude > 0f)
            rb.position += totalDelta;

        currentGroundVelocity = totalDelta / Time.fixedDeltaTime;
        lastGroundPosition = currentGround.position;
        lastGroundRotation = currentGround.rotation;
    }

    // Clears the current ground contact when the player leaves the floor.
    public void ClearGround()
    {
        IsGrounded = false;
        GroundNormal = Vector3.up;
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

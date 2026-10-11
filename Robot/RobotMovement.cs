// Автор прочитал и понимает что тут происходит.

using UnityEngine;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class RobotMovement : MonoBehaviour
{
    [SerializeField] float moveSpeed = 7f;
    [SerializeField] float rotationSpeed = 50f;
    [SerializeField] float targetDistance = 30f;
    [SerializeField] float arrivalDistance = 1.5f;
    [SerializeField] float turnDoneAngle = 0.1f;
    [SerializeField] int targetAttempts = 20;
    [SerializeField] LayerMask terrainLayer;
    [SerializeField] string terrainName = "Terrain";

    Rigidbody rb;
    bool hasTarget;
    bool turning;
    Vector3 moveDirection;
    Quaternion targetRotation;
    float distanceLeft;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        stepStartPosition = stepEndPosition = rb.position;
        stepStartRotation = stepEndRotation = rb.rotation;
    }

    // The player reads the same planned step before PhysX moves either body.
    Vector3 stepStartPosition;
    Quaternion stepStartRotation;
    Vector3 stepEndPosition;
    Quaternion stepEndRotation;

    void FixedUpdate()
    {
        stepStartPosition = stepEndPosition = rb.position;
        stepStartRotation = stepEndRotation = rb.rotation;

        if (!hasTarget)
        {
            TryChooseTarget();
            return;
        }
        if (turning)
        {
            TurnTowardsTarget();
            return;
        }
        MoveTowardsTarget();
    }

    // Exact displacement of a point, including the arc around the robot's pivot.
    public Vector3 GetPointVelocity(Vector3 worldPoint)
    {
        Vector3 offsetFromRobotOrigin = worldPoint - stepStartPosition;
        Quaternion rotationDelta = stepEndRotation * Quaternion.Inverse(stepStartRotation);
        Vector3 nextPoint = stepEndPosition + rotationDelta * offsetFromRobotOrigin;
        return (nextPoint - worldPoint) / Time.fixedDeltaTime;
    }

    void TryChooseTarget()
    {
        for (int attempt = 0; attempt < targetAttempts; attempt++)
        {
            // Normalization chooses a direction on the circle, not a distance inside it.
            Vector2 direction = Random.insideUnitCircle.normalized;
            if (direction == Vector2.zero)
                direction = Vector2.up;
            Vector3 point = rb.position + new Vector3(direction.x, 0f, direction.y) * targetDistance;
            Ray ray = new Ray(new Vector3(point.x, 1000f, point.z), Vector3.down);
            if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, terrainLayer, QueryTriggerInteraction.Ignore))
                continue;
            if (!IsTerrain(hit.collider))
                continue;

            moveDirection = hit.point - rb.position;
            moveDirection.y = 0f;
            distanceLeft = moveDirection.magnitude;
            if (distanceLeft <= arrivalDistance)
                continue;

            moveDirection.Normalize();
            targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            turning = Quaternion.Angle(rb.rotation, targetRotation) > turnDoneAngle;
            hasTarget = true;
            return;
        }
    }

    bool IsTerrain(Collider candidate)
    {
        return candidate.GetComponent<Terrain>() != null
            || candidate.name == terrainName || candidate.transform.root.name == terrainName;
    }

    void TurnTowardsTarget()
    {
        // Specify the next orientation; this does not apply torque.
        stepEndRotation = Quaternion.RotateTowards(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(stepEndRotation);
        turning = Quaternion.Angle(stepEndRotation, targetRotation) > turnDoneAngle;
    }

    void MoveTowardsTarget()
    {
        if (distanceLeft <= arrivalDistance)
        {
            hasTarget = false;
            return;
        }
        float step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, distanceLeft);
        stepEndPosition = rb.position + moveDirection * step;
        rb.MovePosition(stepEndPosition);
        distanceLeft -= step;
    }
}

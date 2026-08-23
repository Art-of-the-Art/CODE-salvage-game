using UnityEngine;

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
    }

    void FixedUpdate()
    {
        if (!hasTarget)
        {
            Vector3 currentPosition = rb.position;

            for (int i = 0; i < targetAttempts; i++)
            {
                Vector2 random = Random.insideUnitCircle.normalized;
                if (random == Vector2.zero)
                    random = Vector2.right;

                Vector3 point = currentPosition + new Vector3(random.x, 0f, random.y) * targetDistance;
                Ray ray = new Ray(new Vector3(point.x, 1000f, point.z), Vector3.down);

                if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, terrainLayer))
                    continue;

                Collider collider = hit.collider;
                if (collider.GetComponent<Terrain>() == null && collider.name != terrainName && collider.transform.root.name != terrainName)
                    continue;

                moveDirection = hit.point - currentPosition;
                moveDirection.y = 0f;
                distanceLeft = moveDirection.magnitude;
                if (distanceLeft <= arrivalDistance)
                    continue;

                moveDirection.Normalize();
                targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                turning = Quaternion.Angle(rb.rotation, targetRotation) > turnDoneAngle;
                hasTarget = true;
                break;
            }
        }

        if (!hasTarget)
            return;

        if (turning)
        {
            Quaternion nextRotation = Quaternion.RotateTowards(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(nextRotation);
            turning = Quaternion.Angle(nextRotation, targetRotation) > turnDoneAngle;
            return;
        }

        if (distanceLeft <= arrivalDistance)
        {
            hasTarget = false;
            return;
        }

        float step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, distanceLeft);
        rb.MovePosition(rb.position + moveDirection * step);
        distanceLeft -= step;
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

}

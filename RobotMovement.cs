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
            Vector3 currentPosition = GetBodyPosition();

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
                turning = Quaternion.Angle(GetBodyRotation(), targetRotation) > turnDoneAngle;
                hasTarget = true;
                break;
            }
        }

        if (!hasTarget)
            return;

        if (turning)
        {
            Quaternion nextRotation = Quaternion.RotateTowards(GetBodyRotation(), targetRotation, rotationSpeed * Time.fixedDeltaTime);
            SetBodyRotation(nextRotation);
            turning = Quaternion.Angle(nextRotation, targetRotation) > turnDoneAngle;
            return;
        }

        if (distanceLeft <= arrivalDistance)
        {
            hasTarget = false;
            return;
        }

        float step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, distanceLeft);
        SetBodyPosition(GetBodyPosition() + moveDirection * step);
        distanceLeft -= step;
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Returns the physical position when possible, otherwise the object position.
    Vector3 GetBodyPosition()
    {
        return rb != null ? rb.position : transform.position;
    }

    // Moves the physical body when possible, otherwise moves the object directly.
    void SetBodyPosition(Vector3 position)
    {
        if (rb != null)
            rb.MovePosition(position);
        else
            transform.position = position;
    }

    // Returns the physical rotation when possible, otherwise the object rotation.
    Quaternion GetBodyRotation()
    {
        return rb != null ? rb.rotation : transform.rotation;
    }

    // Rotates the physical body when possible, otherwise rotates the object directly.
    void SetBodyRotation(Quaternion rotation)
    {
        if (rb != null)
            rb.MoveRotation(rotation);
        else
            transform.rotation = rotation;
    }
}

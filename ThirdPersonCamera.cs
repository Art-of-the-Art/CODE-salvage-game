using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Linked Objects")]
    public Transform player;
    public Transform cameraTransform; 

    [Header("Sensitivity")]
    public float sensitivity = 0.12f;

    [Header("Limits")]
    public float minPitch = -50f;
    public float maxPitch = 65f;
    public float defaultDistance = 5f;
    public float heightOffset = 4f;

    [Header("Collisions")]
    public float collisionRadius = 0.5f;
    public float collisionSmoothing = 10f;

    float pitch;
    float yaw;
    float currentDistance;
    float targetDistance;

    LayerMask collisionMask = LayerMask.GetMask("Default", "Environment", "Ground", "Terrain");

    float timeSinceInput;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        yaw = player.eulerAngles.y;
        pitch = 0f;
        currentDistance = defaultDistance;
    }

    void LateUpdate()
    {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        bool hasInput = mouseDelta.sqrMagnitude > 0.01f;
        if (hasInput) timeSinceInput = 0f;
        else timeSinceInput += Time.deltaTime;

        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Vector3 pivotPoint = player.position + Vector3.up * heightOffset;
        transform.position = pivotPoint;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 desiredPosition = pivotPoint - transform.forward * defaultDistance;    

        RaycastHit hit;
        if (Physics.SphereCast(
            pivotPoint,
            collisionRadius,
            (desiredPosition - pivotPoint).normalized,
            out hit,
            defaultDistance,  
            collisionMask 
        ))
        {
            targetDistance = hit.distance - collisionRadius;
        }
        else
        {
            targetDistance = defaultDistance;
        }
        targetDistance = Mathf.Clamp(targetDistance, collisionRadius, defaultDistance);

        if (targetDistance < currentDistance)
        {
            currentDistance = targetDistance;
        }
        else
        {
            currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * collisionSmoothing);
        }

        transform.position = pivotPoint - transform.forward * currentDistance;
        transform.rotation = transform.rotation;

        //pivot.position = player.position + Vector3.up * 0.5f; // Adjust the height of the pivot as needed
    }
}

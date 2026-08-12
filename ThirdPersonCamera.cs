using UnityEngine;
using UnityEngine.InputSystem;

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

    [Header("Collisions")]
    public float collisionRadius = 0.5f;
    public float collisionSmoothing = 10f;

    float pitch;
    float yaw;
    float currentDistance;
    LayerMask collisionMask;

    void Awake()
    {
        collisionMask = LayerMask.GetMask("Default", "Environment", "Ground", "Terrain");
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        yaw = player != null ? player.eulerAngles.y : transform.eulerAngles.y;
        pitch = 0f;
        currentDistance = defaultDistance;
    }

    void LateUpdate()
    {
        if (player == null || Mouse.current == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Vector3 pivotPoint = player.position + Vector3.up * heightOffset;
        Quaternion cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 cameraForward = cameraRotation * Vector3.forward;
        Vector3 desiredPosition = pivotPoint - cameraForward * defaultDistance;
        Vector3 castDirection = (desiredPosition - pivotPoint).normalized;

        float targetDistance = defaultDistance;
        if (Physics.SphereCast(pivotPoint, collisionRadius, castDirection, out RaycastHit hit, defaultDistance, collisionMask))
            targetDistance = Mathf.Clamp(hit.distance - collisionRadius, collisionRadius, defaultDistance);

        if (targetDistance < currentDistance)
            currentDistance = targetDistance;
        else
            currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * collisionSmoothing);

        transform.SetPositionAndRotation(pivotPoint - cameraForward * currentDistance, cameraRotation);
    }
}

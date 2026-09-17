using UnityEngine;

public class PlayerFrontDetector : MonoBehaviour
{
    [Header("Front Detection")]
    [SerializeField] LayerMask detectionLayer;
    [SerializeField] float detectionDistance = 1.2f;
    [SerializeField] float upperRayHeightOffset = 0.8f;
    [SerializeField] float lowerRayHeightOffset = 0.25f;

    public RaycastHit UpperHit { get; private set; }
    public RaycastHit LowerHit { get; private set; }
    public string UpperObjectName { get; private set; } = "None";
    public string LowerObjectName { get; private set; } = "None";

    // Checks what is directly in front of the player at upper and lower height.
    public void Probe(Transform originTransform, Transform forwardTransform)
    {
        UpperObjectName = "None";
        LowerObjectName = "None";
        UpperHit = default;
        LowerHit = default;

        Vector3 forward = forwardTransform.forward;
        Vector3 upperOrigin = originTransform.position + Vector3.up * upperRayHeightOffset;
        Vector3 lowerOrigin = originTransform.position + Vector3.up * lowerRayHeightOffset;

        if (Physics.Raycast(upperOrigin, forward, out RaycastHit upperHit, detectionDistance, detectionLayer, QueryTriggerInteraction.Ignore))
        {
            UpperHit = upperHit;
            UpperObjectName = upperHit.collider.name;
        }

        if (Physics.Raycast(lowerOrigin, forward, out RaycastHit lowerHit, detectionDistance, detectionLayer, QueryTriggerInteraction.Ignore))
        {
            LowerHit = lowerHit;
            LowerObjectName = lowerHit.collider.name;
        }
    }
}

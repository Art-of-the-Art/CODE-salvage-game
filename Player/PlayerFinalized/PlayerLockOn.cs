using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PlayerInputReader))]
public class PlayerLockOn : MonoBehaviour
{
    [Header("Lock-On Settings")]
    [SerializeField] Camera viewCamera;
    [SerializeField] PlayerInputReader input;
    [SerializeField] LayerMask targetMask;
    [SerializeField] LayerMask occlusionMask;
    [SerializeField, Min(0f)] float searchRadius = 50f;
    [SerializeField, Min(1f)] float searchFovMultiplier = 1.1f;
    [SerializeField, Range(0f, 0.49f)] float screenMargin = 0.2f;
    [SerializeField] string lockOnPointName = "LockOnPoint";

    Transform currentTarget;
    readonly Collider[] targetCandidates = new Collider[32];
    readonly HashSet<Transform> candidateRoots = new HashSet<Transform>();

    public Transform CurrentTarget => currentTarget;
    public bool IsLockedOn => currentTarget != null;

    void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (input == null) input = GetComponent<PlayerInputReader>();

        if (viewCamera == null)
        {
            Debug.LogError("PlayerLockOn needs a Camera reference.", this);
            enabled = false;
        }
    }

    void Update()
    {
        if (input.ConsumeLockOn())
            ToggleLockOn(); 
    }

    void ToggleLockOn()
    {
        if (currentTarget == null)
            currentTarget = FindBestTarget();
        else
            ClearTarget();
    }

    Transform FindBestTarget()
    {
        // TODO: collect robot colliders, then compare their LockOnPoint children.
        return null;
    }

    bool TryProjectCandidate(Transform lockOnPoint, out Vector3 screenPoint)
    {
        // TODO: project lockOnPoint.position and reject points outside the search cone.
        screenPoint = default;
        return false;
    }

    bool IsInsideSafeZone(Vector3 screenPoint)
    {
        // TODO: compare x/y with the 20% screen margins.
        return false;
    }

    bool HasLineOfSight(Transform lockOnPoint)
    {
        // TODO: raycast from viewCamera to lockOnPoint without rejecting its own robot.
        return false;
    }

    void ClearTarget()
    {
        currentTarget = null;
    }
}

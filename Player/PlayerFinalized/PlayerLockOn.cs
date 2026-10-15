using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
public class PlayerLockOn : MonoBehaviour
{
    [Header("Lock-On Settings")]
    [SerializeField] Camera viewCamera;
    [SerializeField] PlayerInputReader input;
    [SerializeField] LayerMask occlusionMask;
    [SerializeField, Min(0f)] float searchRadius = 150f;
    [SerializeField, Min(0f)] float trackingRadius = 200f;
    [SerializeField, Range(0f, 0.49f)] float screenMargin = 0.2f;
    [SerializeField] string lockOnPointName = "LockOnPoint";
    [SerializeField] bool lockOnAtSceneStart = true;

    Transform currentTarget;
    int targetValidationGraceFrames;

    readonly RaycastHit[] lineOfSightHits = new RaycastHit[32];

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

    void Start()
    {
        if (!lockOnAtSceneStart)
            return;

        currentTarget = FindBestTarget(false, false);
        targetValidationGraceFrames = 2;
        if (currentTarget != null)
            Debug.Log($"Lock On: {currentTarget.name} (automatic)", this);
    }

    void Update()
    {
        if (targetValidationGraceFrames > 0)
            targetValidationGraceFrames--;
        else if (currentTarget != null && !IsCurrentTargetValid())
            ClearTarget();

        if (input.ConsumeLockOn())
            ToggleLockOn();
    }

    void ToggleLockOn()
    {
        if (currentTarget == null)
        {
            currentTarget = FindBestTarget();
            string result = currentTarget != null ? currentTarget.name : "no visible target";
            Debug.Log($"Lock On: {result}", this);
        }
        else
            ClearTarget();
    }

    Transform FindBestTarget(bool requireOnScreen = true, bool requireLineOfSight = true)
    {
        GameObject[] candidates = GameObject.FindGameObjectsWithTag(lockOnPointName);
        Transform bestTarget = null;
        float bestScore = float.PositiveInfinity;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float maxDistanceSqr = searchRadius * searchRadius;

        foreach (GameObject candidate in candidates)
        {
            Transform point = candidate.transform;
            Vector3 cameraToPoint = point.position - viewCamera.transform.position;
            if (cameraToPoint.sqrMagnitude > maxDistanceSqr)
                continue;

            Vector3 screenPoint = viewCamera.WorldToScreenPoint(point.position);
            if (requireOnScreen && (screenPoint.z <= 0f ||
                screenPoint.x < 0f || screenPoint.x > Screen.width ||
                screenPoint.y < 0f || screenPoint.y > Screen.height))
                continue;

            if (requireLineOfSight && !HasLineOfSight(point))
                continue;

            float score = requireOnScreen
                ? ((Vector2)screenPoint - screenCenter).sqrMagnitude
                : Vector3.Angle(viewCamera.transform.forward, cameraToPoint);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestTarget = point;
        }

        return bestTarget;
    }

    bool IsCurrentTargetValid()
    {
        if (!currentTarget.gameObject.activeInHierarchy)
            return false;

        Vector3 cameraToTarget = currentTarget.position - viewCamera.transform.position;
        if (cameraToTarget.sqrMagnitude > trackingRadius * trackingRadius)
            return false;

        return HasLineOfSight(currentTarget);
    }

    bool TryProjectCandidate(Transform lockOnPoint, out Vector3 screenPoint)
    {
        // TODO: project lockOnPoint.position and reject points outside the search cone.
        screenPoint = default;
        return false;
    }

    public bool TryGetViewportError(out Vector2 viewportError)
    {
        viewportError = Vector2.zero;
        if (currentTarget == null)
            return false;

        Vector3 cameraToTarget = currentTarget.position - viewCamera.transform.position;
        Vector3 point = viewCamera.WorldToViewportPoint(currentTarget.position);
        if (point.z <= 0f)
        {
            Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
            Vector3 targetDirection = Vector3.ProjectOnPlane(cameraToTarget, Vector3.up);
            if (cameraForward.sqrMagnitude < 0.0001f || targetDirection.sqrMagnitude < 0.0001f)
                return false;

            float yawError = Vector3.SignedAngle(cameraForward, targetDirection, Vector3.up);
            viewportError.x = Mathf.Approximately(yawError, 0f) ? 1f : Mathf.Sign(yawError);
            return true;
        }

        float minimum = screenMargin;
        float maximum = 1f - screenMargin;

        if (point.x < minimum)
            viewportError.x = point.x - minimum;
        else if (point.x > maximum)
            viewportError.x = point.x - maximum;

        if (point.y < minimum)
            viewportError.y = point.y - minimum;
        else if (point.y > maximum)
            viewportError.y = point.y - maximum;

        return true;
    }
    
    public Vector2 ConstrainLookInput(Vector2 lookInput)
    {
        if (currentTarget == null)
            return lookInput;

        Vector3 point = viewCamera.WorldToViewportPoint(currentTarget.position);
        if (point.z <= 0f)
            return lookInput;

        float minimum = screenMargin;
        float maximum = 1f - screenMargin;

        // Camera rotation moves the target in the opposite screen direction.
        if ((point.x <= minimum && lookInput.x > 0f) ||
            (point.x >= maximum && lookInput.x < 0f))
            lookInput.x = 0f;

        if ((point.y <= minimum && lookInput.y > 0f) ||
            (point.y >= maximum && lookInput.y < 0f))
            lookInput.y = 0f;

        return lookInput;
    }

    bool HasLineOfSight(Transform lockOnPoint)
    {
        Vector3 origin = viewCamera.transform.position;
        Vector3 toTarget = lockOnPoint.position - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f) return true;

        RobotMovement robot = lockOnPoint.GetComponentInParent<RobotMovement>();
        Transform targetRoot = robot != null ? robot.transform : lockOnPoint.parent;
        int hitCount = Physics.RaycastNonAlloc(
            origin, toTarget / distance, lineOfSightHits,
            distance, occlusionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = lineOfSightHits[i].collider.transform;
            if (hit == transform || hit.IsChildOf(transform)) continue;
            if (hit == targetRoot || hit.IsChildOf(targetRoot)) continue;
            return false;
        }

        return true;
    }

    void ClearTarget()
    {
        currentTarget = null;
    }
}

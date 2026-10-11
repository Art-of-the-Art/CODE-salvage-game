using UnityEngine;

// Keeps Humanoid feet on the visible surface without moving the Rigidbody.
// Add this component to the same GameObject as the Animator and enable IK Pass.
[RequireComponent(typeof(Animator))]
public class PlayerFootIk : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Animator animator;
    [SerializeField] PlayerAnimationBridge animationBridge;
    [SerializeField] LayerMask surfaceLayers = ~0;

    [Header("Ground Search")]
    [SerializeField, Min(0f)] float rayStartHeight = 0.45f;
    [SerializeField, Min(0f)] float rayDistance = 0.8f;
    [SerializeField, Min(0f)] float soleHeight = 0.06f;

    [Header("Smoothing")]
    [SerializeField, Min(0f)] float weightFadeSpeed = 10f;
    [SerializeField, Min(0f)] float targetFollowSpeed = 25f;
    [SerializeField, Range(0f, 1f)] float runningIkWeight = 0.45f;
    [SerializeField, Min(0.01f)] float runningSpeed = 8f;

    [Header("Limits")]
    [SerializeField, Range(0f, 80f)] float maxFootAngle = 40f;
    [SerializeField, Min(0f)] float maxBodyDrop = 0.18f;
    [SerializeField, Min(0.01f)] float bodySmoothTime = 0.08f;

    readonly RaycastHit[] rayHits = new RaycastHit[8];
    readonly FootTarget leftFoot = new FootTarget(AvatarIKGoal.LeftFoot);
    readonly FootTarget rightFoot = new FootTarget(AvatarIKGoal.RightFoot);

    Transform playerRoot;
    float ikWeight;
    float bodyOffset;
    float bodyOffsetVelocity;

    sealed class FootTarget
    {
        public readonly AvatarIKGoal Goal;
        public Transform Surface;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 AnimatedPosition;
        public bool HasSurface;

        public FootTarget(AvatarIKGoal goal)
        {
            Goal = goal;
        }
    }

    void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animationBridge == null)
            animationBridge = GetComponentInParent<PlayerAnimationBridge>();

        playerRoot = animationBridge != null ? animationBridge.transform : transform.root;

        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("PlayerFootIk needs a Humanoid Animator on the same GameObject.", this);
            enabled = false;
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || animator == null)
            return;

        Vector3 characterUp = playerRoot.up;
        float wantedWeight = CalculateWantedWeight();
        ikWeight = Mathf.MoveTowards(ikWeight, wantedWeight, weightFadeSpeed * Time.deltaTime);

        bool hasLeftFoot = FindFootSurface(leftFoot, characterUp);
        bool hasRightFoot = FindFootSurface(rightFoot, characterUp);

        UpdateBodyHeight(characterUp, hasLeftFoot, hasRightFoot);
        ApplyFoot(leftFoot, hasLeftFoot);
        ApplyFoot(rightFoot, hasRightFoot);
    }

    float CalculateWantedWeight()
    {
        if (animationBridge == null || !animationBridge.ShouldUseGroundFootIk)
            return 0f;

        // Strong placement while standing, softer correction while running so feet do not look glued.
        float speed01 = Mathf.InverseLerp(0f, runningSpeed, animationBridge.LocomotionSpeed);
        return Mathf.Lerp(1f, runningIkWeight, speed01);
    }

    bool FindFootSurface(FootTarget foot, Vector3 characterUp)
    {
        foot.AnimatedPosition = animator.GetIKPosition(foot.Goal);
        Quaternion animatedRotation = animator.GetIKRotation(foot.Goal);
        Vector3 rayOrigin = foot.AnimatedPosition + characterUp * rayStartHeight;
        float castDistance = rayStartHeight + rayDistance;

        if (!TryGetClosestSurface(rayOrigin, -characterUp, castDistance, out RaycastHit hit))
        {
            foot.HasSurface = false;
            return false;
        }

        Vector3 wantedPosition = hit.point + hit.normal * soleHeight;
        Quaternion wantedRotation = CalculateFootRotation(animatedRotation, hit.normal);
        SmoothTargetWithSurface(foot, hit.transform, wantedPosition, wantedRotation);
        foot.HasSurface = true;
        return true;
    }

    bool TryGetClosestSurface(Vector3 origin, Vector3 direction, float distance, out RaycastHit closestHit)
    {
        int hitCount = Physics.RaycastNonAlloc(origin, direction, rayHits, distance,
            surfaceLayers, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        closestHit = default;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(playerRoot))
                continue;
            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    void SmoothTargetWithSurface(FootTarget foot, Transform surface,
        Vector3 wantedPosition, Quaternion wantedRotation)
    {
        if (!foot.HasSurface || foot.Surface != surface)
        {
            foot.Position = wantedPosition;
            foot.Rotation = wantedRotation;
        }
        else
        {
            // Rebuild the previous target from local space first. A moving or rotating
            // robot carries the target before visual smoothing is applied.
            foot.Position = surface.TransformPoint(foot.LocalPosition);
            foot.Rotation = surface.rotation * foot.LocalRotation;
            float follow01 = 1f - Mathf.Exp(-targetFollowSpeed * Time.deltaTime);
            foot.Position = Vector3.Lerp(foot.Position, wantedPosition, follow01);
            foot.Rotation = Quaternion.Slerp(foot.Rotation, wantedRotation, follow01);
        }

        foot.Surface = surface;
        foot.LocalPosition = surface.InverseTransformPoint(foot.Position);
        foot.LocalRotation = Quaternion.Inverse(surface.rotation) * foot.Rotation;
    }

    Quaternion CalculateFootRotation(Quaternion animatedRotation, Vector3 surfaceNormal)
    {
        Vector3 animatedUp = animatedRotation * Vector3.up;
        Vector3 limitedNormal = Vector3.RotateTowards(animatedUp, surfaceNormal,
            maxFootAngle * Mathf.Deg2Rad, 0f);
        return Quaternion.FromToRotation(animatedUp, limitedNormal) * animatedRotation;
    }

    void UpdateBodyHeight(Vector3 characterUp, bool hasLeftFoot, bool hasRightFoot)
    {
        float wantedOffset = 0f;
        if (hasLeftFoot)
            wantedOffset = Mathf.Min(wantedOffset,
                Vector3.Dot(leftFoot.Position - leftFoot.AnimatedPosition, characterUp));
        if (hasRightFoot)
            wantedOffset = Mathf.Min(wantedOffset,
                Vector3.Dot(rightFoot.Position - rightFoot.AnimatedPosition, characterUp));

        wantedOffset = Mathf.Clamp(wantedOffset, -maxBodyDrop, 0f) * ikWeight;
        bodyOffset = Mathf.SmoothDamp(bodyOffset, wantedOffset,
            ref bodyOffsetVelocity, bodySmoothTime);
        animator.bodyPosition += characterUp * bodyOffset;
    }

    void ApplyFoot(FootTarget foot, bool hasSurface)
    {
        float footWeight = hasSurface ? ikWeight : 0f;
        animator.SetIKPositionWeight(foot.Goal, footWeight);
        animator.SetIKRotationWeight(foot.Goal, footWeight);

        if (!hasSurface)
            return;

        animator.SetIKPosition(foot.Goal, foot.Position);
        animator.SetIKRotation(foot.Goal, foot.Rotation);
    }
}

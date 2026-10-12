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
    [SerializeField, HideInInspector] Vector3[] leftSolePoints;
    [SerializeField, HideInInspector] Vector3[] rightSolePoints;
    [SerializeField, HideInInspector] Quaternion leftGoalToBone = Quaternion.identity;
    [SerializeField, HideInInspector] Quaternion rightGoalToBone = Quaternion.identity;
    [SerializeField, Min(0f)] float soleSurfaceMargin = 0.008f;

    [Header("Smoothing")]
    [SerializeField, Min(0f)] float weightFadeSpeed = 10f;
    [SerializeField, Min(0f)] float targetFollowSpeed = 25f;
    [SerializeField, Range(0f, 1f)] float runningIkWeight = 1f;
    [SerializeField, Min(0.01f)] float runningSpeed = 8f;

    [Header("Limits")]
    [SerializeField, Range(0f, 80f)] float maxFootAngle = 40f;
    [SerializeField, Min(0f)] float maxBodyDrop = 0.18f;
    [SerializeField, Min(0.01f)] float bodySmoothTime = 0.08f;
    [SerializeField, Min(0f)] float maxSlopeBodyDrop = 0.35f;
    [SerializeField, Range(0f, 1f)] float kneeHintWeight = 0.6f;

    readonly RaycastHit[] rayHits = new RaycastHit[8];
    readonly FootTarget leftFoot = new FootTarget(AvatarIKGoal.LeftFoot);
    readonly FootTarget rightFoot = new FootTarget(AvatarIKGoal.RightFoot);

    Transform playerRoot;
    PlayerClimbIk climbIk;
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
        public float Lift;
        public Vector3 GroundPoint;
        public Vector3 GroundNormal;
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
        climbIk = GetComponent<PlayerClimbIk>();

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

        // One callback owns both foot goals. Climbing also writes the hands here,
        // avoiding callback order conflicts between two IK components.
        if (climbIk != null && climbIk.ApplyContacts())
        {
            bodyOffset = Mathf.MoveTowards(bodyOffset, 0f, Time.deltaTime);
            animator.bodyPosition += characterUp * bodyOffset;
            return;
        }

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
        // Position must reach the surface at every speed. Soft position weights
        // leave the running clip (whose sole goes below zero) partly underground.
        return 1f;
    }

    bool FindFootSurface(FootTarget foot, Vector3 characterUp)
    {
        // Retargeted clips can contain IK goals collapsed under the pelvis.
        // The animated ankle bone still contains the actual forward/backward stride.
        var bone = animator.GetBoneTransform(foot.Goal == AvatarIKGoal.LeftFoot
            ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        foot.AnimatedPosition = bone.position;
        Quaternion goalToBone = foot.Goal == AvatarIKGoal.LeftFoot ? leftGoalToBone : rightGoalToBone;
        // Use the authored bone orientation: imported IK tracks may have stale rotations.
        Quaternion animatedRotation = bone.rotation * Quaternion.Inverse(goalToBone);
        // Baked from the actual clip on this Avatar, and blended by the same tree.
        // A swinging foot keeps its authored clearance above the sampled surface.
        foot.Lift = Mathf.Max(0f, animator.GetFloat(
            foot.Goal == AvatarIKGoal.LeftFoot ? "leftFootLift" : "rightFootLift"));
        // Start above the character's ground plane, even if the raw clip penetrates
        // an uphill surface. A ray starting inside the slope would miss that surface.
        float animatedHeight = Vector3.Dot(foot.AnimatedPosition - playerRoot.position, characterUp);
        float searchLift = Mathf.Max(rayStartHeight, 1f - animatedHeight);
        Vector3 rayOrigin = foot.AnimatedPosition + characterUp * searchLift;
        // A lifted running foot must still search below the root ground plane.
        float castDistance = searchLift + Mathf.Max(0f, animatedHeight) + rayDistance;

        if (!TryGetClosestSurface(rayOrigin, -characterUp, castDistance, out RaycastHit hit))
        {
            foot.HasSurface = false;
            return false;
        }

        foot.GroundPoint = hit.point;
        foot.GroundNormal = hit.normal;
        float bottomHeight = foot.Goal == AvatarIKGoal.LeftFoot
            ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight;
        Vector3 wantedPosition = hit.point + characterUp * (Mathf.Max(soleHeight, bottomHeight) + foot.Lift);
        Quaternion wantedRotation = CalculateFootRotation(animatedRotation, hit.normal);
        SmoothTargetWithSurface(foot, hit.transform, wantedPosition, wantedRotation);
        // Predict the final bone rotation and keep the actual shoe shape above the plane.
        // A fixed ankle height misses toes pointing down during a running stride.
        float speed01 = Mathf.InverseLerp(0f, runningSpeed, animationBridge.LocomotionSpeed);
        float rotationWeight = Mathf.Lerp(1f, runningIkWeight, speed01) * ikWeight;
        Quaternion finalRotation = Quaternion.Slerp(bone.rotation, foot.Rotation * goalToBone, rotationWeight);
        var points = foot.Goal == AvatarIKGoal.LeftFoot ? leftSolePoints : rightSolePoints;
        float lowest = 0f;
        if (points != null)
            foreach (var point in points)
                lowest = Mathf.Min(lowest, Vector3.Dot(finalRotation * Vector3.Scale(point, bone.lossyScale), hit.normal));
        float neededHeight = (-lowest + soleSurfaceMargin) / Mathf.Max(0.5f, Vector3.Dot(characterUp, hit.normal));
        foot.Position += characterUp * Mathf.Max(0f, neededHeight - Mathf.Max(soleHeight, bottomHeight));
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
            if (Vector3.Dot(hit.normal, playerRoot.up) < 0.5f)
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
            // Never smooth the authored stride: delaying x/z pulls both feet under
            // the pelvis when the gait changes. Only the terrain correction is smoothed.
            foot.Position = wantedPosition;
            foot.Rotation = Quaternion.Slerp(foot.Rotation, wantedRotation, follow01);
        }

        foot.Surface = surface;
        foot.LocalPosition = surface.InverseTransformPoint(foot.Position);
        foot.LocalRotation = Quaternion.Inverse(surface.rotation) * foot.Rotation;
    }

    Quaternion CalculateFootRotation(Quaternion animatedRotation, Vector3 surfaceNormal)
    {
        Vector3 animatedUp = playerRoot.up;
        Vector3 limitedNormal = Vector3.RotateTowards(animatedUp, surfaceNormal,
            maxFootAngle * Mathf.Deg2Rad, 0f);
        return Quaternion.FromToRotation(animatedUp, limitedNormal) * animatedRotation;
    }

    void UpdateBodyHeight(Vector3 characterUp, bool hasLeftFoot, bool hasRightFoot)
    {
        float wantedOffset = 0f;
        if (hasLeftFoot)
            wantedOffset = Mathf.Min(wantedOffset,
                Vector3.Dot(leftFoot.GroundPoint - playerRoot.position, characterUp));
        if (hasRightFoot)
            wantedOffset = Mathf.Min(wantedOffset,
                Vector3.Dot(rightFoot.GroundPoint - playerRoot.position, characterUp));

        // Lower the pelvis toward the lower support. This leaves the uphill knee
        // free to bend instead of extending both legs and lifting the downhill foot.
        float heightDifference = hasLeftFoot && hasRightFoot
            ? Mathf.Abs(Vector3.Dot(leftFoot.Position - rightFoot.Position, characterUp)
                - leftFoot.Lift + rightFoot.Lift) : 0f;
        float dropLimit = Mathf.Max(maxBodyDrop, Mathf.Min(maxSlopeBodyDrop, heightDifference));
        wantedOffset = Mathf.Clamp(wantedOffset, -dropLimit, 0f) * ikWeight;
        bodyOffset = Mathf.SmoothDamp(bodyOffset, wantedOffset,
            ref bodyOffsetVelocity, bodySmoothTime);
        animator.bodyPosition += characterUp * bodyOffset;
    }

    void ApplyFoot(FootTarget foot, bool hasSurface)
    {
        float footWeight = hasSurface ? ikWeight : 0f;
        animator.SetIKPositionWeight(foot.Goal, footWeight);
        float speed01 = Mathf.InverseLerp(0f, runningSpeed, animationBridge.LocomotionSpeed);
        float rotationWeight = Mathf.Lerp(1f, runningIkWeight, speed01);
        animator.SetIKRotationWeight(foot.Goal, footWeight * rotationWeight);
        bool left = foot.Goal == AvatarIKGoal.LeftFoot;
        var thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
        var hint = left ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee;
        animator.SetIKHintPositionWeight(hint, footWeight * kneeHintWeight);
        if (thigh != null)
            animator.SetIKHintPosition(hint, thigh.position + animator.transform.forward * 0.5f
                - playerRoot.up * 0.25f);

        if (!hasSurface)
            return;

        animator.SetIKPosition(foot.Goal, foot.Position);
        animator.SetIKRotation(foot.Goal, foot.Rotation);
    }
}

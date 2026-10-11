using UnityEngine;

// Called by PlayerFootIk from OnAnimatorIK so exactly one callback owns each goal.
[RequireComponent(typeof(Animator), typeof(PlayerFootIk))]
public class PlayerClimbIk : MonoBehaviour
{
    [SerializeField, Min(0f)] float contactOffset = 0.04f;
    [SerializeField, Min(0.1f)] float probeDistance = 1.5f;
    [SerializeField, Min(0.1f)] float maxContactStretch = 0.45f;
    [SerializeField, Min(0f)] float weightSpeed = 10f;
    [SerializeField, Range(0f, 1f)] float footRotationWeight = 0.65f;

    Animator animator;
    PlayerAnimationBridge bridge;
    PlayerClimbMotor motor;
    float weight;
    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly Contact[] contacts =
    {
        new Contact(AvatarIKGoal.LeftHand, "leftHandPlant"),
        new Contact(AvatarIKGoal.RightHand, "rightHandPlant"),
        new Contact(AvatarIKGoal.LeftFoot, "leftFootPlant"),
        new Contact(AvatarIKGoal.RightFoot, "rightFootPlant")
    };

    sealed class Contact
    {
        public readonly AvatarIKGoal Goal;
        public readonly string Phase;
        public Collider Collider;
        public Vector3 LocalPoint;
        public Vector3 LocalNormal;
        public float Weight;
        public bool Planted;
        public Contact(AvatarIKGoal goal, string phase) { Goal = goal; Phase = phase; }
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        bridge = GetComponentInParent<PlayerAnimationBridge>();
        motor = GetComponentInParent<PlayerClimbMotor>();
    }

    public bool ApplyContacts()
    {
        if (animator == null || bridge == null || motor == null) return false;
        bool climbing = bridge.IsClimbing && motor.HasWall;
        weight = Mathf.MoveTowards(weight, climbing ? 1f : 0f, weightSpeed * Time.deltaTime);
        if (weight < 0.001f)
        {
            foreach (var contact in contacts) { contact.Planted = false; contact.Collider = null; }
            SetHandWeightsToZero();
            return false;
        }
        foreach (var contact in contacts) UpdateContact(contact, climbing);
        return true;
    }

    void UpdateContact(Contact contact, bool climbing)
    {
        Vector3 animated = animator.GetIKPosition(contact.Goal);
        Quaternion rotation = animator.GetIKRotation(contact.Goal);
        float phase = bridge.IsClimbMoving ? Mathf.Clamp01(animator.GetFloat(contact.Phase)) : 1f;
        bool valid = contact.Collider != null && contact.Collider.enabled
            && contact.Collider.gameObject.activeInHierarchy;
        Vector3 target = valid ? contact.Collider.transform.TransformPoint(contact.LocalPoint) : animated;

        // Release the old grip during swing or before the body can overextend a limb.
        if (phase < 0.3f || !valid || Vector3.Distance(target, animated) > maxContactStretch)
            contact.Planted = false;
        if (climbing && !contact.Planted)
        {
            valid = FindContact(animated, motor.SurfaceNormal, out var hit);
            if (valid)
            {
                contact.Collider = hit.collider;
                target = hit.point + hit.normal * contactOffset;
                contact.LocalPoint = hit.collider.transform.InverseTransformPoint(target);
                contact.LocalNormal = hit.collider.transform.InverseTransformDirection(hit.normal);
                contact.Planted = phase > 0.65f;
            }
            else contact.Collider = null;
        }

        contact.Weight = Mathf.MoveTowards(contact.Weight, valid ? phase : 0f, weightSpeed * Time.deltaTime);
        float goalWeight = contact.Weight * weight;
        animator.SetIKPositionWeight(contact.Goal, goalWeight);
        if (valid) animator.SetIKPosition(contact.Goal, target);

        bool foot = contact.Goal == AvatarIKGoal.LeftFoot || contact.Goal == AvatarIKGoal.RightFoot;
        animator.SetIKRotationWeight(contact.Goal, foot ? goalWeight * footRotationWeight : 0f);
        if (foot && valid)
        {
            Vector3 normal = contact.Collider.transform.TransformDirection(contact.LocalNormal);
            animator.SetIKRotation(contact.Goal, Quaternion.FromToRotation(rotation * Vector3.up, normal) * rotation);
        }
        if (foot)
        {
            // Forward hints bend knees toward the wall rather than through the pelvis.
            bool left = contact.Goal == AvatarIKGoal.LeftFoot;
            var thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var hint = left ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee;
            animator.SetIKHintPositionWeight(hint, goalWeight * 0.5f);
            if (thigh != null) animator.SetIKHintPosition(hint, thigh.position + animator.transform.forward * 0.4f);
        }
    }

    bool FindContact(Vector3 animated, Vector3 normal, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(animated + normal * 0.3f, -normal,
            hits, probeDistance, motor.ClimbableLayer, QueryTriggerInteraction.Ignore);
        closest = default;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.transform.IsChildOf(bridge.transform)
                || Vector3.Dot(hit.normal, normal) < 0.5f || hit.distance >= distance) continue;
            closest = hit;
            distance = hit.distance;
        }
        return distance < float.PositiveInfinity;
    }

    void SetHandWeightsToZero()
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
    }
}

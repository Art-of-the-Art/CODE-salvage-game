using UnityEngine;

// Converts traversal facts to visual animation. Rigidbody movement stays in PlayerMovement.
public class PlayerAnimationBridge : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] Transform visualRoot;
    [SerializeField] Animator animator;
    [SerializeField] RuntimeAnimatorController animationController;
    [SerializeField] float locomotionCrossFadeDuration = 0.08f;
    [SerializeField] float locomotionSpeedDampTime = 0.12f;
    [SerializeField] float jumpCrossFadeDuration = 0.06f;
    [SerializeField] float fallCrossFadeDuration = 0.12f;
    [SerializeField] float landingCrossFadeDuration = 0.1f;
    [SerializeField] float landingExitDelay = 0.12f;
    [SerializeField] float climbPlaybackRampDuration = 0.1f;
    [SerializeField] float maxClimbPlaybackSpeed = 0.8f;
    [SerializeField] float maxDownwardModelOffset = 0.35f;
    [SerializeField] float fallingVelocityThreshold = -1f;
    [SerializeField] float minimumJumpStateTime = 0.08f;

    static readonly int Locomotion = Animator.StringToHash("Locomotion");
    static readonly int Climb = Animator.StringToHash("Climb");
    static readonly int Jump = Animator.StringToHash("Jump");
    static readonly int JumpInPlace = Animator.StringToHash("JumpInPlace");
    static readonly int Fall = Animator.StringToHash("Fall");
    static readonly int Land = Animator.StringToHash("Land");
    static readonly int Speed = Animator.StringToHash("locomotionSpeed");
    static readonly int ClimbSpeed = Animator.StringToHash("climbPlaybackSpeed");

    bool grounded = true;
    bool falling;
    bool landing;
    bool hasFall;
    bool hasLand;
    float jumpStartedAt;
    float landingStartedAt;
    float climbRamp;
    float desiredLocomotionSpeed;
    Vector3 modelBaseLocalPosition;

    public Transform ModelTransform => visualRoot;
    public bool ShouldUseGroundFootIk => grounded && !IsClimbing;
    public bool IsClimbing { get; private set; }
    public bool IsClimbMoving { get; private set; }
    public float LocomotionSpeed => desiredLocomotionSpeed;

    void Awake()
    {
        if (visualRoot != null)
            animator = visualRoot.GetComponentInChildren<Animator>(true);
        if (visualRoot == null || animator == null)
        {
            Debug.LogError("PlayerAnimationBridge needs a visual root with an Animator.", this);
            enabled = false;
            return;
        }
        if (animationController != null)
            animator.runtimeAnimatorController = animationController;
        animator.applyRootMotion = false;
        modelBaseLocalPosition = visualRoot.localPosition;
        hasFall = animator.HasState(0, Fall);
        hasLand = animator.HasState(0, Land);
    }

    void Update()
    {
        if (animator == null) return;
        animator.SetFloat(Speed, desiredLocomotionSpeed, locomotionSpeedDampTime, Time.deltaTime);
        UpdateClimbPlayback();

        if (landing && Time.time - landingStartedAt >= landingExitDelay)
        {
            landing = false;
            EnterState(Locomotion, landingCrossFadeDuration);
        }
        if (hasFall && !grounded && !IsClimbing && falling
            && Time.time - jumpStartedAt >= minimumJumpStateTime)
            EnterState(Fall, fallCrossFadeDuration);
    }

    public void SetMovementState(bool onGround, float yVelocity, float speed,
        bool climbing, bool climbingMoving = true)
    {
        bool wasGrounded = grounded;
        grounded = onGround && !climbing;
        IsClimbing = climbing;
        IsClimbMoving = climbing && climbingMoving;
        falling = !grounded && !climbing && yVelocity < fallingVelocityThreshold;
        desiredLocomotionSpeed = grounded ? Mathf.Max(0f, speed) : 0f;

        if (climbing)
        {
            landing = false;
            EnterState(Climb, locomotionCrossFadeDuration);
        }
        else if (!grounded)
            landing = false;
        else if (!wasGrounded && hasLand)
        {
            landing = true;
            landingStartedAt = Time.time;
            EnterState(Land, landingCrossFadeDuration);
        }
        else if (!landing)
            EnterState(Locomotion, wasGrounded ? locomotionCrossFadeDuration : landingCrossFadeDuration);
    }

    public void PlayJump(bool hasMoveInput)
    {
        jumpStartedAt = Time.time;
        grounded = falling = landing = IsClimbing = IsClimbMoving = false;
        desiredLocomotionSpeed = 0f;
        climbRamp = 0f;
        EnterState(hasMoveInput ? Jump : JumpInPlace, jumpCrossFadeDuration);
    }

    public void SetGroundSpeedImmediately(float speed)
    {
        // PlayerMovement already supplies the S-curve; damping it again delays the pose.
        desiredLocomotionSpeed = Mathf.Max(0f, speed);
        animator.SetFloat(Speed, desiredLocomotionSpeed);
    }

    void UpdateClimbPlayback()
    {
        if (!IsClimbing)
            climbRamp = 0f;
        else if (IsClimbMoving)
            climbRamp = Mathf.MoveTowards(climbRamp, 1f,
                Time.deltaTime / Mathf.Max(0.01f, climbPlaybackRampDuration));
        float speed = IsClimbMoving ? Mathf.SmoothStep(0f, maxClimbPlaybackSpeed, climbRamp) : 0f;
        animator.SetFloat(ClimbSpeed, speed);
    }

    void EnterState(int state, float duration)
    {
        // Repeated crossfades restart the blend and prevent it from ever completing.
        int destination = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0).shortNameHash
            : animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (destination != state)
            animator.CrossFadeInFixedTime(state, duration);
    }

    public void SetModelVerticalOffset(float offset)
    {
        visualRoot.localPosition = modelBaseLocalPosition
            + Vector3.up * Mathf.Clamp(offset, -maxDownwardModelOffset, maxDownwardModelOffset);
    }

    public void ClearModelVerticalOffset() => SetModelVerticalOffset(0f);

    public void FaceClimbSurface(Vector3 surfaceNormal)
    {
        if (surfaceNormal.sqrMagnitude < 0.001f) return;
        Vector3 forward = -surfaceNormal.normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(transform.up, forward);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.forward, forward);
        visualRoot.rotation = Quaternion.LookRotation(forward, up.normalized);
    }

    public void RotateTowardsVelocity(Vector3 velocity, float turnSpeed)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (horizontal.sqrMagnitude < 0.1f) return;
        visualRoot.rotation = Quaternion.RotateTowards(visualRoot.rotation,
            Quaternion.LookRotation(horizontal), turnSpeed * Time.fixedDeltaTime);
    }

    public string GetDebugInfo()
    {
        if (animator == null) return "Animator missing";
        var state = animator.GetCurrentAnimatorStateInfo(0);
        string name = state.IsName("Locomotion") ? "Locomotion" :
            state.IsName("Climb") ? "Climb" : state.IsName("Jump") ? "Jump" :
            state.IsName("JumpInPlace") ? "JumpInPlace" : state.IsName("Fall") ? "Fall" : "Land";
        return "Anim: " + name + " phase=" + Mathf.Repeat(state.normalizedTime, 1f).ToString("F2")
            + "\nSpeed relative/blend: " + desiredLocomotionSpeed.ToString("F2")
            + "/" + animator.GetFloat(Speed).ToString("F2");
    }
}


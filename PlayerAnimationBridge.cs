using UnityEngine;

public class PlayerAnimationBridge : MonoBehaviour
{
    const float LocomotionKeepThreshold = 0.001f;
    const float RotateVelocityThresholdSqr = 0.1f;

    [Header("Animator")]
    [SerializeField] Transform visualRoot;
    [SerializeField] Animator animator;
    [SerializeField] string walkingParameter = "isWalking";
    [SerializeField] string runningParameter = "isRunning";
    [SerializeField] string climbingParameter = "isClimbing";
    [SerializeField] string playbackSpeedParameter = "runPlaybackSpeed";
    [SerializeField] string groundedParameter = "isGrounded";
    [SerializeField] string verticalVelocityParameter = "verticalVelocity";
    [SerializeField] string fallingParameter = "isFalling";
    [SerializeField] string jumpTriggerParameter = "jumpRequested";
    [SerializeField] string jumpInPlaceTriggerParameter = "jumpInPlaceRequested";
    [SerializeField] string idleStateName = "Idle";
    [SerializeField] string walkStateName = "Walk";
    [SerializeField] string runStateName = "Run";
    [SerializeField] string climbStateName = "Climb";
    [SerializeField] string jumpStateName = "Jump";
    [SerializeField] string jumpInPlaceStateName = "JumpInPlace";
    [SerializeField] string fallStateName = "Fall";
    [SerializeField] string landStateName = "Land";
    [SerializeField] float locomotionCrossFadeDuration = 0.08f;
    [SerializeField] float idleCrossFadeDuration = 0.08f;
    [SerializeField] float jumpCrossFadeDuration = 0.06f;
    [SerializeField] float fallCrossFadeDuration = 0.12f;
    [SerializeField] float landingCrossFadeDuration = 0.1f;
    [SerializeField] float landingExitDelay = 0.12f;
    [SerializeField] float locomotionRampDuration = 0.1f;
    [SerializeField] float maxWalkPlaybackSpeed = 1.1f;
    [SerializeField] float maxRunPlaybackSpeed = 1f;
    [SerializeField] float maxClimbPlaybackSpeed = 0.8f;
    [SerializeField] float maxDownwardModelOffset = 0.35f;
    [SerializeField] float fallingVelocityThreshold = -1f;
    [SerializeField] float minimumJumpStateTime = 0.08f;
    [SerializeField] bool useDirectCrossFade = true;

    bool desiredWalking;
    bool desiredRunning;
    bool desiredClimbing;
    bool desiredClimbMoving;
    bool currentlyInWalk;
    bool currentlyInRun;
    bool currentlyInClimb;
    bool isGroundedAnimation = true;
    bool isFallingAnimation;
    bool landingAnimationActive;
    bool hasIdleState;
    bool hasWalkState;
    bool hasRunState;
    bool hasClimbState;
    bool hasJumpState;
    bool hasJumpInPlaceState;
    bool hasFallState;
    bool hasLandState;
    bool hasWalkingParameter;
    bool hasRunningParameter;
    bool hasClimbingParameter;
    bool hasPlaybackSpeedParameter;
    bool hasGroundedParameter;
    bool hasVerticalVelocityParameter;
    bool hasFallingParameter;
    bool hasJumpTriggerParameter;
    bool hasJumpInPlaceTriggerParameter;
    float locomotionRamp01;
    float jumpStartedAt;
    float landingStartedAt;
    int idleStateHash;
    int walkStateHash;
    int runStateHash;
    int climbStateHash;
    int jumpStateHash;
    int jumpInPlaceStateHash;
    int fallStateHash;
    int landStateHash;
    int walkingParameterHash;
    int runningParameterHash;
    int climbingParameterHash;
    int playbackSpeedParameterHash;
    int groundedParameterHash;
    int verticalVelocityParameterHash;
    int fallingParameterHash;
    int jumpTriggerParameterHash;
    int jumpInPlaceTriggerParameterHash;
    Transform hipsTransform;
    Vector3 modelBaseLocalPosition;
    float modelVerticalOffset;

    public Transform ModelTransform => visualRoot;

    void Awake()
    {


        walkingParameterHash = Animator.StringToHash(walkingParameter);
        runningParameterHash = Animator.StringToHash(runningParameter);
        climbingParameterHash = Animator.StringToHash(climbingParameter);
        playbackSpeedParameterHash = Animator.StringToHash(playbackSpeedParameter);
        groundedParameterHash = Animator.StringToHash(groundedParameter);
        verticalVelocityParameterHash = Animator.StringToHash(verticalVelocityParameter);
        fallingParameterHash = Animator.StringToHash(fallingParameter);
        jumpTriggerParameterHash = Animator.StringToHash(jumpTriggerParameter);
        jumpInPlaceTriggerParameterHash = Animator.StringToHash(jumpInPlaceTriggerParameter);
        idleStateHash = Animator.StringToHash(idleStateName);
        walkStateHash = Animator.StringToHash(walkStateName);
        runStateHash = Animator.StringToHash(runStateName);
        climbStateHash = Animator.StringToHash(climbStateName);
        jumpStateHash = Animator.StringToHash(jumpStateName);
        jumpInPlaceStateHash = Animator.StringToHash(jumpInPlaceStateName);
        fallStateHash = Animator.StringToHash(fallStateName);
        landStateHash = Animator.StringToHash(landStateName);


        animator.applyRootMotion = false;
        hipsTransform = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hipsTransform == null)
            hipsTransform = animator.transform.Find("mixamorig:Hips");
        modelBaseLocalPosition = ModelTransform.localPosition;

        hasIdleState = animator.HasState(0, idleStateHash);
        hasWalkState = animator.HasState(0, walkStateHash);
        hasRunState = animator.HasState(0, runStateHash);
        hasClimbState = animator.HasState(0, climbStateHash);
        hasJumpState = animator.HasState(0, jumpStateHash);
        hasJumpInPlaceState = animator.HasState(0, jumpInPlaceStateHash);
        hasFallState = animator.HasState(0, fallStateHash);
        hasLandState = animator.HasState(0, landStateHash);

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            switch (parameter.nameHash)
            {
                case int hash when hash == walkingParameterHash: hasWalkingParameter = true; break;
                case int hash when hash == runningParameterHash: hasRunningParameter = true; break;
                case int hash when hash == climbingParameterHash: hasClimbingParameter = true; break;
                case int hash when hash == playbackSpeedParameterHash: hasPlaybackSpeedParameter = true; break;
                case int hash when hash == groundedParameterHash: hasGroundedParameter = true; break;
                case int hash when hash == verticalVelocityParameterHash: hasVerticalVelocityParameter = true; break;
                case int hash when hash == fallingParameterHash: hasFallingParameter = true; break;
                case int hash when hash == jumpTriggerParameterHash: hasJumpTriggerParameter = true; break;
                case int hash when hash == jumpInPlaceTriggerParameterHash: hasJumpInPlaceTriggerParameter = true; break;
            }
        }

        SetBoolIfPresent(groundedParameterHash, hasGroundedParameter, true);
        SetBoolIfPresent(walkingParameterHash, hasWalkingParameter, false);
        SetBoolIfPresent(runningParameterHash, hasRunningParameter, false);
        SetBoolIfPresent(climbingParameterHash, hasClimbingParameter, false);
        SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 1f);
        SetFloatIfPresent(verticalVelocityParameterHash, hasVerticalVelocityParameter, 0f);
        SetBoolIfPresent(fallingParameterHash, hasFallingParameter, false);
    }

    void Update()
    {

        bool wantsLocomotion = desiredWalking || desiredRunning || desiredClimbing;
        bool currentAnimatorStateIsLocomotion = IsCurrentState(walkStateHash) || IsCurrentState(runStateHash) || IsCurrentState(climbStateHash);

        if (!wantsLocomotion)
        {
            locomotionRamp01 = 0f;
            SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 1f);

            if (currentAnimatorStateIsLocomotion && isGroundedAnimation && useDirectCrossFade && hasIdleState && !landingAnimationActive)
                animator.CrossFadeInFixedTime(idleStateHash, idleCrossFadeDuration);
        }
        else
        {
            float rampStep = locomotionRampDuration > 0f ? Time.deltaTime / locomotionRampDuration : 1f;
            locomotionRamp01 = Mathf.MoveTowards(locomotionRamp01, 1f, rampStep);

            float maxPlaybackSpeed = maxWalkPlaybackSpeed;
            if (desiredClimbing)
                maxPlaybackSpeed = desiredClimbMoving ? maxClimbPlaybackSpeed : 0f;
            else if (desiredRunning)
                maxPlaybackSpeed = maxRunPlaybackSpeed;

            float eased = locomotionRamp01 * locomotionRamp01 * (3f - 2f * locomotionRamp01);
            float playbackSpeed = desiredClimbing && !desiredClimbMoving ? 0f : Mathf.Max(0.05f, eased * maxPlaybackSpeed);
            SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, playbackSpeed);

            if (currentlyInWalk || currentlyInRun || currentlyInClimb)
            {
                bool keepWalking = desiredWalking || (locomotionRamp01 > LocomotionKeepThreshold && currentlyInWalk);
                bool keepRunning = desiredRunning || (locomotionRamp01 > LocomotionKeepThreshold && currentlyInRun);
                bool keepClimbing = desiredClimbing || (locomotionRamp01 > LocomotionKeepThreshold && currentlyInClimb);
                SetLocomotionBools(keepWalking, keepRunning, keepClimbing);
            }
        }

        if (landingAnimationActive && Time.time - landingStartedAt >= landingExitDelay)
        {
            landingAnimationActive = false;
            CrossFadeToCurrentLocomotion(landingCrossFadeDuration);
        }

        if (useDirectCrossFade && hasFallState && !isGroundedAnimation && !desiredClimbing && isFallingAnimation && Time.time - jumpStartedAt >= minimumJumpStateTime)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName(fallStateName))
                animator.CrossFadeInFixedTime(fallStateHash, fallCrossFadeDuration);
        }
    }

    // Applies the movement state that should be visible on the player model.
    public void SetMovementState(bool grounded, float yVelocity, bool walking, bool running, bool climbing, bool climbingMoving = true)
    {

        bool wasGrounded = isGroundedAnimation;
        desiredClimbing = climbing;
        desiredClimbMoving = climbing && climbingMoving;
        desiredWalking = grounded && walking && !climbing;
        desiredRunning = grounded && running && !climbing;
        isGroundedAnimation = grounded && !climbing;
        isFallingAnimation = !isGroundedAnimation && !climbing && yVelocity < fallingVelocityThreshold;

        SetBoolIfPresent(groundedParameterHash, hasGroundedParameter, isGroundedAnimation);
        SetFloatIfPresent(verticalVelocityParameterHash, hasVerticalVelocityParameter, yVelocity);
        SetBoolIfPresent(fallingParameterHash, hasFallingParameter, isFallingAnimation);
        SetLocomotionBools(desiredWalking || currentlyInWalk, desiredRunning || currentlyInRun, desiredClimbing || currentlyInClimb);

        if (climbing)
        {
            landingAnimationActive = false;
            UpdateLocomotionCrossFade();
            return;
        }

        if (!grounded)
        {
            landingAnimationActive = false;
            ClearLocomotionState();
            return;
        }

        if (!wasGrounded)
        {
            currentlyInWalk = desiredWalking;
            currentlyInRun = desiredRunning;
            currentlyInClimb = false;
            SetLocomotionBools(desiredWalking, desiredRunning, false);

            if (hasLandState && useDirectCrossFade)
            {
                landingAnimationActive = true;
                landingStartedAt = Time.time;
                animator.CrossFadeInFixedTime(landStateHash, landingCrossFadeDuration);
                return;
            }

            CrossFadeToCurrentLocomotion(landingCrossFadeDuration);
            return;
        }

        UpdateLocomotionCrossFade();
    }

    // Plays either the moving jump or the standing jump animation branch.
    public void PlayJump(bool hasMoveInput)
    {

        jumpStartedAt = Time.time;
        landingAnimationActive = false;
        isGroundedAnimation = false;
        isFallingAnimation = false;
        desiredWalking = false;
        desiredRunning = false;
        desiredClimbing = false;
        desiredClimbMoving = false;
        ClearLocomotionState();

        SetBoolIfPresent(groundedParameterHash, hasGroundedParameter, false);
        SetBoolIfPresent(fallingParameterHash, hasFallingParameter, false);
        SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 1f);

        if (!hasMoveInput)
        {
            if (hasJumpTriggerParameter)
                animator.ResetTrigger(jumpTriggerParameterHash);
            if (hasJumpInPlaceTriggerParameter)
            {
                animator.ResetTrigger(jumpInPlaceTriggerParameterHash);
                animator.SetTrigger(jumpInPlaceTriggerParameterHash);
            }
            if (useDirectCrossFade && hasJumpInPlaceState)
                animator.CrossFadeInFixedTime(jumpInPlaceStateHash, jumpCrossFadeDuration);
            return;
        }

        if (hasJumpInPlaceTriggerParameter)
            animator.ResetTrigger(jumpInPlaceTriggerParameterHash);
        if (hasJumpTriggerParameter)
        {
            animator.ResetTrigger(jumpTriggerParameterHash);
            animator.SetTrigger(jumpTriggerParameterHash);
        }
        if (useDirectCrossFade && hasJumpState)
            animator.CrossFadeInFixedTime(jumpStateHash, jumpCrossFadeDuration);
    }

    // Builds a compact description of the current animator state for the debug HUD.
    public string GetDebugInfo()
    {

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        string stateName = ResolveStateName(state.shortNameHash);
        string clipName = "none";
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        if (clips != null && clips.Length > 0 && clips[0].clip != null)
            clipName = clips[0].clip.name;

        string nextStateName = "none";
        string nextClipName = "none";
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            nextStateName = ResolveStateName(nextState.shortNameHash);
            AnimatorClipInfo[] nextClips = animator.GetNextAnimatorClipInfo(0);
            if (nextClips != null && nextClips.Length > 0 && nextClips[0].clip != null)
                nextClipName = nextClips[0].clip.name;
        }

        Vector3 modelLocal = ModelTransform.localPosition;
        Vector3 hipsLocal = hipsTransform != null ? hipsTransform.localPosition : Vector3.zero;
        string hipsName = hipsTransform != null ? hipsTransform.name : "none";

        return
            "Anim: " + stateName + " t=" + state.normalizedTime.ToString("F2") + " clip=" + clipName + "\n" +
            "Next: " + nextStateName + " clip=" + nextClipName + "\n" +
            "AnimSpeed: " + animator.speed.ToString("F2") + " stateSpeed=" + state.speed.ToString("F2") + " enabled=" + animator.enabled + " active=" + animator.gameObject.activeInHierarchy + " transition=" + animator.IsInTransition(0) + "\n" +
            "ModelLocal: " + FormatVector(modelLocal) + "\n" +
            "Hips(" + hipsName + "): " + FormatVector(hipsLocal);
    }

    // Moves the visual model up or down without moving the physics body.
    public void SetModelVerticalOffset(float offset)
    {

        modelVerticalOffset = Mathf.Clamp(offset, -maxDownwardModelOffset, maxDownwardModelOffset);
        Vector3 position = modelBaseLocalPosition;
        position.y += modelVerticalOffset;
        ModelTransform.localPosition = position;
    }

    // Returns the visual model to its normal local height.
    public void ClearModelVerticalOffset()
    {
        SetModelVerticalOffset(0f);
    }

    // Turns the visual model toward the current movement direction.
    public void RotateTowardsVelocity(Vector3 currentVelocity, float turnSpeed)
    {
        Transform model = ModelTransform;

        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
        if (horizontalVelocity.sqrMagnitude < RotateVelocityThresholdSqr)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(horizontalVelocity.normalized);
        model.rotation = Quaternion.RotateTowards(model.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Moves directly into the needed locomotion state when the desired state changes.
    void UpdateLocomotionCrossFade()
    {
        if (landingAnimationActive || !useDirectCrossFade)
            return;

        if (desiredClimbing && !currentlyInClimb && hasClimbState)
        {
            currentlyInClimb = true;
            currentlyInWalk = false;
            currentlyInRun = false;
            SetLocomotionBools(false, false, true);
            animator.CrossFadeInFixedTime(climbStateHash, locomotionCrossFadeDuration);
            return;
        }

        if (desiredRunning && !currentlyInRun && hasRunState)
        {
            currentlyInRun = true;
            currentlyInWalk = false;
            currentlyInClimb = false;
            SetLocomotionBools(false, true, false);
            animator.CrossFadeInFixedTime(runStateHash, locomotionCrossFadeDuration);
            return;
        }

        if (desiredWalking && !currentlyInWalk && hasWalkState)
        {
            currentlyInWalk = true;
            currentlyInRun = false;
            currentlyInClimb = false;
            SetLocomotionBools(true, false, false);
            animator.CrossFadeInFixedTime(walkStateHash, locomotionCrossFadeDuration);
            return;
        }

        if (!desiredWalking && !desiredRunning && !desiredClimbing && (currentlyInWalk || currentlyInRun || currentlyInClimb) && hasIdleState)
        {
            ClearLocomotionState();
            animator.CrossFadeInFixedTime(idleStateHash, idleCrossFadeDuration);
        }
    }

    // Fades to the locomotion state that matches the stored desired flags.
    void CrossFadeToCurrentLocomotion(float duration)
    {
        if (!useDirectCrossFade)
            return;

        if (desiredClimbing && hasClimbState)
        {
            currentlyInClimb = true;
            currentlyInWalk = false;
            currentlyInRun = false;
            SetLocomotionBools(false, false, true);
            animator.CrossFadeInFixedTime(climbStateHash, duration);
            return;
        }

        if (desiredRunning && hasRunState)
        {
            currentlyInRun = true;
            currentlyInWalk = false;
            currentlyInClimb = false;
            SetLocomotionBools(false, true, false);
            animator.CrossFadeInFixedTime(runStateHash, duration);
            return;
        }

        if (desiredWalking && hasWalkState)
        {
            currentlyInWalk = true;
            currentlyInRun = false;
            currentlyInClimb = false;
            SetLocomotionBools(true, false, false);
            animator.CrossFadeInFixedTime(walkStateHash, duration);
            return;
        }

        ClearLocomotionState();
        if (hasIdleState)
            animator.CrossFadeInFixedTime(idleStateHash, duration);
    }

    // Clears the stored walk, run, and climb animation flags.
    void ClearLocomotionState()
    {
        currentlyInWalk = false;
        currentlyInRun = false;
        currentlyInClimb = false;
        locomotionRamp01 = 0f;
        desiredClimbMoving = false;
        SetLocomotionBools(false, false, false);
    }

    // Writes the three locomotion bools only when the animator has those parameters.
    void SetLocomotionBools(bool walking, bool running, bool climbing)
    {
        SetBoolIfPresent(walkingParameterHash, hasWalkingParameter, walking);
        SetBoolIfPresent(runningParameterHash, hasRunningParameter, running);
        SetBoolIfPresent(climbingParameterHash, hasClimbingParameter, climbing);
    }

    // Checks whether the current animator state matches a cached state hash.
    bool IsCurrentState(int stateHash)
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == stateHash;
    }

    // Converts a cached animator state hash into a readable state name.
    string ResolveStateName(int stateHash) => stateHash switch
    {
        _ when stateHash == idleStateHash        => idleStateName,
        _ when stateHash == walkStateHash        => walkStateName,
        _ when stateHash == runStateHash         => runStateName,
        _ when stateHash == climbStateHash       => climbStateName,
        _ when stateHash == jumpStateHash        => jumpStateName,
        _ when stateHash == jumpInPlaceStateHash => jumpInPlaceStateName,
        _ when stateHash == fallStateHash        => fallStateName,
        _ when stateHash == landStateHash        => landStateName,
        _                                        => stateHash.ToString() // Дефолтный вариант (вместо else)
    };


    // Formats a vector without adding noisy labels.
    static string FormatVector(Vector3 value)
    {
        return value.x.ToString("F2") + "," + value.y.ToString("F2") + "," + value.z.ToString("F2");
    }

    // Writes a bool animator parameter only when it exists.
    void SetBoolIfPresent(int parameterHash, bool hasParameter, bool value)
    {
        if (hasParameter)
            animator.SetBool(parameterHash, value);
    }

    // Writes a float animator parameter only when it exists.
    void SetFloatIfPresent(int parameterHash, bool hasParameter, float value)
    {
        if (hasParameter)
            animator.SetFloat(parameterHash, value);
    }
}


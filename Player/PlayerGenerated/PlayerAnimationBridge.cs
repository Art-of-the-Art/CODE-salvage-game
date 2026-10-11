/* 
    Про контекст разработчика
    Разработчик НЕ хочет изучать как работают анимации в Unity. 
    Разработчика устроит полностью "вайбкодить" этот скрипт 
*/

/* ЧТО ДЕЛАЕТ СКРИПТ
 * Связывает состояние движения игрока с анимацией его общей 3D-модели.
 * Unity вызывает Awake() при инициализации: скрипт находит Animator внутри visualRoot,
 * назначает общий контроллер и запоминает доступные состояния и параметры анимации.
 * Во время игры PlayerMovement передаёт сюда сведения о ходьбе, беге, прыжке и лазании.
 * Скрипт выбирает анимацию и переходы, а в Update() каждый кадр регулирует скорость
 * воспроизведения и отложенные переходы. По командам PlayerMovement также поворачивает
 * модель к движению или стене и меняет её визуальную высоту, не перемещая Rigidbody.
 */

using UnityEngine;

public class PlayerAnimationBridge : MonoBehaviour
{
    const float RotateVelocityThresholdSqr = 0.1f;

    [Header("Animator")]
    [SerializeField] Transform visualRoot;
    [SerializeField] Animator animator;
    [SerializeField] RuntimeAnimatorController animationController;
    [SerializeField] string walkingParameter = "isWalking";
    [SerializeField] string runningParameter = "isRunning";
    [SerializeField] string climbingParameter = "isClimbing";
    [SerializeField] string playbackSpeedParameter = "runPlaybackSpeed";
    [SerializeField] string groundedParameter = "isGrounded";
    [SerializeField] string verticalVelocityParameter = "verticalVelocity";
    [SerializeField] string fallingParameter = "isFalling";
    [SerializeField] string jumpTriggerParameter = "jumpRequested";
    [SerializeField] string jumpInPlaceTriggerParameter = "jumpInPlaceRequested";
    [SerializeField] string locomotionStateName = "Locomotion";
    [SerializeField] string locomotionSpeedParameter = "locomotionSpeed";
    [SerializeField] string idleStateName = "Idle";
    [SerializeField] string walkStateName = "Walk";
    [SerializeField] string runStateName = "Run";
    [SerializeField] string climbStateName = "Climb";
    [SerializeField] string jumpStateName = "Jump";
    [SerializeField] string jumpInPlaceStateName = "JumpInPlace";
    [SerializeField] string fallStateName = "Fall";
    [SerializeField] string landStateName = "Land";
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
    [SerializeField] bool useDirectCrossFade = true;

    bool desiredWalking;
    bool desiredRunning;
    bool desiredClimbing;
    bool desiredClimbMoving;
    bool isGroundedAnimation = true;
    bool isFallingAnimation;
    bool landingAnimationActive;
    bool hasLocomotionState;
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
    bool hasLocomotionSpeedParameter;
    bool hasPlaybackSpeedParameter;
    bool hasGroundedParameter;
    bool hasVerticalVelocityParameter;
    bool hasFallingParameter;
    bool hasJumpTriggerParameter;
    bool hasJumpInPlaceTriggerParameter;
    float locomotionRamp01;
    float jumpStartedAt;
    float landingStartedAt;
    int locomotionStateHash;
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
    int locomotionSpeedParameterHash;
    Transform hipsTransform;
    Vector3 modelBaseLocalPosition;
    float modelVerticalOffset;
    float desiredLocomotionSpeed;

    public Transform ModelTransform => visualRoot;
    public bool ShouldUseGroundFootIk => isGroundedAnimation && !desiredClimbing;
    public float LocomotionSpeed => desiredLocomotionSpeed;

    void Awake()
    {


        // Resolve the Animator from the shared visual prefab, including after replacing its model.
        if (visualRoot != null)
            animator = visualRoot.GetComponentInChildren<Animator>(true);
        if (visualRoot == null || animator == null)
        {
            Debug.LogError("PlayerAnimationBridge needs a visual root containing the model's Animator.", this);
            enabled = false;
            return;
        }
        if (animationController != null)
            animator.runtimeAnimatorController = animationController;

        walkingParameterHash = Animator.StringToHash(walkingParameter);
        runningParameterHash = Animator.StringToHash(runningParameter);
        climbingParameterHash = Animator.StringToHash(climbingParameter);
        playbackSpeedParameterHash = Animator.StringToHash(playbackSpeedParameter);
        groundedParameterHash = Animator.StringToHash(groundedParameter);
        verticalVelocityParameterHash = Animator.StringToHash(verticalVelocityParameter);
        fallingParameterHash = Animator.StringToHash(fallingParameter);
        jumpTriggerParameterHash = Animator.StringToHash(jumpTriggerParameter);
        jumpInPlaceTriggerParameterHash = Animator.StringToHash(jumpInPlaceTriggerParameter);
        locomotionStateHash = Animator.StringToHash(locomotionStateName);
        idleStateHash = Animator.StringToHash(idleStateName);
        walkStateHash = Animator.StringToHash(walkStateName);
        runStateHash = Animator.StringToHash(runStateName);
        climbStateHash = Animator.StringToHash(climbStateName);
        jumpStateHash = Animator.StringToHash(jumpStateName);
        jumpInPlaceStateHash = Animator.StringToHash(jumpInPlaceStateName);
        fallStateHash = Animator.StringToHash(fallStateName);
        landStateHash = Animator.StringToHash(landStateName);
        locomotionSpeedParameterHash = Animator.StringToHash(locomotionSpeedParameter);


        animator.applyRootMotion = false;
        hipsTransform = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hipsTransform == null)
            hipsTransform = animator.transform.Find("mixamorig:Hips");
        modelBaseLocalPosition = ModelTransform.localPosition;

        hasLocomotionState = animator.HasState(0, locomotionStateHash);
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
                case int hash when hash == locomotionSpeedParameterHash: hasLocomotionSpeedParameter = true; break;
            }
        }

        SetBoolIfPresent(groundedParameterHash, hasGroundedParameter, true);
        SetBoolIfPresent(walkingParameterHash, hasWalkingParameter, false);
        SetBoolIfPresent(runningParameterHash, hasRunningParameter, false);
        SetBoolIfPresent(climbingParameterHash, hasClimbingParameter, false);
        SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 1f);
        SetFloatIfPresent(verticalVelocityParameterHash, hasVerticalVelocityParameter, 0f);
        SetFloatIfPresent(locomotionSpeedParameterHash, hasLocomotionSpeedParameter, 0f);
        SetBoolIfPresent(fallingParameterHash, hasFallingParameter, false);
    }

    void Update()
    {
        UpdateLocomotionSpeed();
        UpdateClimbPlaybackSpeed();

        if (landingAnimationActive && Time.time - landingStartedAt >= landingExitDelay)
        {
            landingAnimationActive = false;
            CrossFadeToCurrentLocomotion(landingCrossFadeDuration);
        }

        if (useDirectCrossFade && hasFallState && !isGroundedAnimation && !desiredClimbing && isFallingAnimation && Time.time - jumpStartedAt >= minimumJumpStateTime)
        {
            if (!IsCurrentOrNextState(fallStateHash))
                animator.CrossFadeInFixedTime(fallStateHash, fallCrossFadeDuration);
        }
    }

    // Applies the movement state that should be visible on the player model.
    public void SetMovementState(bool grounded, float yVelocity, float locomotionSpeed, bool walking, bool running, bool climbing, bool climbingMoving = true)
    {

        bool wasGrounded = isGroundedAnimation;
        desiredClimbing = climbing;
        desiredClimbMoving = climbing && climbingMoving;
        desiredWalking = grounded && walking && !climbing;
        desiredRunning = grounded && running && !climbing;
        isGroundedAnimation = grounded && !climbing;
        isFallingAnimation = !isGroundedAnimation && !climbing && yVelocity < fallingVelocityThreshold;
        desiredLocomotionSpeed = isGroundedAnimation ? locomotionSpeed : 0f;

        SetBoolIfPresent(groundedParameterHash, hasGroundedParameter, isGroundedAnimation);
        SetFloatIfPresent(verticalVelocityParameterHash, hasVerticalVelocityParameter, yVelocity);
        SetBoolIfPresent(fallingParameterHash, hasFallingParameter, isFallingAnimation);
        SetLocomotionBools(desiredWalking, desiredRunning, desiredClimbing);

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

    // Walking rotates the visual child independently of the physics body.
    // A wall attachment must replace that old world heading, not add the parent's turn to it.
    public void FaceClimbSurface(Vector3 surfaceNormal)
    {
        if (surfaceNormal.sqrMagnitude < 0.001f)
            return;
        Vector3 forward = -surfaceNormal.normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(transform.up, forward);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.forward, forward);
        ModelTransform.rotation = Quaternion.LookRotation(forward, up.normalized);
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

    // The movement code supplies a target speed at the physics rate.
    // Damping here lets the visible blend change smoothly at the render rate.
    void UpdateLocomotionSpeed()
    {
        if (hasLocomotionSpeedParameter)
            animator.SetFloat(locomotionSpeedParameterHash, desiredLocomotionSpeed,
                locomotionSpeedDampTime, Time.deltaTime);
    }

    // Ground locomotion uses Blend Tree child speeds. This legacy parameter now
    // controls only Climb, including the existing freeze when input is released.
    void UpdateClimbPlaybackSpeed()
    {
        if (!desiredClimbing)
        {
            locomotionRamp01 = 0f;
            SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 1f);
            return;
        }

        if (!desiredClimbMoving)
        {
            SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter, 0f);
            return;
        }

        float rampStep = climbPlaybackRampDuration > 0f
            ? Time.deltaTime / climbPlaybackRampDuration : 1f;
        locomotionRamp01 = Mathf.MoveTowards(locomotionRamp01, 1f, rampStep);
        float eased = locomotionRamp01 * locomotionRamp01 * (3f - 2f * locomotionRamp01);
        SetFloatIfPresent(playbackSpeedParameterHash, hasPlaybackSpeedParameter,
            Mathf.Max(0.05f, eased * maxClimbPlaybackSpeed));
    }

    // Ground speed changes stay inside Locomotion; only a mode change needs CrossFade.
    void UpdateLocomotionCrossFade()
    {
        if (landingAnimationActive || !useDirectCrossFade)
            return;
        CrossFadeToCurrentLocomotion(locomotionCrossFadeDuration);
    }

    // One selection path is shared by normal movement and landing completion.
    void CrossFadeToCurrentLocomotion(float duration)
    {
        if (!useDirectCrossFade)
            return;
        int stateHash;
        if (desiredClimbing && hasClimbState)
            stateHash = climbStateHash;
        else if (isGroundedAnimation && hasLocomotionState)
            stateHash = locomotionStateHash;
        else if (desiredRunning && hasRunState)
            stateHash = runStateHash;
        else if (desiredWalking && hasWalkState)
            stateHash = walkStateHash;
        else if (hasIdleState)
            stateHash = idleStateHash;
        else
            return;

        SetLocomotionBools(desiredWalking, desiredRunning, desiredClimbing);
        if (IsCurrentOrNextState(stateHash))
            return;
        animator.CrossFadeInFixedTime(stateHash, duration);
    }

    // Clears the stored walk, run, and climb animation flags.
    void ClearLocomotionState()
    {
        locomotionRamp01 = 0f;
        desiredLocomotionSpeed = 0f;
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

    // Do not restart a crossfade while its destination is already being blended in.
    bool IsCurrentOrNextState(int stateHash)
    {
        if (animator.IsInTransition(0))
            return animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
        return IsCurrentState(stateHash);
    }

    // Converts a cached animator state hash into a readable state name.
    string ResolveStateName(int stateHash) => stateHash switch
    {
        _ when stateHash == idleStateHash        => idleStateName,
        _ when stateHash == locomotionStateHash  => locomotionStateName,
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


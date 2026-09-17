using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerInput))]
public class PlayerInputReader : MonoBehaviour
{
    const float MoveInputDeadZoneSqr = 0.01f;

    [SerializeField] string moveActionName = "Move";
    [SerializeField] string jumpActionName = "Jump";
    [SerializeField] string runActionName = "Sprint";

    PlayerInput playerInput;
    InputAction moveAction;
    InputAction jumpAction;
    InputAction runAction;
    Vector2 moveInput;
    bool jumpQueued;
    bool runHeld;

    public Vector2 MoveInput => moveInput;
    public bool HasMoveInput => moveInput.sqrMagnitude > MoveInputDeadZoneSqr;
    public bool RunHeld => runHeld;

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
    }

    void OnEnable()
    {
        BindActions();
    }

    void Start()
    {
        // PlayerInput may create its per-player action copy in its own OnEnable.
        BindActions();
    }

    void OnDisable()
    {
        ClearInput();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ClearInput();
    }

    void Update()
    {
        if (playerInput == null || !playerInput.isActiveAndEnabled || !playerInput.inputIsActive)
        {
            ClearInput();
            return;
        }

        if (moveAction != null)
            moveInput = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
            jumpQueued = true;
        if (runAction != null)
            runHeld = runAction.IsPressed();
    }

    // Preserve message-based adapters for configurations without named actions.
    // Bound actions are polled only once: SendMessages plus polling must not queue two jumps.
    public void OnMove(InputValue value)
    {
        if (isActiveAndEnabled && moveAction == null)
            moveInput = Vector2.ClampMagnitude(value.Get<Vector2>(), 1f);
    }

    public void OnJump(InputValue value)
    {
        if (isActiveAndEnabled && jumpAction == null && value.isPressed)
            jumpQueued = true;
    }

    public void OnSprint(InputValue value)
    {
        if (isActiveAndEnabled && runAction == null)
            runHeld = value.isPressed;
    }

    public bool ConsumeJump()
    {
        bool wasQueued = jumpQueued;
        jumpQueued = false;
        return wasQueued;
    }

    void ClearInput()
    {
        moveInput = Vector2.zero;
        runHeld = false;
        jumpQueued = false;
    }

    void BindActions()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();
        var actions = playerInput != null ? playerInput.actions : null;
        moveAction = actions != null ? actions.FindAction(moveActionName, false) : null;
        jumpAction = actions != null ? actions.FindAction(jumpActionName, false) : null;
        runAction = actions != null ? actions.FindAction(runActionName, false) : null;
    }
}

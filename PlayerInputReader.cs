using UnityEngine;
using UnityEngine.InputSystem;

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
        BindActions();
    }

    void OnEnable()
    {
        BindActions();
    }

    void Update()
    {
        if (moveAction != null)
            moveInput = moveAction.ReadValue<Vector2>();
        else
            moveInput = ReadKeyboardMoveFallback();

        if (jumpAction != null && jumpAction.WasPressedThisFrame())
            jumpQueued = true;
        else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpQueued = true;

        if (runAction != null)
            runHeld = runAction.IsPressed();
        else
            runHeld = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    // Receives move input from Unity events when an action asset is not bound.
    public void OnMove(InputValue value)
    {
        if (moveAction == null)
            moveInput = value.Get<Vector2>();
    }

    // Receives jump input from Unity events and stores it for the movement step.
    public void OnJump(InputValue value)
    {
        if (value.isPressed)
            jumpQueued = true;
    }

    // Gives the queued jump to movement code once, then clears it.
    public bool ConsumeJump()
    {
        bool wasQueued = jumpQueued;
        jumpQueued = false;
        return wasQueued;
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

    // Finds the named input actions used by the player.
    void BindActions()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (playerInput == null || playerInput.actions == null)
            return;

        moveAction = playerInput.actions.FindAction(moveActionName, false);
        jumpAction = playerInput.actions.FindAction(jumpActionName, false);
        runAction = playerInput.actions.FindAction(runActionName, false);
    }

    // Reads WASD or arrow keys when the Input System actions are unavailable.
    static Vector2 ReadKeyboardMoveFallback()
    {
        if (Keyboard.current == null)
            return Vector2.zero;

        Vector2 input = Vector2.zero;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            input.x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            input.x += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            input.y -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
            input.y += 1f;

        return input.sqrMagnitude > 1f ? input.normalized : input;
    }
}

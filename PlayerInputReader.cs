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
        moveInput = moveAction.ReadValue<Vector2>();

        if (jumpAction.WasPressedThisFrame())
            jumpQueued = true;

        runHeld = runAction.IsPressed();
    }

    // Receives move input from Unity events when an action asset is not bound.
    public void OnMove(InputValue value)
    {
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

        moveAction = playerInput.actions.FindAction(moveActionName, false);
        jumpAction = playerInput.actions.FindAction(jumpActionName, false);
        runAction = playerInput.actions.FindAction(runActionName, false);
    }


}

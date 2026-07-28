using UnityEngine;

/// <summary>
/// Physical movement layer shared by every character. Both PlayerBrain and
/// AIBrain drive this same interface, which keeps Player-vs-AI and AI-vs-AI
/// perfectly symmetric.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 9f;
    public float jumpHeight = 1.2f;
    public float gravity = -22f;

    [Header("Look")]
    public Transform head;           // pitched up/down; body handles yaw
    public float minPitch = -85f;
    public float maxPitch = 85f;

    [Tooltip("External speed scale (X-Ray scope, future shrink gadget, etc.).")]
    public float speedMultiplier = 1f;

    [Tooltip("Second speed scale owned by StatusEffects (slow/freeze/goo). Multiplies with speedMultiplier.")]
    [HideInInspector] public float statusSpeedMultiplier = 1f;

    [Tooltip("Impulse velocity from knockback weapons; decays automatically.")]
    [HideInInspector] public Vector3 externalVelocity;

    [Tooltip("While true, gravity is replaced by a gentle upward drift (Moonboots / bubble trap).")]
    [HideInInspector] public bool floatMode;

    CharacterController _controller;
    TransformMode _vehicle;
    Vector2 _moveInput;
    bool _sprinting;
    float _verticalVelocity;
    float _pitch;

    public bool IsGrounded => _controller.isGrounded;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _vehicle = GetComponent<TransformMode>();
    }

    public void SetMoveInput(Vector2 input) => _moveInput = Vector2.ClampMagnitude(input, 1f);

    public void SetSprint(bool sprinting) => _sprinting = sprinting;

    public void AddLook(Vector2 delta)
    {
        transform.Rotate(0f, delta.x, 0f);
        _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        if (head != null)
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    /// <summary>
    /// Leap, if this character has legs to leap with.
    ///
    /// A tank does not jump. The rule lives here rather than at the input site
    /// because jumps arrive from several places — the Space key, the on-screen
    /// touch button — and a check at each one is a check somebody later forgets.
    /// Mid-transformation counts as "not a robot" too: half a tank should not
    /// spring off the floor.
    /// </summary>
    public void Jump()
    {
        if (_vehicle != null && (_vehicle.IsVehicle || _vehicle.IsBusy))
            return;
        if (_controller.isGrounded)
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    public void Teleport(Vector3 position)
    {
        _controller.enabled = false;
        transform.position = position;
        _verticalVelocity = 0f;
        _controller.enabled = true;
    }

    void Update()
    {
        float speed = (_sprinting ? sprintSpeed : walkSpeed) * speedMultiplier * statusSpeedMultiplier;
        Vector3 planar = transform.TransformDirection(new Vector3(_moveInput.x, 0f, _moveInput.y)) * speed;

        if (floatMode)
        {
            // Anti-grav: drift gently upward instead of falling.
            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 1.2f, 10f * Time.deltaTime);
        }
        else
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 motion = planar + Vector3.up * _verticalVelocity + externalVelocity;
        _controller.Move(motion * Time.deltaTime);
        externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, 18f * Time.deltaTime);
    }
}

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

    [Tooltip("Apex floor in metres, for a pawn wearing no robot model yet.")]
    public float jumpHeight = 1.2f;

    [Tooltip("Apex as a multiple of the robot's own height; overrides jumpHeight " +
             "whenever the robot is taller than it. See RobotFactory.JumpHeights.")]
    public float jumpBodyHeights = RobotFactory.JumpHeights;

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

    /// <summary>
    /// Turn the body and pitch the view.
    ///
    /// Ice holds the pose. Movement was already stopped by the status system
    /// zeroing the speed multiplier, but turning goes straight to the transform
    /// and so survived it — a frozen character could spin on the spot and track
    /// targets while encased. Blocked here, the one place every look request
    /// passes through, mouse and touch alike.
    /// </summary>
    public void AddLook(Vector2 delta)
    {
        if (IsFrozen)
            return;
        transform.Rotate(0f, delta.x, 0f);
        _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        if (head != null)
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    StatusEffects _status;

    /// <summary>
    /// Looked up lazily: StatusEffects is added to a character the first time
    /// something afflicts it, so most characters never have one.
    /// </summary>
    bool IsFrozen
    {
        get
        {
            if (_status == null)
                _status = GetComponent<StatusEffects>();
            return _status != null && _status.IsFrozen;
        }
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
        // The status system zeroes the planar speed multiplier, but jump writes
        // vertical velocity directly and slipped past it — a frozen character
        // could still hop on the spot inside the ice.
        if (IsFrozen)
            return;
        if (_controller.isGrounded)
            _verticalVelocity = Mathf.Sqrt(JumpApex * -2f * gravity);
    }

    /// <summary>
    /// How high this jump goes: the robot's own height plus 20%, so a leap
    /// visibly clears the robot doing it whichever one the player picked.
    ///
    /// Measured at the moment of the jump rather than cached, because the
    /// roster screen swaps the model underneath a live pawn (RobotFactory
    /// .Reskin) and a height cached at Awake would be the previous robot's.
    /// Jumps are rare enough that walking the model's renderers once each is
    /// nothing.
    /// </summary>
    public float JumpApex => Mathf.Max(jumpHeight, RobotFactory.MeasureHeight(transform) * jumpBodyHeights);

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

        UpdateAirborne();
    }

    /// <summary>
    /// Tells the legs when they are off the floor, which is what puts the
    /// robot into the jump animation instead of running on nothing. The bots'
    /// half of the same signal is RobotJump.SetAirborne.
    ///
    /// Grounding is not read raw: CharacterController.isGrounded drops out for
    /// single frames on stairs and slope seams, and a raw read would flicker
    /// the whole robot through takeoff-and-land every time it walked up a
    /// ramp. Only <see cref="GroundGrace"/> of continuous air counts.
    ///
    /// Float mode is deliberately NOT airborne: moonboots are a hover, and a
    /// robot holding a jumping-knee tuck for the ten seconds of a drift reads
    /// as a frozen animation, not as flying.
    /// </summary>
    void UpdateAirborne()
    {
        bool grounded = _controller.isGrounded || floatMode;
        _airTime = grounded ? 0f : _airTime + Time.deltaTime;

        bool inRobotForm = _vehicle == null || (!_vehicle.IsVehicle && !_vehicle.IsBusy);
        SetAirborne(inRobotForm && _airTime > GroundGrace);
    }

    /// <summary>
    /// Ungrounded frames shorter than this are step seams, not flight.
    /// </summary>
    const float GroundGrace = 0.08f;

    float _airTime;
    bool _airborne;

    /// <summary>
    /// Looked up on the edge rather than cached: a robot swap destroys the
    /// whole model, taking its RobotLocomotion with it, and a cached reference
    /// would leave the new skeleton never hearing about a jump again.
    /// </summary>
    void SetAirborne(bool airborne)
    {
        if (airborne == _airborne)
            return;
        _airborne = airborne;
        var legs = GetComponentInChildren<RobotLocomotion>();
        if (legs != null)
            legs.Airborne = airborne;
    }
}

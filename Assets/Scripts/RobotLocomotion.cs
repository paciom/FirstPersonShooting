using UnityEngine;

/// <summary>
/// Drives a rigged walker robot's Animator from how fast its character is
/// actually moving. Lives on the generated strider-robot model prefab (see
/// WalkerRigForge), so it works no matter who owns the model: player, bot, or
/// the robot-select turntable preview.
///
/// Speed is measured from the character root's own position delta rather than
/// asking a CharacterController or NavMeshAgent, because bots move through both
/// and the delta is the one signal that is always true — including when a status
/// effect slows or freezes them.
///
/// The presence of this component also marks the model as "stands on the floor",
/// which RobotFactory uses to sit it on the ground instead of centring it like a
/// hovering robot.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class RobotLocomotion : MonoBehaviour
{
    /// <summary>Animator float parameter the locomotion blend tree reads (m/s).</summary>
    public const string SpeedParameter = "Speed";

    [Tooltip("How quickly the animator's Speed follows the real speed (higher = snappier).")]
    public float responsiveness = 12f;

    [Tooltip("Legs and hover bob fight each other — switch the bob off on our character.")]
    public bool disableHoverBob = true;

    /// <summary>
    /// Set while the character is mid-leap (see RobotJump). The legs settle
    /// instead of sprinting through the air — a robot running on nothing reads
    /// worse than one holding a pose.
    /// </summary>
    public bool Airborne { get; set; }

    Animator _animator;
    Transform _character;
    Vector3 _lastPosition;
    float _speed;
    int _speedHash;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _speedHash = Animator.StringToHash(SpeedParameter);

        var motor = GetComponentInParent<CharacterMotor>();
        _character = motor != null ? motor.transform : transform.root;
        _lastPosition = _character.position;
    }

    void Start()
    {
        if (!disableHoverBob || _character == null)
            return;
        var bob = _character.GetComponent<HoverBob>();
        if (bob != null)
            bob.StopAndReset();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 delta = _character.position - _lastPosition;
        _lastPosition = _character.position;

        float planar = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        if (planar > 40f)
            planar = 0f;    // teleport / respawn, not a sprint
        if (Airborne)
            planar = 0f;    // crossing a gap, not running along the ground

        _speed = Mathf.Lerp(_speed, planar, 1f - Mathf.Exp(-responsiveness * dt));
        _animator.SetFloat(_speedHash, _speed);
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes a bot leap the gaps in its own path instead of gliding across them.
///
/// NavMeshAgents traverse an off-mesh link by sliding along it in a straight
/// line, so a robot stepping off a ZIGGURAT tier or a FOUNDRY catwalk floated
/// down like it was on a wire. This takes manual control of that traversal and
/// throws the robot along a parabola instead, peaking a full body height and a
/// bit over the far end — the arena links already mark exactly where a jump is
/// necessary. The martial-arts jump animation rides on top of it, via
/// RobotLocomotion.Airborne.
///
/// Only robot form jumps. A transformed robot is a ground vehicle, so it is
/// routed around every link by the pathfinder (see <see cref="UpdateAreaMask"/>)
/// rather than being allowed to leap and then stopped — a tank never commits to
/// a gap it cannot cross. The player side of the same rule lives in
/// CharacterMotor.Jump.
///
/// Deliberately an Update-driven state machine rather than a coroutine: bots get
/// deactivated on de-rez and on every mode switch, and a deactivated GameObject
/// loses its coroutines permanently — which would strand the robot mid-air, on a
/// link it never completes, forever.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class RobotJump : MonoBehaviour
{
    [Tooltip("Shortest a leap may take, seconds. Below this it reads as a twitch.")]
    public float minAirTime = 0.45f;

    [Tooltip("Longest a leap may take. Above this the robot hangs.")]
    public float maxAirTime = 0.95f;

    [Tooltip("Apex above the higher end of the link, as a multiple of the robot's " +
             "own height. See RobotFactory.JumpHeights.")]
    public float clearanceBodyHeights = RobotFactory.JumpHeights;

    [Tooltip("Apex floor in metres, for a bot wearing no robot model yet.")]
    public float clearance = 0.9f;

    /// <summary>
    /// If something else moves us this far in one frame mid-leap — a respawn
    /// teleport, a rising cover block — the leap is abandoned rather than
    /// fought.
    /// </summary>
    const float HijackDistance = 1.5f;

    /// <summary>
    /// The gravity a leap is TIMED to. Nothing here is simulated — the arc is
    /// an interpolation — but a body-height leap crossed in a third of a second
    /// reads as a slingshot, so how long the robot hangs is taken from how high
    /// it goes, at the same pull the player's own jump falls under
    /// (CharacterMotor.gravity). The two jumps then look like the same jump.
    /// </summary>
    const float TimedGravity = 22f;

    /// <summary>Seconds to fall <paramref name="height"/> metres under <see cref="TimedGravity"/>.</summary>
    static float FallTime(float height)
    {
        return Mathf.Sqrt(2f * Mathf.Max(0f, height) / TimedGravity);
    }

    /// <summary>
    /// Unity's built-in "Jump" NavMesh area. ArenaKit puts every link on it so
    /// that a form which cannot jump can simply be routed around them.
    /// </summary>
    public const int JumpArea = 2;
    const int JumpAreaMask = 1 << JumpArea;

    NavMeshAgent _agent;
    TransformMode _vehicle;

    bool _jumping;
    Vector3 _from, _to, _lastSet;
    float _t, _duration, _peak;

    int _baseAreaMask;
    bool _maskedForVehicle;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _vehicle = GetComponent<TransformMode>();
        // Take the link away from the agent; without this it keeps sliding.
        _agent.autoTraverseOffMeshLink = false;
        _baseAreaMask = _agent.areaMask;
    }

    /// <summary>
    /// Tanks do not jump, so they are not offered routes that require one — the
    /// pathfinder simply sends them round by the ramps instead. Doing it with
    /// the area mask rather than by refusing a link mid-crossing means a vehicle
    /// never commits to a gap it cannot cross.
    ///
    /// A vehicle that somehow ends up on a link anyway (transformed while
    /// standing on one) still completes it — see Begin() — because a form that
    /// refuses to finish a crossing is a form that gets stuck on it forever.
    /// </summary>
    void UpdateAreaMask()
    {
        bool asVehicle = _vehicle != null && _vehicle.IsVehicle;
        if (asVehicle == _maskedForVehicle)
            return;
        _maskedForVehicle = asVehicle;
        _agent.areaMask = asVehicle ? (_baseAreaMask & ~JumpAreaMask) : _baseAreaMask;
    }

    void OnDisable()
    {
        // Never leave the agent parked on a half-finished link — it would refuse
        // to path anywhere until something completed it.
        if (_jumping)
            Finish();
    }

    void Update()
    {
        if (_agent == null || !_agent.enabled)
            return;

        UpdateAreaMask();

        if (_jumping)
        {
            Tick();
            return;
        }

        if (_agent.isOnOffMeshLink)
            Begin();
    }

    void Begin()
    {
        OffMeshLinkData link = _agent.currentOffMeshLinkData;
        if (!link.valid)
        {
            _agent.CompleteOffMeshLink();
            return;
        }

        _from = transform.position;
        _to = link.endPos + Vector3.up * _agent.baseOffset;

        var flatFrom = new Vector3(_from.x, 0f, _from.z);
        var flatTo = new Vector3(_to.x, 0f, _to.z);
        float span = Vector3.Distance(flatFrom, flatTo);
        float rise = _to.y - _from.y;

        // A tank does not leap — not even a little. It only reaches here if it
        // transformed while already standing on a link, and the only thing left
        // to do then is get off it: no arc at all, just a quick roll to the far
        // end. Refusing outright would park it on the link permanently.
        bool asVehicle = _vehicle != null && _vehicle.IsVehicle;

        // Its own height plus 20%, measured off the robot actually wearing the
        // rig — the roster is swapped underneath a live bot, so this cannot be
        // a number decided when the component was added.
        float apex = Mathf.Max(clearance,
            RobotFactory.MeasureHeight(transform) * clearanceBodyHeights);

        _peak = asVehicle ? 0f : Mathf.Max(apex, rise + apex);
        _duration = asVehicle
            ? Mathf.Clamp(span / 9f, 0.15f, 0.45f)
            : Mathf.Clamp(Mathf.Max(span / 6f, FallTime(_peak) * 2f), minAirTime, maxAirTime);

        _t = 0f;
        _jumping = true;
        _lastSet = _from;
        SetAirborne(!asVehicle);
    }

    void Tick()
    {
        // The link vanished under us (respawn, arena swap) — stop pretending.
        if (!_agent.isOnOffMeshLink)
        {
            Finish();
            return;
        }

        // Someone else moved us. A de-rez teleport home, or a cover block rising
        // underneath. Their move wins.
        if (Vector3.Distance(transform.position, _lastSet) > HijackDistance)
        {
            Finish();
            return;
        }

        _t += Time.deltaTime / Mathf.Max(_duration, 0.01f);
        float k = Mathf.Clamp01(_t);

        Vector3 pos = Vector3.Lerp(_from, _to, k);
        // 4k(1-k) peaks at exactly 1.0 halfway across, so _peak is the real
        // apex height rather than something to tune by eye.
        pos.y += _peak * 4f * k * (1f - k);
        transform.position = pos;
        _lastSet = pos;

        if (k >= 1f)
        {
            transform.position = _to;
            _lastSet = _to;
            Finish();
        }
    }

    void Finish()
    {
        _jumping = false;
        SetAirborne(false);
        if (_agent != null && _agent.enabled && _agent.isOnOffMeshLink)
            _agent.CompleteOffMeshLink();
    }

    /// <summary>
    /// Hand the leap to the animator: the launch, the tuck and the landing
    /// play off this one flag. Looked up per leap rather than cached — a robot
    /// swap destroys the whole skeleton, taking this with it.
    /// </summary>
    void SetAirborne(bool airborne)
    {
        var locomotion = GetComponentInChildren<RobotLocomotion>();
        if (locomotion != null)
            locomotion.Airborne = airborne;
    }
}

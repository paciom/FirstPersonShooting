using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes a bot leap the gaps in its own path instead of gliding across them.
///
/// NavMeshAgents traverse an off-mesh link by sliding along it in a straight
/// line, so a robot stepping off a ZIGGURAT tier or a FOUNDRY catwalk floated
/// down like it was on a wire. This takes manual control of that traversal and
/// throws the robot along a parabola instead — which is all "jumping" needs to
/// be, because the arena links already mark exactly where a jump is necessary.
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
    public float minAirTime = 0.34f;

    [Tooltip("Longest a leap may take. Above this the robot hangs.")]
    public float maxAirTime = 0.85f;

    [Tooltip("How high the arc peaks above the higher end of the link.")]
    public float clearance = 0.9f;

    /// <summary>
    /// If something else moves us this far in one frame mid-leap — a respawn
    /// teleport, a rising cover block — the leap is abandoned rather than
    /// fought.
    /// </summary>
    const float HijackDistance = 1.5f;

    NavMeshAgent _agent;
    TransformMode _vehicle;

    bool _jumping;
    Vector3 _from, _to, _lastSet;
    float _t, _duration, _peak;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _vehicle = GetComponent<TransformMode>();
        // Take the link away from the agent; without this it keeps sliding.
        _agent.autoTraverseOffMeshLink = false;
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

        // A transformed robot is a ground vehicle — it does not leap. It still
        // has to cross, though, or a link becomes a place where a vehicle stops
        // forever: it drops off the edge quickly and flatly instead.
        bool asVehicle = _vehicle != null && _vehicle.IsVehicle;

        _peak = asVehicle ? 0.12f : Mathf.Max(clearance, rise + clearance);
        _duration = asVehicle
            ? Mathf.Clamp(span / 9f, 0.15f, 0.45f)
            : Mathf.Clamp(span / 6f, minAirTime, maxAirTime);

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
    /// Hold the legs still while airborne. Looked up per leap rather than
    /// cached: a robot swap destroys the whole skeleton, taking this with it.
    /// </summary>
    void SetAirborne(bool airborne)
    {
        var locomotion = GetComponentInChildren<RobotLocomotion>();
        if (locomotion != null)
            locomotion.Airborne = airborne;
    }
}

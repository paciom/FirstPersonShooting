using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The one authority over a raider's ground speed. Stasis fields and the
/// vehicle-form travel bonus both want to scale it, and CommanderUnit.Morph
/// also writes agent.speed on every fold — three writers, so the losing
/// write depends on frame order. This component ends the argument by
/// recomputing speed from its factors every frame: base × slow × form.
///
/// A sibling component rather than TDCreep code because the recompute needs
/// an Update, and a subclass declaring Update would HIDE CommanderUnit's —
/// Unity calls only the most-derived magic method, and the base Update is
/// the whole order-brain.
/// </summary>
public class TDPace : MonoBehaviour
{
    [SerializeField] float _baseSpeed = 3.5f;
    [SerializeField] float _slowFactor = 1f;
    [SerializeField] float _slowUntil = -1f;

    NavMeshAgent _agent;
    CommanderUnit _unit;
    GameObject _slowGlow;

    public void Init(float baseSpeed)
    {
        _baseSpeed = baseSpeed;
    }

    /// <summary>
    /// A stasis pulse's grip. Pulses don't stack — the strongest current
    /// factor simply holds until its clock runs out, so two coils on one
    /// lane are coverage, not a freeze ray.
    /// </summary>
    public void ApplySlow(float factor, float seconds)
    {
        float until = Time.time + seconds;
        if (Time.time < _slowUntil)
        {
            _slowFactor = Mathf.Min(_slowFactor, factor);
            _slowUntil = Mathf.Max(_slowUntil, until);
        }
        else
        {
            _slowFactor = factor;
            _slowUntil = until;
        }
    }

    void Update()
    {
        if (_agent == null)
            _agent = GetComponent<NavMeshAgent>();
        if (_unit == null)
            _unit = GetComponent<CommanderUnit>();
        if (_agent == null || !_agent.enabled)
            return;

        bool slowed = Time.time < _slowUntil;
        float speed = _baseSpeed
            * (slowed ? _slowFactor : 1f)
            * (_unit != null && _unit.InVehicleForm ? CommanderUnit.VehicleSpeedFactor : 1f);
        _agent.speed = speed;

        // The tell: a teal underglow while the field has hold, so a slowed
        // wave reads from the rim without a health-bar in sight.
        if (slowed && _slowGlow == null)
            _slowGlow = CommanderUnit.GlowQuad(transform, "StasisGrip", "VFX/ring",
                new Color(0.3f, 1f, 0.8f), 0.9f, 2f, 0.05f);
        if (_slowGlow != null && _slowGlow.activeSelf != slowed)
            _slowGlow.SetActive(slowed);
    }
}

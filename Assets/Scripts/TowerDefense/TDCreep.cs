using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One raider in the wave: a CommanderUnit that has taken a vow — march on
/// the Photon Core and touch it, answering nothing on the way. All the rig
/// (roster model, shield, agent, vehicle-fold travel, de-rez death) is
/// inherited; what this subclass adds is the vow.
///
/// Raiders carry no gun and never retaliate — a tower defense where the
/// wave shoots back is a war, and Commander already is one. Their whole
/// threat is arithmetic: shield points versus tower fire over the length
/// of the canyon.
///
/// Reaching the Core LEAKS: the raider drains core energy and folds into
/// light on the spot — the same de-rez exit a kill gets, so the ending
/// always looks the same and only the scoreboard knows the difference.
/// </summary>
public class TDCreep : CommanderUnit
{
    /// <summary>Close enough to the Core to drain it — the pocket's inner sanctum.</summary>
    const float LeakRadius = 8f;

    /// <summary>Credits this raider is worth to whoever stops it.</summary>
    [SerializeField] int _bounty = 10;

    /// <summary>Core energy lost if it arrives. 1 for the line, more for a boss.</summary>
    [SerializeField] int _leakDamage = 1;

    /// <summary>Set the instant the leak is booked, so a death can't book it twice.</summary>
    [SerializeField] bool _resolved;

    public int Bounty => _bounty;

    public static TDCreep Spawn(RobotRoster.Entry entry, Vector3 position, float hp,
        float speed, int bounty, int leakDamage, float visualScale)
    {
        var creep = Build<TDCreep>("TDRaider", entry.modelPrefab, entry.vehiclePrefab,
            teamId: 1, position, yaw: 180f, armed: false, secondaryWeapon: null,
            transformStages: entry.transformStages, paintAnchorHue: entry.paintAnchorHue);

        creep._bounty = bounty;
        creep._leakDamage = leakDamage;
        // A marcher, not a hunter — and blind on purpose: sight is what
        // makes an idle CommanderUnit pick fights.
        creep.sightRange = 0f;

        var shield = creep.GetComponent<EnergyShield>();
        shield.maxShield = hp;
        // No regen: damage banked against a raider must STAY banked, or
        // every tower gap on the lane silently refunds the towers before it.
        shield.regenPerSecond = 0f;
        shield.Rematerialize();   // resync Current after the change, as ever

        // TDPace owns the agent's speed from here on (slow fields and the
        // vehicle-form bonus compose there); this seed value is its base.
        creep.GetComponent<NavMeshAgent>().speed = speed;
        creep.gameObject.AddComponent<TDPace>().Init(speed);

        // Bosses read as bosses by silhouette alone. The capsule collider
        // scales with the root; the agent's radius doesn't, which only
        // means a boss brushes the canyon walls — suitably monstrous.
        if (!Mathf.Approximately(visualScale, 1f))
            creep.transform.localScale = Vector3.one * visualScale;

        creep.IssueMove(TDMap.CoreSite);
        return creep;
    }

    /// <summary>The vow, part one: no target is ever worth stopping for.</summary>
    protected override void OnUnderAttack(CommanderUnit attacker) { }

    /// <summary>The vow, part two: nobody re-tasks a raider.</summary>
    public override void IssueAttack(CommanderUnit target) { }

    /// <summary>
    /// An idle raider is either AT the Core — leak — or was jostled off its
    /// march (crowd shoves, arrival slack) and re-swears it. This tick is
    /// also what turns "arrived at the pocket" into the drain: Arrived()
    /// lands the unit Idle a stride short of the Core, inside LeakRadius.
    /// </summary>
    protected override void ThinkIdle()
    {
        Vector3 flat = TDMap.CoreSite - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < LeakRadius * LeakRadius)
        {
            Leak();
            return;
        }
        IssueMove(TDMap.CoreSite);
    }

    /// <summary>
    /// Touch the Core: book the drain, then take the standard fold-into-light
    /// exit. TDWaves is told FIRST — synchronously — so its bookkeeping can
    /// never mistake this death for a kill and pay a bounty on it.
    /// </summary>
    void Leak()
    {
        if (_resolved)
            return;
        _resolved = true;
        TDWaves.NotifyLeaked(this);
        TDMatch.NotifyLeak(_leakDamage);
        var shield = GetComponent<EnergyShield>();
        if (shield != null && !shield.IsDown)
            shield.TakeHit(999999f, transform.position + Vector3.up);
    }
}

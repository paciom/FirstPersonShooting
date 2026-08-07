using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One raider in the wave: a CommanderUnit under marching orders — take the
/// canyon, shoot whatever robot stands in the lane, and touch the Photon
/// Core. All the rig (roster model, shield, agent, laser, vehicle-fold
/// travel, de-rez death) is inherited; what this subclass adds is the
/// destination and the discipline.
///
/// The march is an ATTACK-MOVE, so the fight with the defender corps comes
/// free from the base brain: see a defender, close to range, trade fire,
/// resume the march over the wreckage. Two things never distract a raider:
/// TOWERS — rim fire is weather to them, not a target, which is what keeps
/// the towers' half of the game a tower defense — and distance: a raider
/// fights what blocks the lane, it does not go hunting.
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
        float speed, float damage, int bounty, int leakDamage, float visualScale)
    {
        var creep = Build<TDCreep>("TDRaider", entry.modelPrefab, entry.vehiclePrefab,
            teamId: 1, position, yaw: 180f, armed: true, secondaryWeapon: null,
            transformStages: entry.transformStages, paintAnchorHue: entry.paintAnchorHue);

        creep._bounty = bounty;
        creep._leakDamage = leakDamage;
        // Short eyes and a short gun: raiders answer what's in the lane
        // ahead, they don't wander off the march to hunt.
        creep.sightRange = 18f;
        creep.attackRange = 16f;

        var shield = creep.GetComponent<EnergyShield>();
        shield.maxShield = hp;
        // No regen: damage banked against a raider must STAY banked, or
        // every tower gap on the lane silently refunds the towers before it.
        shield.regenPerSecond = 0f;
        shield.Rematerialize();   // resync Current after the change, as ever

        foreach (var weapon in creep.GetComponentsInChildren<Weapon>())
            weapon.damage = damage;

        // TDPace owns the agent's speed from here on (slow fields and the
        // vehicle-form bonus compose there); this seed value is its base.
        creep.GetComponent<NavMeshAgent>().speed = speed;
        creep.gameObject.AddComponent<TDPace>().Init(speed);

        // The drive-by gun: pot-shots at rim structures while the march
        // rolls on. A sibling for the same reason TDPace is one.
        creep.gameObject.AddComponent<TDRaiderGun>();

        // Bosses read as bosses by silhouette alone. The capsule collider
        // scales with the root; the agent's radius doesn't, which only
        // means a boss brushes the canyon walls — suitably monstrous.
        if (!Mathf.Approximately(visualScale, 1f))
            creep.transform.localScale = Vector3.one * visualScale;

        creep.IssueAttackMove(TDMap.CoreSite);
        return creep;
    }

    /// <summary>
    /// Shot by a defender: turn and fight — the base brain already knows
    /// how, and the attack-move's resume point survives the detour. Shot by
    /// a TOWER (no unit attacker to resolve): shrug and keep marching — the
    /// base behavior would break toward "home", and this map's home for
    /// team 1 is a Commander coordinate that doesn't exist here.
    /// </summary>
    protected override void OnUnderAttack(CommanderUnit attacker)
    {
        if (attacker != null)
            base.OnUnderAttack(attacker);
    }

    /// <summary>
    /// An idle raider is either AT the Core — leak — or was jostled off its
    /// march (crowd shoves, arrival slack, a won fight with no resume) and
    /// re-swears it. This tick is also what turns "arrived at the pocket"
    /// into the drain: Arrived() lands the unit Idle a stride short of the
    /// Core, inside LeakRadius.
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
        IssueAttackMove(TDMap.CoreSite);
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

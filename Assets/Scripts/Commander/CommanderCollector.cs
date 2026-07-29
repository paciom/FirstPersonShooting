using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The economy on wheels: drives to the nearest live crystal field, mines a
/// load, hauls it home, repeat. Unarmed and slower than a fighter, with a
/// heavier shield — it is meant to be raided, and killing one is meant to
/// take a moment the defender can respond in.
///
/// The whole cycle lives in ThinkIdle, so any player Move order interrupts it
/// cleanly and the cycle resumes by itself on arrival. Mining is progressive
/// rather than one lump — the field visibly depletes while the collector
/// sits on it, and an interrupted collector keeps its partial load.
/// </summary>
public class CommanderCollector : CommanderUnit
{
    /// <summary>Credits a full load is worth.</summary>
    public const int Capacity = 300;

    /// <summary>Seconds parked on a field to fill from empty.</summary>
    const float MineSeconds = 8f;

    /// <summary>Close enough to the depot to dump the load.</summary>
    const float UnloadRadius = 9f;

    static readonly Color CargoAmber = new Color(1f, 0.72f, 0.25f);

    float _carrying;
    CrystalField _field;
    Vector3 _depot;

    public static CommanderCollector BuildCollector(string name, GameObject modelPrefab,
        int teamId, Vector3 position, float yaw)
    {
        var collector = Build<CommanderCollector>(name, modelPrefab, teamId, position, yaw,
            armed: false);

        // A hauler, not a hunter.
        collector.sightRange = 0f;
        collector.GetComponent<NavMeshAgent>().speed = 3.6f;

        var shield = collector.GetComponent<EnergyShield>();
        shield.maxShield = 120f;
        shield.Rematerialize();   // resync Current after the change, as ever

        // Permanent amber under-glow: from 45 m up, "which of these dots is
        // my economy" must not require clicking them.
        GlowQuad(collector.transform, "CargoGlow", "VFX/glow", CargoAmber, 0.5f, 1.9f, 0.04f);

        collector._depot = CommanderMap.BaseSite(teamId);
        return collector;
    }

    /// <summary>Collectors don't take attack orders — a right-click on an enemy is ignored.</summary>
    public override void IssueAttack(CommanderUnit target) { }

    /// <summary>
    /// A player order is also a retask: forget the remembered field so the
    /// cycle resumes at the NEAREST one from wherever the move ends. Without
    /// this, a collector pulled out of a raid marches straight back into it —
    /// the remembered field is still live, and no harvest order exists yet to
    /// say otherwise.
    /// </summary>
    public override void IssueMove(Vector3 destination)
    {
        _field = null;
        base.IssueMove(destination);
    }

    public override void IssueAttackMove(Vector3 destination)
    {
        _field = null;
        base.IssueAttackMove(destination);
    }

    protected override void ThinkIdle()
    {
        // Full enough to be worth banking, or nothing left anywhere to mine.
        if (_carrying >= Capacity || (_carrying > 0f && NoFieldLeft()))
        {
            if (FlatDistance(_depot) <= UnloadRadius)
            {
                CommanderEconomy.Grant(TeamId, Mathf.RoundToInt(_carrying));
                _carrying = 0f;
            }
            else
            {
                SetAgentDestination(_depot);
            }
            return;
        }

        if (_field == null || _field.IsExhausted)
            _field = CrystalField.Nearest(transform.position);
        if (_field == null)
            return;   // map mined dry — park

        if (FlatDistance(_field.transform.position) > CrystalField.HarvestRadius)
        {
            // Park short of the centre so two collectors on one field don't
            // fight over the same square metre of crystal.
            Vector3 approach = _field.transform.position
                + (transform.position - _field.transform.position).normalized * 4f;
            SetAgentDestination(approach);
            return;
        }

        // On the field: mine at the tick's share of the fill rate.
        _carrying += _field.Harvest(Capacity * (ThinkInterval / MineSeconds));
    }

    static bool NoFieldLeft() => CrystalField.Nearest(Vector3.zero) == null;

    float FlatDistance(Vector3 to)
    {
        Vector3 delta = to - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }
}

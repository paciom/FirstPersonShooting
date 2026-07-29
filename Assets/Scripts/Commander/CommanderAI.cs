using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A whole commander in one component: build order, army production, and
/// attack waves for one team. Drives the match through exactly the public
/// API the player's UI uses — CommanderEconomy.Spend, Building.Construct
/// behind BuildPlacer.IsValidPlacement, ProductionQueue.Enqueue, and unit
/// orders — so it cannot cheat, and the same class commands BOTH sides in
/// the AI-war spectator mode.
///
/// Strategy is rules, not planning: expand the base down the tech tree,
/// keep the collectors alive, mass a wave, throw it at the enemy HQ, and
/// make the next wave bigger. Crude — and exactly the opponent an 8-year-old
/// should get to beat.
/// </summary>
public class CommanderAI : MonoBehaviour
{
    public int teamId = 1;

    const float TickSeconds = 1.5f;
    /// <summary>Credits kept in hand for structures while the army shops.</summary>
    const int ArmyReserve = 300;

    float _nextTick;
    float _nextStragglerPush;
    [SerializeField] bool _assaulting;
    [SerializeField] int _waveSize = 8;

    static readonly (string key, float weight)[] ArmyMix =
    {
        (UnitCatalog.Ranger, 0.45f),
        (UnitCatalog.Panther, 0.25f),
        (UnitCatalog.Scout, 0.15f),
        (UnitCatalog.Titan, 0.15f),
    };

    void Update()
    {
        if (Time.time < _nextTick)
            return;
        _nextTick = Time.time + TickSeconds;

        TryBuildStructure();
        TryTrain();
        Command();
    }

    // ------------------------------------------------------------- base

    /// <summary>
    /// The opening book, as rules: power, economy, factory, guns, tech —
    /// then more power whenever the grid browns out, and a second factory
    /// once rich. Each tick places at most one structure.
    /// </summary>
    void TryBuildStructure()
    {
        string want = null;
        if (!Has(BuildingCatalog.PowerPlant)) want = BuildingCatalog.PowerPlant;
        else if (!Has(BuildingCatalog.Refinery)) want = BuildingCatalog.Refinery;
        else if (!Has(BuildingCatalog.Factory)) want = BuildingCatalog.Factory;
        else if (Count(BuildingCatalog.Turret) < 2) want = BuildingCatalog.Turret;
        else if (CommanderPower.Efficiency(teamId) < 1f) want = BuildingCatalog.PowerPlant;
        else if (!Has(BuildingCatalog.TechLab) && Credits() > 1800) want = BuildingCatalog.TechLab;
        else if (Count(BuildingCatalog.Factory) < 2 && Credits() > 2600) want = BuildingCatalog.Factory;

        if (want == null)
            return;
        var def = BuildingCatalog.Get(want);
        if (def == null || Credits() < def.cost)
            return;

        if (FindSpot(def, out Vector3 spot) && CommanderEconomy.Spend(teamId, def.cost))
            Building.Construct(def, teamId, spot);
    }

    /// <summary>
    /// Random rings around own structures, judged by the same law the
    /// player's ghost enforces. Turrets anchor on the HQ and bias toward the
    /// map centre — guns belong on the war-facing side of a base.
    /// </summary>
    bool FindSpot(BuildingDefinition def, out Vector3 spot)
    {
        var anchors = OwnBuildings();
        if (anchors.Count == 0)
        {
            spot = default;
            return false;
        }

        bool turret = def.key == BuildingCatalog.Turret;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Building anchor = turret
                ? (Headquarters() ?? anchors[Random.Range(0, anchors.Count)])
                : anchors[Random.Range(0, anchors.Count)];

            float angle;
            if (turret)
            {
                // Within ±50° of the direction the war comes from.
                float toWar = teamId == 0 ? 90f : -90f;   // +z for cyan, -z for magenta
                angle = (toWar + Random.Range(-50f, 50f)) * Mathf.Deg2Rad;
            }
            else
            {
                angle = Random.Range(0f, Mathf.PI * 2f);
            }

            float dist = Random.Range(8f, 14f);
            var candidate = anchor.transform.position
                + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            candidate = new Vector3(Mathf.Round(candidate.x), CommanderMap.GroundY,
                                    Mathf.Round(candidate.z));

            if (BuildPlacer.IsValidPlacement(def, teamId, candidate))
            {
                spot = candidate;
                return true;
            }
        }
        spot = default;
        return false;
    }

    // ------------------------------------------------------------- army

    void TryTrain()
    {
        var queue = ProductionQueue.LeastBusy(teamId);
        if (queue == null)
            return;

        // The economy comes first: two collectors minimum, always.
        var collectorDef = UnitCatalog.Get(UnitCatalog.Collector);
        if (CountCollectors() + ProductionQueue.TotalQueued(teamId, UnitCatalog.Collector) < 2
            && Credits() >= collectorDef.cost)
        {
            if (CommanderEconomy.Spend(teamId, collectorDef.cost))
                queue.Enqueue(UnitCatalog.Collector);
            return;
        }

        // Then soldiers, keeping a construction reserve in hand.
        var pick = PickArmyUnit();
        if (pick == null || Credits() < pick.cost + ArmyReserve)
            return;
        if (CommanderEconomy.Spend(teamId, pick.cost))
        {
            if (!queue.Enqueue(pick.key))
                CommanderEconomy.Grant(teamId, pick.cost);   // filled up this tick
        }
    }

    UnitDefinition PickArmyUnit()
    {
        float total = 0f;
        foreach (var (key, weight) in ArmyMix)
            if (Unlocked(key))
                total += weight;
        if (total <= 0f)
            return null;

        float roll = Random.value * total;
        foreach (var (key, weight) in ArmyMix)
        {
            if (!Unlocked(key))
                continue;
            roll -= weight;
            if (roll <= 0f)
                return UnitCatalog.Get(key);
        }
        return UnitCatalog.Get(UnitCatalog.Ranger);
    }

    bool Unlocked(string unitKey)
    {
        var def = UnitCatalog.Get(unitKey);
        return def != null && (def.prerequisite == null || Has(def.prerequisite));
    }

    /// <summary>
    /// Rally-then-wave: soldiers hold a line in front of the base until the
    /// wave quorum stands, then the whole wave attack-moves onto the enemy
    /// HQ — AttackMove picks its own fights on the way, which is where the
    /// mid-map battles come from. A spent wave (down to a couple of
    /// survivors) resets to rally, and the next quorum is bigger.
    /// </summary>
    void Command()
    {
        var fighters = Fighters();

        if (_assaulting && fighters.Count <= 2)
        {
            _assaulting = false;
            _waveSize = Mathf.Min(16, _waveSize + 2);
        }
        else if (!_assaulting && fighters.Count >= _waveSize)
        {
            _assaulting = true;
            _nextStragglerPush = 0f;   // push everyone immediately
        }

        if (Time.time < _nextStragglerPush)
            return;
        _nextStragglerPush = Time.time + 6f;

        Vector3 target = _assaulting ? EnemyHqPosition() : RallyPoint();
        float slack = _assaulting ? 30f : 24f;
        foreach (var fighter in fighters)
        {
            if (fighter.InCombat)
                continue;   // never yank a robot out of a live fight
            Vector3 flat = fighter.transform.position - target;
            flat.y = 0f;
            if (flat.magnitude <= slack)
                continue;
            var spread = new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            fighter.IssueAttackMove(target + spread);
        }
    }

    Vector3 RallyPoint()
    {
        Vector3 site = CommanderMap.BaseSite(teamId);
        float forward = teamId == 0 ? 1f : -1f;
        return site + new Vector3(0f, 0f, forward * 18f);
    }

    Vector3 EnemyHqPosition()
    {
        foreach (var building in Building.All)
            if (building != null && building.TeamId != teamId && building.IsAlive
                && building.Definition != null && building.Definition.isHeadquarters)
                return building.transform.position;
        // HQ gone — the match is ending; sweep whatever base remains.
        return CommanderMap.BaseSite(1 - teamId);
    }

    // ------------------------------------------------------------- queries

    int Credits() => CommanderEconomy.Credits(teamId);

    bool Has(string buildingKey) => Count(buildingKey) > 0;

    int Count(string buildingKey)
    {
        int count = 0;
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null && building.Definition.key == buildingKey)
                count++;
        return count;
    }

    List<Building> OwnBuildings()
    {
        var own = new List<Building>();
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive)
                own.Add(building);
        return own;
    }

    Building Headquarters()
    {
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null && building.Definition.isHeadquarters)
                return building;
        return null;
    }

    List<CommanderUnit> Fighters()
    {
        var fighters = new List<CommanderUnit>();
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == teamId && unit.IsAlive
                && !(unit is CommanderCollector))
                fighters.Add(unit);
        return fighters;
    }

    int CountCollectors()
    {
        int count = 0;
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == teamId && unit.IsAlive && unit is CommanderCollector)
                count++;
        return count;
    }
}

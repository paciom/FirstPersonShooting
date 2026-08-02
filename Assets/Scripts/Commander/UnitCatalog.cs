using UnityEngine;

/// <summary>
/// Everything a producible unit IS: price, prerequisites, chassis, and the
/// stats that get stamped onto the generic CommanderUnit rig. Same
/// data-drives-everything contract as BuildingCatalog — the build bar, the
/// factory queue and the AI commander all read this one table.
/// </summary>
public class UnitDefinition
{
    public string key;
    public string displayName;
    public int cost;
    /// <summary>Building key that must stand before this unit is buildable (besides the factory itself).</summary>
    public string prerequisite;
    /// <summary>Roster robot whose model this unit wears; falls back to entry 0.</summary>
    public string robotName;
    public bool isCollector;

    public float speed;
    public float maxShield;
    public float damage;
    public float shotsPerSecond;
    public float sightRange;
    public float attackRange;

    /// <summary>
    /// Second gun in the loadout: "plasma", "rail" or "beam" (null = laser
    /// only). Units swap between their weapons mid-fight, arena-style.
    /// </summary>
    public string secondaryWeapon;

    /// <summary>Seconds of factory work at full power. Priced at cost/100.</summary>
    public float BuildSeconds => cost / 100f;

    /// <summary>Stamp these stats onto a freshly built rig.</summary>
    public void ApplyTo(CommanderUnit unit)
    {
        unit.sightRange = sightRange;
        unit.attackRange = attackRange;

        var agent = unit.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
            agent.speed = speed;

        var shield = unit.GetComponent<EnergyShield>();
        if (shield != null)
        {
            shield.maxShield = maxShield;
            shield.Rematerialize();   // resync Current, as ever
        }

        // The whole loadout takes the unit's damage number; each weapon
        // keeps its own cadence and projectile, which is the variety.
        foreach (var weapon in unit.GetComponentsInChildren<Weapon>())
        {
            weapon.damage = damage;
            if (weapon is LaserBlaster blaster)
                blaster.shotsPerSecond = shotsPerSecond;
        }
    }
}

public static class UnitCatalog
{
    public const string Ranger = "ranger";
    public const string Scout = "scout";
    public const string Panther = "panther";
    public const string Titan = "titan";
    public const string Collector = "collector";

    static UnitDefinition[] _all;

    public static UnitDefinition[] All
    {
        get
        {
            if (_all == null)
            {
                // Costs from COMMANDER_PLAN.md §3. The spread is the classic
                // triangle: scouts see, pantheras raid, titans break bases,
                // rangers do everything a little.
                _all = new[]
                {
                    new UnitDefinition
                    {
                        key = Ranger, displayName = "RANGER", cost = 300, robotName = "ranger",
                        speed = 4.2f, maxShield = 80f, damage = 10f, shotsPerSecond = 4f,
                        sightRange = 26f, attackRange = 20f, secondaryWeapon = "plasma",
                    },
                    new UnitDefinition
                    {
                        key = Scout, displayName = "SCOUT", cost = 400, robotName = "scout",
                        speed = 6.5f, maxShield = 50f, damage = 6f, shotsPerSecond = 5f,
                        sightRange = 34f, attackRange = 20f, secondaryWeapon = "rail",
                    },
                    new UnitDefinition
                    {
                        key = Panther, displayName = "PANTHER", cost = 500, robotName = "panther",
                        speed = 5.5f, maxShield = 70f, damage = 12f, shotsPerSecond = 4f,
                        sightRange = 26f, attackRange = 20f, secondaryWeapon = "beam",
                    },
                    new UnitDefinition
                    {
                        key = Titan, displayName = "TITAN", cost = 700, robotName = "titan",
                        prerequisite = BuildingCatalog.TechLab,
                        speed = 3.2f, maxShield = 200f, damage = 22f, shotsPerSecond = 1.6f,
                        sightRange = 26f, attackRange = 22f, secondaryWeapon = "plasma",
                    },
                    new UnitDefinition
                    {
                        key = Collector, displayName = "COLLECTOR", cost = 1000, robotName = "knight",
                        prerequisite = BuildingCatalog.Refinery, isCollector = true,
                        speed = 3.6f, maxShield = 120f,
                    },
                };
            }
            return _all;
        }
    }

    public static UnitDefinition Get(string key)
    {
        foreach (var def in All)
            if (def.key == key)
                return def;
        return null;
    }

    /// <summary>
    /// The roster model for a chassis name, or the roster default, or null
    /// (capsule fallback) when no roster exists at all.
    /// </summary>
    public static GameObject Model(RobotRoster roster, string robotName) =>
        EntryOf(roster, robotName).modelPrefab;

    /// <summary>
    /// The full roster entry — model AND vehicle form — for a chassis name,
    /// falling back to entry 0, or to default (all nulls) with no roster.
    /// </summary>
    public static RobotRoster.Entry EntryOf(RobotRoster roster, string robotName)
    {
        if (roster == null || !roster.HasRobots)
            return default;
        foreach (var entry in roster.robots)
            if (string.Equals(entry.displayName, robotName, System.StringComparison.OrdinalIgnoreCase))
                return entry;
        return roster.Get(0);
    }
}

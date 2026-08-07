using UnityEngine;

/// <summary>What a tower DOES — the half of its identity a BuildingDefinition can't say.</summary>
public enum TDTowerKind
{
    /// <summary>Fast short-range laser — the bread-and-butter gun.</summary>
    Pulse,
    /// <summary>Slow long-range railgun — the armor-cracker.</summary>
    Rail,
    /// <summary>Lobbed plasma with splash — the swarm answer.</summary>
    Mortar,
    /// <summary>No gun at all: a field that slows every raider inside it.</summary>
    Stasis,
    /// <summary>No gun either: photon income, drip-fed every few seconds.</summary>
    Refinery,
}

/// <summary>
/// Everything a tower IS, before one exists. Wraps a BuildingDefinition —
/// so Building.Construct raises it with the same Meshy model loading, shield,
/// rise animation and collapse the Commander structures get — and adds the
/// combat stats Building has no vocabulary for.
///
/// Data-only, like BuildingCatalog: the build bar, the ghost placer and the
/// tower brain all read this one table, so a balance change is one edit here.
/// </summary>
public class TDTowerDefinition
{
    public string key;
    public TDTowerKind kind;
    public int cost;

    /// <summary>Structure half: footprint, height, shield, and which Meshy GLB to wear.</summary>
    public BuildingDefinition building;

    public float range;
    public float damage;
    public float shotsPerSecond;

    /// <summary>Stasis only: speed multiplier and how long each pulse's grip lasts.</summary>
    public float slowFactor;
    public float slowSeconds;

    /// <summary>Refinery only: credits granted every <see cref="tickSeconds"/>.</summary>
    public int incomePerTick;
    public float tickSeconds;

    public string displayName => building.displayName;
    public Color accent => building.accent;
}

public static class TDTowerCatalog
{
    public const string Pulse = "td-pulse";
    public const string Rail = "td-rail";
    public const string Mortar = "td-mortar";
    public const string Stasis = "td-stasis";
    public const string Refinery = "td-refinery";

    /// <summary>The Photon Core — the thing the whole mode defends. Not buildable.</summary>
    public const string Core = "td-core";

    static TDTowerDefinition[] _all;
    static BuildingDefinition _core;

    /// <summary>
    /// The five buildable towers, in build-bar order. Every modelKey below
    /// is a Commander building GLB in Resources/Buildings — the whole
    /// Commander model budget re-enlisted: the turret aims, the tech lab
    /// snipes, the factory lobs, the power plant hums a stasis field, and
    /// the refinery does what refineries do — makes money.
    /// </summary>
    public static TDTowerDefinition[] All
    {
        get
        {
            if (_all == null)
            {
                _all = new[]
                {
                    // Shields sized for a world where raiders SHOOT BACK:
                    // a passing wave scorches a tower, a neglected one dies.
                    // Building regen (6/s after 8 s calm) heals the scorch
                    // between waves — chip damage is pressure, not attrition.
                    new TDTowerDefinition
                    {
                        key = Pulse, kind = TDTowerKind.Pulse, cost = 100,
                        range = 15f, damage = 7f, shotsPerSecond = 3.5f,
                        building = Structure(Pulse, "PULSE TURRET", "turret",
                            new Vector2(2.2f, 2.2f), 2.6f, 500f, new Color(1f, 0.35f, 0.3f)),
                    },
                    new TDTowerDefinition
                    {
                        key = Rail, kind = TDTowerKind.Rail, cost = 260,
                        range = 26f, damage = 45f, shotsPerSecond = 0.55f,
                        building = Structure(Rail, "RAIL SPIRE", "tech",
                            new Vector2(2.6f, 2.6f), 3.6f, 550f, new Color(0.4f, 0.75f, 1f)),
                    },
                    new TDTowerDefinition
                    {
                        key = Mortar, kind = TDTowerKind.Mortar, cost = 220,
                        range = 19f, damage = 16f, shotsPerSecond = 0.8f,
                        building = Structure(Mortar, "PLASMA MORTAR", "factory",
                            new Vector2(3f, 3f), 3f, 550f, new Color(0.9f, 0.5f, 1f)),
                    },
                    new TDTowerDefinition
                    {
                        key = Stasis, kind = TDTowerKind.Stasis, cost = 140,
                        range = 8.5f, slowFactor = 0.5f, slowSeconds = 2f,
                        building = Structure(Stasis, "STASIS COIL", "power",
                            new Vector2(2.4f, 2.4f), 2.8f, 450f, new Color(0.55f, 1f, 0.4f)),
                    },
                    new TDTowerDefinition
                    {
                        key = Refinery, kind = TDTowerKind.Refinery, cost = 150,
                        incomePerTick = 6, tickSeconds = 2f,
                        building = Structure(Refinery, "PHOTON REFINERY", "refinery",
                            new Vector2(3f, 2.4f), 2.8f, 550f, new Color(1f, 0.72f, 0.25f)),
                    },
                };
            }
            return _all;
        }
    }

    /// <summary>
    /// The Core's structure definition: the Command Center model in its
    /// defender's role. Big shield only so stray splash never chips it —
    /// raiders don't shoot it, they drain it by ARRIVING (see TDMatch).
    /// </summary>
    public static BuildingDefinition CoreDefinition =>
        _core ?? (_core = new BuildingDefinition
        {
            key = Core, displayName = "PHOTON CORE", modelKey = "command",
            cost = 0, power = 0, footprint = new Vector2(6f, 6f), height = 5f,
            maxShield = 6000f, isHeadquarters = true,
            accent = new Color(0.2f, 0.9f, 1f),
        });

    public static TDTowerDefinition Get(string key)
    {
        foreach (var def in All)
            if (def.key == key)
                return def;
        return null;
    }

    /// <summary>One structure entry, TD-priced: cost lives on the tower, not the building.</summary>
    static BuildingDefinition Structure(string key, string name, string modelKey,
        Vector2 footprint, float height, float shield, Color accent)
    {
        return new BuildingDefinition
        {
            key = key, displayName = name, modelKey = modelKey,
            cost = 0, power = 0, footprint = footprint, height = height,
            maxShield = shield, accent = accent,
        };
    }
}

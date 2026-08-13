using UnityEngine;

/// <summary>
/// Everything a structure IS, before one exists: costs, power, footprints,
/// prerequisites, and how to draw the block placeholder. One definition per
/// building, catalogued in build-bar order.
///
/// Data-only on purpose — the same table drives the player's build bar, the
/// ghost placer's validity checks, and (Phase 5) the AI commander's build
/// order, so a balance change is one edit here.
/// </summary>
public class BuildingDefinition
{
    public string key;
    public string displayName;

    /// <summary>
    /// Which Resources/Buildings GLB this structure wears, when that differs
    /// from <see cref="key"/>. Null means the key IS the model name — true
    /// for every Commander building. Tower Defense reuses the same six
    /// models under its own keys, which is the whole reason this exists.
    /// </summary>
    public string modelKey;

    public int cost;
    /// <summary>Positive supplies the grid, negative draws from it.</summary>
    public int power;
    /// <summary>Footprint on the ground, metres.</summary>
    public Vector2 footprint;
    public float height;
    /// <summary>Key of the building that unlocks this one; null = always available.</summary>
    public string prerequisite;
    public float maxShield;
    /// <summary>Accent colour for the placeholder block's glow trim.</summary>
    public Color accent = new Color(1f, 0.72f, 0.25f);

    /// <summary>Command Centers win/lose the match; exactly one per team, pre-placed.</summary>
    public bool isHeadquarters;
}

public static class BuildingCatalog
{
    public const string CommandCenter = "command";
    public const string PowerPlant = "power";
    public const string Refinery = "refinery";
    public const string Factory = "factory";
    public const string Turret = "turret";
    public const string TechLab = "tech";
    public const string Airbase = "airbase";
    public const string Missiles = "missiles";

    static BuildingDefinition[] _all;

    public static BuildingDefinition[] All
    {
        get
        {
            if (_all == null)
            {
                // Costs follow COMMANDER_PLAN.md §3. Shields scale with cost:
                // a Command Center should survive a raid, a turret shouldn't
                // outlast the army defending it.
                _all = new[]
                {
                    new BuildingDefinition
                    {
                        key = CommandCenter, displayName = "COMMAND CENTER",
                        cost = 2500, power = 50, footprint = new Vector2(6f, 6f), height = 5f,
                        maxShield = 900f, isHeadquarters = true,
                        accent = new Color(0.2f, 0.9f, 1f),
                    },
                    new BuildingDefinition
                    {
                        key = PowerPlant, displayName = "POWER PLANT",
                        cost = 300, power = 100, footprint = new Vector2(4f, 4f), height = 3.4f,
                        prerequisite = CommandCenter, maxShield = 220f,
                        accent = new Color(0.55f, 1f, 0.4f),
                    },
                    new BuildingDefinition
                    {
                        key = Refinery, displayName = "REFINERY",
                        cost = 1400, power = -30, footprint = new Vector2(6f, 4f), height = 3.8f,
                        prerequisite = PowerPlant, maxShield = 450f,
                        accent = new Color(1f, 0.72f, 0.25f),
                    },
                    new BuildingDefinition
                    {
                        key = Factory, displayName = "ROBOT FACTORY",
                        cost = 1500, power = -50, footprint = new Vector2(6f, 6f), height = 4.2f,
                        prerequisite = Refinery, maxShield = 520f,
                        accent = new Color(0.9f, 0.5f, 1f),
                    },
                    new BuildingDefinition
                    {
                        key = Turret, displayName = "PHOTON TURRET",
                        cost = 600, power = -40, footprint = new Vector2(2f, 2f), height = 2.6f,
                        prerequisite = PowerPlant, maxShield = 260f,
                        accent = new Color(1f, 0.35f, 0.3f),
                    },
                    new BuildingDefinition
                    {
                        key = TechLab, displayName = "TECH LAB",
                        cost = 1200, power = -60, footprint = new Vector2(4f, 4f), height = 3.6f,
                        prerequisite = Factory, maxShield = 380f,
                        accent = new Color(0.4f, 0.75f, 1f),
                    },
                    new BuildingDefinition
                    {
                        // Each one standing sustains CommanderAir.JetsPerAirbase
                        // robots in jet form at once — air power is a building
                        // you protect, not a button you press.
                        key = Airbase, displayName = "AIRBASE",
                        cost = 1600, power = -40, footprint = new Vector2(5f, 5f), height = 3f,
                        prerequisite = TechLab, maxShield = 420f,
                        accent = new Color(0.45f, 0.85f, 1f),
                    },
                    new BuildingDefinition
                    {
                        // The counter: homing missiles from the Missiles Pack,
                        // fired only at airborne robots. See BuildingMissiles.
                        key = Missiles, displayName = "MISSILE BATTERY",
                        cost = 900, power = -30, footprint = new Vector2(3f, 3f), height = 2.2f,
                        prerequisite = Factory, maxShield = 300f,
                        accent = new Color(1f, 0.55f, 0.2f),
                    },
                };
            }
            return _all;
        }
    }

    public static BuildingDefinition Get(string key)
    {
        foreach (var def in All)
            if (def.key == key)
                return def;
        return null;
    }
}

using UnityEngine;

/// <summary>
/// Everything a defender robot IS: price, chassis, and the stats stamped
/// onto its CommanderUnit rig. Deliberately NOT Commander's UnitCatalog —
/// same shape, different economy: these prices answer TD bounties and
/// wave-clear bonuses, not refinery income, and a defender that dies is
/// bought again, so the whole table runs a third of Commander's scale.
/// </summary>
public class TDDefenderDefinition
{
    public string key;
    public string displayName;
    public int cost;
    /// <summary>Roster robot whose model (and vehicle form) this defender wears.</summary>
    public string robotName;
    /// <summary>One-line role note for the build bar.</summary>
    public string role;

    public float speed;
    public float maxShield;
    public float damage;
    public float shotsPerSecond;
    public float sightRange;
    public float attackRange;

    /// <summary>Second gun to swap to mid-fight: "plasma", "rail" or "beam".</summary>
    public string secondaryWeapon;
}

public static class TDDefenderCatalog
{
    public const string Ranger = "td-ranger";
    public const string Scout = "td-scout";
    public const string Panther = "td-panther";
    public const string Titan = "td-titan";

    static TDDefenderDefinition[] _all;

    /// <summary>The four hires, in build-bar order — cheap bulk up to heavy anchor.</summary>
    public static TDDefenderDefinition[] All
    {
        get
        {
            if (_all == null)
            {
                _all = new[]
                {
                    new TDDefenderDefinition
                    {
                        key = Scout, displayName = "SCOUT", cost = 90, robotName = "scout",
                        role = "fast screen",
                        speed = 6.5f, maxShield = 55f, damage = 6f, shotsPerSecond = 5f,
                        sightRange = 30f, attackRange = 18f, secondaryWeapon = "rail",
                    },
                    new TDDefenderDefinition
                    {
                        key = Ranger, displayName = "RANGER", cost = 120, robotName = "ranger",
                        role = "all-rounder",
                        speed = 4.2f, maxShield = 90f, damage = 9f, shotsPerSecond = 4f,
                        sightRange = 26f, attackRange = 18f, secondaryWeapon = "plasma",
                    },
                    new TDDefenderDefinition
                    {
                        key = Panther, displayName = "PANTHER", cost = 160, robotName = "panther",
                        role = "striker",
                        speed = 5.5f, maxShield = 110f, damage = 12f, shotsPerSecond = 4f,
                        sightRange = 26f, attackRange = 18f, secondaryWeapon = "beam",
                    },
                    new TDDefenderDefinition
                    {
                        key = Titan, displayName = "TITAN", cost = 320, robotName = "titan",
                        role = "heavy anchor",
                        speed = 3.2f, maxShield = 260f, damage = 20f, shotsPerSecond = 1.6f,
                        sightRange = 26f, attackRange = 20f, secondaryWeapon = "plasma",
                    },
                };
            }
            return _all;
        }
    }

    public static TDDefenderDefinition Get(string key)
    {
        foreach (var def in All)
            if (def.key == key)
                return def;
        return null;
    }
}

using UnityEngine;

/// <summary>
/// An enemy structure standing on the field, and what knocking it down is worth.
///
/// The buildings are Commander's — the same six GLBs in Resources/Buildings,
/// which already exist, are already textured, and already read as "installation"
/// at a glance. Reusing them costs nothing and makes the two modes feel like
/// they are set in the same war.
///
/// An outpost is the mode's one PIECE OF TERRAIN WORTH ARGUING WITH. Everything
/// else on the field is coming at you and can be driven past; an outpost sits
/// still, shoots hard, and holds an upgrade you keep for the rest of the run.
/// It is the only thing in the mode the player has to decide about.
///
/// The structure itself is an ordinary <see cref="TankPawn"/> on the
/// <see cref="TankPawn.Chassis.Structure"/> chassis, so it is targeted, shot,
/// wrecked and swept by machinery that already existed. This component is only
/// the label and the prize.
/// </summary>
public class TankOutpost : MonoBehaviour
{
    /// <summary>What a captured outpost is worth. Multipliers are stacking factors.</summary>
    public struct Reward
    {
        public string title;
        public string blurb;
        public float damageScale;
        public float rateScale;
        public float shieldBonus;
        public float regenScale;
        public int allyCapBonus;
        /// <summary>Recruits that drive out of the wreckage immediately.</summary>
        public int recruits;
    }

    /// <summary>Which building it wears, and what it drops. Set by the spawner.</summary>
    public string buildingKey;
    public Reward reward;

    /// <summary>
    /// The six, in rising order of what they are worth.
    ///
    /// Every one of them is a DIFFERENT KIND of stronger, not a bigger number on
    /// the same axis — a run that captures a turret and a tech lab plays visibly
    /// differently from one that captured two reactors. And each reads off its
    /// own model: the turret gives you its gun, the factory gives you its robots,
    /// the power plant gives you its reactor.
    /// </summary>
    public static Reward RewardFor(string buildingKey)
    {
        switch (buildingKey)
        {
            case BuildingCatalog.PowerPlant:
                return new Reward
                {
                    title = "REACTOR",
                    blurb = "+200 SHIELD",
                    damageScale = 1f, rateScale = 1f, regenScale = 1f,
                    shieldBonus = 200f,
                };

            case BuildingCatalog.Refinery:
                return new Reward
                {
                    title = "COOLANT LINES",
                    blurb = "SHIELD RECHARGES FASTER",
                    damageScale = 1f, rateScale = 1f, regenScale = 2.2f,
                };

            case BuildingCatalog.Turret:
                return new Reward
                {
                    title = "HEAVY SHELLS",
                    blurb = "+45% CANNON DAMAGE",
                    damageScale = 1.45f, rateScale = 1f, regenScale = 1f,
                };

            case BuildingCatalog.TechLab:
                return new Reward
                {
                    title = "TARGETING UPLINK",
                    blurb = "+40% FIRE RATE",
                    damageScale = 1f, rateScale = 1.4f, regenScale = 1f,
                };

            case BuildingCatalog.Factory:
                return new Reward
                {
                    title = "FACTORY SEIZED",
                    blurb = "TWO TANKS JOIN YOU",
                    damageScale = 1f, rateScale = 1f, regenScale = 1f,
                    allyCapBonus = 2, recruits = 2,
                };

            // The prize. Rare, heavily defended, and worth crossing the field for.
            default:
                return new Reward
                {
                    title = "COMMAND CENTER",
                    blurb = "EVERYTHING AT ONCE",
                    damageScale = 1.3f, rateScale = 1.25f, regenScale = 1.5f,
                    shieldBonus = 150f, allyCapBonus = 1, recruits = 2,
                };
        }
    }

    /// <summary>
    /// Which outposts may appear, and how far in.
    ///
    /// Ordered so the early ones are the plain force multipliers and the command
    /// center only shows up once a run is going well enough to have earned it.
    /// The gate is DISTANCE, the same clock the difficulty runs on, so the two
    /// stay in step.
    /// </summary>
    public static string RollBuilding(float metres)
    {
        var pool = metres < 700f
            ? new[] { BuildingCatalog.PowerPlant, BuildingCatalog.Turret, BuildingCatalog.Refinery }
            : metres < 1600f
                ? new[]
                {
                    BuildingCatalog.PowerPlant, BuildingCatalog.Turret, BuildingCatalog.Refinery,
                    BuildingCatalog.TechLab, BuildingCatalog.Factory,
                }
                : new[]
                {
                    BuildingCatalog.Turret, BuildingCatalog.TechLab, BuildingCatalog.Factory,
                    BuildingCatalog.CommandCenter,
                };
        return pool[Random.Range(0, pool.Length)];
    }

    /// <summary>How much shield an outpost of this kind carries. The prize is the toughest.</summary>
    public static float ShieldFor(string buildingKey) =>
        buildingKey == BuildingCatalog.CommandCenter ? 1400f
            : buildingKey == BuildingCatalog.Factory ? 900f
            : 650f;
}

using UnityEngine;

/// <summary>
/// The kinds of thing that parachute into the arena mid-match. Robots only
/// carry two basic guns, so everything else — the whole 52-weapon arsenal,
/// repairs, reinforcements — arrives from the sky and has to be fought over.
/// </summary>
public enum TreasureKind
{
    WeaponPod,
    RepairPack,
    GoldBars,
    Bomb,
    Overshield,
    TurboCells,
    EmpCharge,
    MysteryCube,
}

/// <summary>Static data for one treasure kind: how it reads on camera and how often it drops.</summary>
public class TreasureDef
{
    public TreasureKind kind;
    public string displayName;
    public string blurb;          // announcer subtitle
    public Color color;
    public float weight;          // relative drop chance
    public bool hazard;           // true = nobody wants to touch it (the bomb)
}

/// <summary>
/// The one table of treasure kinds. Adding a new drop is a row here plus a case
/// in <see cref="TreasureEffects"/> (and optionally a payload shape in
/// <see cref="TreasureDrop"/>) — nothing else needs to know.
/// </summary>
public static class TreasureCatalog
{
    public static readonly TreasureDef[] All =
    {
        new TreasureDef
        {
            kind = TreasureKind.WeaponPod,
            displayName = "WEAPON POD",
            blurb = "a random gun from the full arsenal",
            color = new Color(1f, 0.55f, 0.12f),
            weight = 30f,
        },
        new TreasureDef
        {
            kind = TreasureKind.RepairPack,
            displayName = "REPAIR PACK",
            blurb = "patches the shield back up",
            color = new Color(0.30f, 1f, 0.55f),
            weight = 20f,
        },
        new TreasureDef
        {
            kind = TreasureKind.GoldBars,
            displayName = "GOLD BARS",
            blurb = "team funds — enough of it builds a new robot",
            color = new Color(1f, 0.82f, 0.25f),
            weight = 16f,
        },
        new TreasureDef
        {
            kind = TreasureKind.Bomb,
            displayName = "SCRAP MINE",
            blurb = "DON'T TOUCH IT — shoot it from a safe distance",
            color = new Color(1f, 0.22f, 0.16f),
            weight = 14f,
            hazard = true,
        },
        new TreasureDef
        {
            kind = TreasureKind.Overshield,
            displayName = "OVERSHIELD",
            blurb = "bonus shield layer for a while",
            color = new Color(0.45f, 0.85f, 1f),
            weight = 8f,
        },
        new TreasureDef
        {
            kind = TreasureKind.TurboCells,
            displayName = "TURBO CELLS",
            blurb = "a big burst of speed",
            color = new Color(0.80f, 1f, 0.25f),
            weight = 6f,
        },
        new TreasureDef
        {
            kind = TreasureKind.EmpCharge,
            displayName = "EMP CHARGE",
            blurb = "freezes every nearby enemy solid",
            color = new Color(0.65f, 0.40f, 1f),
            weight = 4f,
        },
        new TreasureDef
        {
            kind = TreasureKind.MysteryCube,
            displayName = "MYSTERY CUBE",
            blurb = "could be anything… including the mine",
            color = new Color(1f, 0.35f, 0.85f),
            weight = 2f,
        },
    };

    public static TreasureDef Get(TreasureKind kind)
    {
        foreach (var def in All)
            if (def.kind == kind)
                return def;
        return All[0];
    }

    /// <summary>Weighted random pick over the whole table.</summary>
    public static TreasureDef Roll() => Roll(includeMystery: true);

    /// <summary>
    /// Weighted pick; <paramref name="includeMystery"/> false is what the
    /// Mystery Cube itself rolls on, so it can't chain into another cube.
    /// </summary>
    public static TreasureDef Roll(bool includeMystery)
    {
        float total = 0f;
        foreach (var def in All)
            if (includeMystery || def.kind != TreasureKind.MysteryCube)
                total += def.weight;

        float r = Random.value * total;
        foreach (var def in All)
        {
            if (!includeMystery && def.kind == TreasureKind.MysteryCube)
                continue;
            r -= def.weight;
            if (r <= 0f)
                return def;
        }
        return All[0];
    }
}

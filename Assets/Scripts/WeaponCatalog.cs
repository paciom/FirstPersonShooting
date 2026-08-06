using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one list of every expanded-arsenal weapon. ArenaBuilder calls AttachAll
/// to put the full kit on the player and every bot; each weapon names and
/// colors itself in Awake, so no per-weapon wiring is needed here.
///
/// ORDER IS THE CONTRACT. A character's WeaponLoadout indexes into this list by
/// position (and the debug console numbers weapons by it), and those indices are
/// serialized into the arena scene — so new weapons go on the END, never in the
/// middle.
/// </summary>
public static class WeaponCatalog
{
    /// <summary>
    /// A named family of weapons — one tab in the arsenal picker.
    ///
    /// The families are not new: they were the comment headings that had always
    /// separated the catalogue. Sixty guns in one flat list is unusable in a
    /// picker and unmemorable in the hand, and the headings were already the
    /// grouping a player would guess at.
    /// </summary>
    public sealed class Group
    {
        public readonly string name;
        public readonly System.Type[] types;

        public Group(string name, params System.Type[] types)
        {
            this.name = name;
            this.types = types;
        }
    }

    /// <summary>
    /// The four guns every character has carried since before the catalogue
    /// existed. ArenaBuilder attaches these itself, ahead of everything here, so
    /// they are NOT in <see cref="Types"/> — they are listed only so the picker
    /// has a tab for them.
    /// </summary>
    public static readonly Group Core = new Group("CORE",
        typeof(LaserBlaster), typeof(PhotonBeam), typeof(PlasmaLobber), typeof(RailZapper));

    /// <summary>
    /// The expanded arsenal, by family. <see cref="Types"/> is this flattened,
    /// in this order — so ORDER IS STILL THE CONTRACT: a character's
    /// WeaponLoadout indexes into it by position and those indices are
    /// serialized into the arena scene. New weapons go on the END of a family,
    /// and a new family goes on the END of this list. Reordering the families
    /// renumbers every weapon after the one that moved.
    /// </summary>
    public static readonly Group[] Families =
    {
        new Group("LIGHT",
            typeof(PrismSplitter),
            typeof(StrobeBurst),
            typeof(SunflareCannon),
            typeof(GlowwormLauncher),
            typeof(MirrorRicochet),
            typeof(HaloRingGun),
            typeof(BlacklightMarker)),
        new Group("ELECTRIC",
            typeof(ArcWhip),
            typeof(TeslaTurretThrower),
            typeof(MagnetRam),
            typeof(StaticShotgun),
            typeof(VoltBoomerang),
            typeof(IonRain)),
        new Group("PLASMA",
            typeof(CometSling),
            typeof(EmberGatling),
            typeof(MagmaMortar),
            typeof(PhoenixDart),
            typeof(HeatHazeProjector)),
        new Group("CRYO",
            typeof(FrostbiteBeam),
            typeof(IcicleFlechette),
            typeof(SnowglobeGrenade),
            typeof(GlacierWall)),
        new Group("SOUND",
            typeof(BassDropper),
            typeof(SonicScreech),
            typeof(EchoLocator),
            typeof(DrumlineCannon),
            typeof(WaveRider)),
        new Group("GRAVITY",
            typeof(BlackHoleYoyo),
            typeof(RepulsorPalm),
            typeof(MoonbootsBeam),
            typeof(MeteorCaller),
            typeof(OrbitLauncher)),
        new Group("GOO",
            typeof(BubbleBlower),
            typeof(GooGusher),
            typeof(BouncyBallCannon),
            typeof(GlueGrenade),
            typeof(PaintBomber)),
        new Group("NATURE",
            typeof(TornadoTube),
            typeof(VineSnare),
            typeof(ThundercloudPet),
            typeof(SandstormSprayer),
            typeof(GeyserRod)),
        new Group("GADGETS",
            typeof(PortalPistol),
            typeof(CloneDecoyCaster),
            typeof(TimeBubbleBomb),
            typeof(SwarmHive),
            typeof(RicochetDisc),
            typeof(ShrinkRay),
            typeof(MimicCube),
            typeof(FireworksFinale)),
        // Real rocket bodies from the Missiles Pack (MissileModels)
        new Group("MISSILES",
            typeof(SeekerMissile),
            typeof(HornetSwarm),
            typeof(ClusterMissile),
            typeof(SkyStriker),
            typeof(SkimmerRocket),
            typeof(SiegeTorpedo)),
    };

    /// <summary>Every tab the picker shows: the core four, then each family.</summary>
    public static readonly Group[] Tabs;

    /// <summary>
    /// Every expanded weapon, in canonical order — <see cref="Families"/>
    /// flattened. A list of types rather than a list of AddComponent calls so
    /// that <see cref="TopUp"/> can tell what a character is missing without a
    /// second copy of the list to keep in step.
    /// </summary>
    public static readonly System.Type[] Types;

    static readonly Dictionary<System.Type, Group> GroupByType = new Dictionary<System.Type, Group>();

    static WeaponCatalog()
    {
        Tabs = new Group[Families.Length + 1];
        Tabs[0] = Core;
        Families.CopyTo(Tabs, 1);

        var types = new List<System.Type>();
        foreach (var family in Families)
            types.AddRange(family.types);
        Types = types.ToArray();

        foreach (var tab in Tabs)
            foreach (var type in tab.types)
                GroupByType[type] = tab;
    }

    /// <summary>
    /// Which tab a live weapon belongs to, or null for one the catalogue has
    /// never heard of — the vehicle siege kit, which is mounted rather than
    /// carried and never appears in the picker.
    /// </summary>
    public static Group GroupOf(Weapon weapon)
    {
        if (weapon == null)
            return null;
        return GroupByType.TryGetValue(weapon.GetType(), out var group) ? group : null;
    }

    /// <summary>Attach every expanded weapon to a host (blaster viewmodel) and return them.</summary>
    public static Weapon[] AttachAll(GameObject host, Transform muzzle, Transform owner)
    {
        var weapons = new Weapon[Types.Length];
        for (int i = 0; i < Types.Length; i++)
            weapons[i] = Add(host, Types[i], muzzle, owner);
        return weapons;
    }

    /// <summary>
    /// Give a character any catalog weapon it doesn't already carry, appending
    /// the newcomers to its <see cref="WeaponLoadout.all"/>. Returns how many
    /// were added.
    ///
    /// The arena scene has each character's whole arsenal serialized into it, so
    /// a weapon added to this catalog after the scene was last built exists in
    /// code and nowhere else — no Weapon Pod could ever roll it. Rather than
    /// making every new weapon wait on a scene rebuild, characters top
    /// themselves up when the match loads.
    ///
    /// Appends, so every existing index — and every basic — keeps its meaning.
    /// </summary>
    public static int TopUp(WeaponLoadout loadout)
    {
        if (loadout == null || loadout.all == null || loadout.all.Length == 0)
            return 0;

        // The host, muzzle and owner are taken from a weapon that is already
        // there: they were resolved once when the scene was built, and a
        // character's arsenal all lives on the one blaster.
        Weapon template = null;
        foreach (var weapon in loadout.all)
            if (weapon != null) { template = weapon; break; }
        if (template == null)
            return 0;

        var host = template.gameObject;
        List<Weapon> added = null;
        foreach (var type in Types)
        {
            if (host.GetComponent(type) != null)
                continue;
            (added ??= new List<Weapon>()).Add(Add(host, type, template.muzzle, template.ownerRoot));
        }
        if (added == null)
            return 0;

        var grown = new Weapon[loadout.all.Length + added.Count];
        loadout.all.CopyTo(grown, 0);
        for (int i = 0; i < added.Count; i++)
            grown[loadout.all.Length + i] = added[i];
        loadout.all = grown;
        return added.Count;
    }

    static Weapon Add(GameObject host, System.Type type, Transform muzzle, Transform owner)
    {
        // At runtime AddComponent runs Awake before we get the reference back,
        // so the weapon wakes on Weapon's own defaults (muzzle = the blaster,
        // owner = the character root — which is where the team id comes from,
        // and is already correct). Re-pointing the muzzle afterwards is safe:
        // the shared flash light re-homes itself the first time it fires.
        var weapon = (Weapon)host.AddComponent(type);
        weapon.muzzle = muzzle;
        weapon.ownerRoot = owner;
        return weapon;
    }
}

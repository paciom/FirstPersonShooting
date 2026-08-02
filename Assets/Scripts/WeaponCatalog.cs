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
    /// Every expanded weapon, in canonical order. A list of types rather than a
    /// list of AddComponent calls so that <see cref="TopUp"/> can tell what a
    /// character is missing without a second copy of the list to keep in step.
    /// </summary>
    public static readonly System.Type[] Types =
    {
        // Light & photon
        typeof(PrismSplitter),
        typeof(StrobeBurst),
        typeof(SunflareCannon),
        typeof(GlowwormLauncher),
        typeof(MirrorRicochet),
        typeof(HaloRingGun),
        typeof(BlacklightMarker),
        // Electric & magnetic
        typeof(ArcWhip),
        typeof(TeslaTurretThrower),
        typeof(MagnetRam),
        typeof(StaticShotgun),
        typeof(VoltBoomerang),
        typeof(IonRain),
        // Plasma & heat
        typeof(CometSling),
        typeof(EmberGatling),
        typeof(MagmaMortar),
        typeof(PhoenixDart),
        typeof(HeatHazeProjector),
        // Cryo & ice
        typeof(FrostbiteBeam),
        typeof(IcicleFlechette),
        typeof(SnowglobeGrenade),
        typeof(GlacierWall),
        // Sound & waves
        typeof(BassDropper),
        typeof(SonicScreech),
        typeof(EchoLocator),
        typeof(DrumlineCannon),
        typeof(WaveRider),
        // Gravity & force
        typeof(BlackHoleYoyo),
        typeof(RepulsorPalm),
        typeof(MoonbootsBeam),
        typeof(MeteorCaller),
        typeof(OrbitLauncher),
        // Goo, bubbles & slime
        typeof(BubbleBlower),
        typeof(GooGusher),
        typeof(BouncyBallCannon),
        typeof(GlueGrenade),
        typeof(PaintBomber),
        // Nature & elemental
        typeof(TornadoTube),
        typeof(VineSnare),
        typeof(ThundercloudPet),
        typeof(SandstormSprayer),
        typeof(GeyserRod),
        // Gadgets & exotic
        typeof(PortalPistol),
        typeof(CloneDecoyCaster),
        typeof(TimeBubbleBomb),
        typeof(SwarmHive),
        typeof(RicochetDisc),
        typeof(ShrinkRay),
        typeof(MimicCube),
        typeof(FireworksFinale),
        // Missiles — real rocket bodies from the Missiles Pack (MissileModels)
        typeof(SeekerMissile),
        typeof(HornetSwarm),
        typeof(ClusterMissile),
        typeof(SkyStriker),
        typeof(SkimmerRocket),
        typeof(SiegeTorpedo),
    };

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

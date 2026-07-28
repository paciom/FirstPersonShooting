using UnityEngine;

/// <summary>
/// The one list of every expanded-arsenal weapon. ArenaBuilder calls AttachAll
/// to put the full kit on the player and every bot; each weapon names and
/// colors itself in Awake, so no per-weapon wiring is needed here.
/// </summary>
public static class WeaponCatalog
{
    /// <summary>Attach all 50 expanded weapons to a host (blaster viewmodel) and return them.</summary>
    public static Weapon[] AttachAll(GameObject host, Transform muzzle, Transform owner)
    {
        return new Weapon[]
        {
            // Light & photon
            Add<PrismSplitter>(host, muzzle, owner),
            Add<StrobeBurst>(host, muzzle, owner),
            Add<SunflareCannon>(host, muzzle, owner),
            Add<GlowwormLauncher>(host, muzzle, owner),
            Add<MirrorRicochet>(host, muzzle, owner),
            Add<HaloRingGun>(host, muzzle, owner),
            Add<BlacklightMarker>(host, muzzle, owner),
            // Electric & magnetic
            Add<ArcWhip>(host, muzzle, owner),
            Add<TeslaTurretThrower>(host, muzzle, owner),
            Add<MagnetRam>(host, muzzle, owner),
            Add<StaticShotgun>(host, muzzle, owner),
            Add<VoltBoomerang>(host, muzzle, owner),
            Add<IonRain>(host, muzzle, owner),
            // Plasma & heat
            Add<CometSling>(host, muzzle, owner),
            Add<EmberGatling>(host, muzzle, owner),
            Add<MagmaMortar>(host, muzzle, owner),
            Add<PhoenixDart>(host, muzzle, owner),
            Add<HeatHazeProjector>(host, muzzle, owner),
            // Cryo & ice
            Add<FrostbiteBeam>(host, muzzle, owner),
            Add<IcicleFlechette>(host, muzzle, owner),
            Add<SnowglobeGrenade>(host, muzzle, owner),
            Add<GlacierWall>(host, muzzle, owner),
            // Sound & waves
            Add<BassDropper>(host, muzzle, owner),
            Add<SonicScreech>(host, muzzle, owner),
            Add<EchoLocator>(host, muzzle, owner),
            Add<DrumlineCannon>(host, muzzle, owner),
            Add<WaveRider>(host, muzzle, owner),
            // Gravity & force
            Add<BlackHoleYoyo>(host, muzzle, owner),
            Add<RepulsorPalm>(host, muzzle, owner),
            Add<MoonbootsBeam>(host, muzzle, owner),
            Add<MeteorCaller>(host, muzzle, owner),
            Add<OrbitLauncher>(host, muzzle, owner),
            // Goo, bubbles & slime
            Add<BubbleBlower>(host, muzzle, owner),
            Add<GooGusher>(host, muzzle, owner),
            Add<BouncyBallCannon>(host, muzzle, owner),
            Add<GlueGrenade>(host, muzzle, owner),
            Add<PaintBomber>(host, muzzle, owner),
            // Nature & elemental
            Add<TornadoTube>(host, muzzle, owner),
            Add<VineSnare>(host, muzzle, owner),
            Add<ThundercloudPet>(host, muzzle, owner),
            Add<SandstormSprayer>(host, muzzle, owner),
            Add<GeyserRod>(host, muzzle, owner),
            // Gadgets & exotic
            Add<PortalPistol>(host, muzzle, owner),
            Add<CloneDecoyCaster>(host, muzzle, owner),
            Add<TimeBubbleBomb>(host, muzzle, owner),
            Add<SwarmHive>(host, muzzle, owner),
            Add<RicochetDisc>(host, muzzle, owner),
            Add<ShrinkRay>(host, muzzle, owner),
            Add<MimicCube>(host, muzzle, owner),
            Add<FireworksFinale>(host, muzzle, owner),
        };
    }

    static T Add<T>(GameObject host, Transform muzzle, Transform owner) where T : Weapon
    {
        var weapon = host.AddComponent<T>();
        weapon.muzzle = muzzle;
        weapon.ownerRoot = owner;
        return weapon;
    }
}

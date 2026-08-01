using UnityEngine;

/// <summary>
/// What a tank can be holding.
///
/// A CURATED TWELVE, not the arena's fifty-four. Two reasons, and neither is
/// laziness. The first is legibility: a weapon pod is a prize, and a prize the
/// player cannot tell apart from the last one is not a prize — twelve guns that
/// each look and sound like themselves beat fifty where a third are variations
/// on a theme. The second is the camera. This mode watches from eighteen metres
/// up, where a weapon that shines only up close (the whips, the palms, the
/// melee-range projectors) does nothing you can see, and a weapon that fills the
/// screen with a dome hides the fight underneath it.
///
/// Everything here fires forward, travels, and arrives somewhere visible.
///
/// Slot 0 is the cannon every tank always has; the rest are pod-only. That is
/// exactly the shape <see cref="WeaponLoadout"/> wants (basics plus one granted
/// special on a timer), so the grant, the countdown and the drop-on-de-rez all
/// come for free from the component the arena's airdrops already use.
/// </summary>
public static class TankArsenal
{
    /// <summary>How long a weapon pod's gun lasts. Long enough to enjoy, short
    /// enough that losing it is a reason to go and find another one.</summary>
    public const float PodSeconds = 20f;

    /// <summary>
    /// Attach the rack and hand it back in canonical order.
    ///
    /// <paramref name="podCapable"/> is the hero. Raiders get the cannon and
    /// nothing else: pods are a reward the player collects, so a raider's other
    /// eleven guns could never be reached — and eleven unreachable
    /// <see cref="Weapon"/> components on every one of a dozen live raiders is a
    /// hundred and thirty MonoBehaviours a frame doing nothing at all.
    /// </summary>
    public static Weapon[] Attach(GameObject host, Transform muzzle, Transform owner,
        TankPawn.Chassis kind, bool podCapable)
    {
        // Slot 0 on both: the main gun. Slower and heavier than the FPS blaster
        // — a tank's cannon should land, not chatter.
        var cannon = Cannon(host, muzzle, owner, kind);
        if (!podCapable)
            return new[] { cannon };

        var rack = new Weapon[]
        {
            cannon,
            Add<EmberGatling>(host, muzzle, owner),
            Add<StaticShotgun>(host, muzzle, owner),
            Add<CometSling>(host, muzzle, owner),
            Add<MagmaMortar>(host, muzzle, owner),
            Add<IcicleFlechette>(host, muzzle, owner),
            Add<RicochetDisc>(host, muzzle, owner),
            Add<HaloRingGun>(host, muzzle, owner),
            Add<SunflareCannon>(host, muzzle, owner),
            Add<DrumlineCannon>(host, muzzle, owner),
            Add<SwarmHive>(host, muzzle, owner),
            Add<FireworksFinale>(host, muzzle, owner),
        };
        return rack;
    }

    /// <summary>
    /// The main gun: a laser blaster wound down to a cannon's cadence and up to
    /// a cannon's punch. Bolt speed is left alone — travel time is what makes a
    /// moving target worth leading, and leading is most of the skill here.
    /// </summary>
    static LaserBlaster Cannon(GameObject host, Transform muzzle, Transform owner,
        TankPawn.Chassis kind)
    {
        var gun = Add<LaserBlaster>(host, muzzle, owner);
        bool tank = kind == TankPawn.Chassis.Tank;
        gun.weaponName = tank ? "Cannon" : "Rifle";
        gun.shotsPerSecond = tank ? 2.2f : 3.2f;
        gun.damage = tank ? 26f : 12f;
        gun.boltSpeed = 55f;
        return gun;
    }

    static T Add<T>(GameObject host, Transform muzzle, Transform owner) where T : Weapon
    {
        var weapon = host.AddComponent<T>();
        // Set before Awake ever runs: the host is inactive while a pawn is being
        // built (see TankPawn.Spawn), and Awake is where a weapon latches its
        // team off the owner's shield and defaults its muzzle to itself.
        weapon.muzzle = muzzle;
        weapon.ownerRoot = owner;
        return weapon;
    }
}

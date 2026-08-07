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

    /// <summary>Who is holding the gun. Decides the whole profile.</summary>
    public enum Role
    {
        /// <summary>The player's tank (or the AI driving it). Carries the pods.</summary>
        Hero,
        /// <summary>A recruit that joined off a beacon. Real help, never the star.</summary>
        Ally,
        /// <summary>One of the army.</summary>
        Raider,
        /// <summary>An enemy outpost's gun. Slow, heavy, and it hurts.</summary>
        Outpost,
    }

    /// <summary>
    /// THE HERO IS NOT A RAIDER WITH MORE SHIELD. It is one tank against an army,
    /// and the arithmetic has to say so: its cannon one-shots a walker and
    /// two-shots a raider tank, while a raider needs the best part of a minute
    /// alone to get through the hero. An army does it in seconds; one raider
    /// never does. That gap is the mode.
    ///
    /// Public because <see cref="TankBoons"/> recomputes the gun from these
    /// rather than multiplying the live value — an upgrade re-applied on every
    /// respawn must not compound.
    /// </summary>
    public const float HeroCannonDamage = 60f;
    public const float HeroCannonRate = 3.4f;

    /// <summary>
    /// What a pod's gun is multiplied by.
    ///
    /// The twelve come from the arena's arsenal, where they are balanced against
    /// hundred-shield robots and a blaster that chatters. Dropped in beside a
    /// sixty-damage cannon they would every one of them be a DOWNGRADE, and a
    /// prize that makes you weaker is worse than no prize. Scaled rather than
    /// re-tuned one at a time: the twelve differ wildly in cadence and in what a
    /// "shot" even is (pellets, streams, splashes), and a factor keeps whatever
    /// balance they already had against each other.
    /// </summary>
    const float PodDamageScale = 2.5f;

    /// <summary>
    /// Attach the rack and hand it back in canonical order.
    ///
    /// Only <see cref="Role.Hero"/> gets the pods. Everyone else gets the main
    /// gun and nothing else: pods are a reward the player collects, so nobody
    /// else's other eleven guns could ever be reached — and eleven unreachable
    /// <see cref="Weapon"/> components on every one of a dozen live raiders is a
    /// hundred and thirty MonoBehaviours a frame doing nothing at all.
    /// </summary>
    public static Weapon[] Attach(GameObject host, Transform muzzle, Transform owner,
        TankPawn.Chassis kind, Role role, Color boltColor)
    {
        // Slot 0 for everyone: the main gun. Slower and heavier than the FPS
        // blaster — a tank's cannon should land, not chatter.
        var cannon = Cannon(host, muzzle, owner, kind, role, boltColor);
        if (role != Role.Hero)
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
    /// a cannon's punch. Travel time is kept on purpose — leading a moving target
    /// is most of the skill here — but the hero's shell is the faster of the two,
    /// so the player is the one who gets to hit a tank that is trying to dodge.
    /// </summary>
    static LaserBlaster Cannon(GameObject host, Transform muzzle, Transform owner,
        TankPawn.Chassis kind, Role role, Color boltColor)
    {
        var gun = Add<LaserBlaster>(host, muzzle, owner);
        bool tank = kind == TankPawn.Chassis.Tank;
        gun.weaponName = kind == TankPawn.Chassis.Structure ? "Emplacement"
            : tank ? "Cannon" : "Rifle";
        // Coloured by TEAM rather than by weapon, unlike the arena's guns. Down a
        // scrolling field with a dozen tanks firing at once, "whose shot is that"
        // has to be answerable at a glance, and hue is the only channel left.
        gun.color = boltColor;

        switch (role)
        {
            case Role.Hero:
                gun.shotsPerSecond = HeroCannonRate;
                gun.damage = HeroCannonDamage;
                gun.boltSpeed = 70f;
                break;

            // Real help, never the star: a recruit hits half as hard as the hero
            // and takes half again as long between shots. Four of them are worth
            // having and still do not make the player a spectator at their own
            // escort's battle.
            case Role.Ally:
                gun.shotsPerSecond = 2.2f;
                gun.damage = 30f;
                gun.boltSpeed = 62f;
                break;

            // An outpost's gun is the one enemy weapon that genuinely threatens
            // the hero on its own — slow enough to drive out of, heavy enough
            // that sitting in front of it is a decision rather than an oversight.
            case Role.Outpost:
                gun.shotsPerSecond = 0.85f;
                gun.damage = 42f;
                gun.boltSpeed = 40f;
                break;

            default:
                gun.shotsPerSecond = tank ? 1.5f : 2.2f;
                gun.damage = tank ? 13f : 7f;
                gun.boltSpeed = 46f;
                break;
        }
        return gun;
    }

    /// <summary>
    /// Scale the pod guns up — see <see cref="PodDamageScale"/>.
    ///
    /// AFTER ACTIVATION, and it has to be: most of the twelve set their own
    /// damage in Awake, which does not run until the pawn is switched on (see
    /// TankPawn.Spawn), so anything written here beforehand is overwritten by
    /// the weapon itself. Slot 0 is skipped — the cannon's numbers are the ones
    /// everything else is scaled against.
    /// </summary>
    public static void AmplifyPods(Weapon[] rack)
    {
        if (rack == null)
            return;
        for (int i = 1; i < rack.Length; i++)
            if (rack[i] != null)
                rack[i].damage *= PodDamageScale;
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

using System.Collections.Generic;

/// <summary>
/// The frame-data table: every attack's damage, reach and timing lives here
/// and nowhere else, so tuning the game is editing one file. Times are
/// seconds (this is a Unity Update loop, not an arcade board).
///
/// Gameplay reads these windows; animation is stretched to fit them (the
/// forge bakes template clips to exactly Duration, and scales any adopted
/// Meshy clip's state speed to match), so swapping clips never changes feel.
/// </summary>
public static class BrawlMoveSet
{
    public enum Move { Punch, Kick, FlyKick, Blast }

    public struct Data
    {
        public Move move;
        public int damage;
        /// <summary>Reach along the lane, from the attacker's centre, metres.</summary>
        public float range;
        public float startup;
        public float active;
        public float recover;

        public float Duration => startup + active + recover;
    }

    public static readonly Dictionary<Move, Data> Table = new Dictionary<Move, Data>
    {
        // Ranges are the DECISION reach (what the AI closes to and the
        // outer gate); actual contact needs the striking limb inside the
        // defender's body column — see BrawlFighter.TryHit. Tuned to what
        // an extended arm/leg visually covers from the 0.9 m separation.
        // Actives sized so the animation's contact moment sits mid-window
        // (the analyzer aligns Meshy trims to the same fractions — see
        // PEAK_FRACTION in meshyfight.py; drift there cost every hit).
        [Move.Punch] = new Data
        {
            move = Move.Punch,
            damage = 8,
            range = 1.2f,
            startup = 0.12f,
            active = 0.14f,
            recover = 0.20f,
        },
        [Move.Kick] = new Data
        {
            move = Move.Kick,
            damage = 12,
            range = 1.45f,
            startup = 0.20f,
            active = 0.16f,
            recover = 0.28f,
        },
        // FlyKick's active window really ends at landing; `active` here is
        // the cap so a full-height arc can't stay hot absurdly long.
        [Move.FlyKick] = new Data
        {
            move = Move.FlyKick,
            damage = 16,
            range = 1.5f,
            startup = 0.10f,
            active = 0.80f,
            recover = 0.25f,
        },
        // The PHOTON BLAST: damage rides the projectile, these windows are
        // the cast. Range is handled by the bolt itself.
        [Move.Blast] = new Data
        {
            move = Move.Blast,
            damage = 20,
            range = 0f,
            startup = 0.30f,
            active = 0.10f,
            recover = 0.40f,
        },
    };

    // The non-attack timings, one authoritative home each.
    public const float MaxHealth = 100f;
    public const float RoundSeconds = 60f;
    public const float HitStun = 0.25f;
    public const float HitKnockback = 0.8f;
    /// <summary>Fall + floor beat; the rise starts when this much is left…</summary>
    public const float KnockdownTime = 0.90f;
    /// <summary>…and takes this long. Down for 1.7 s total, invulnerable throughout.</summary>
    public const float GetUpTime = 0.80f;
    public const float WalkSpeed = 3.0f;
    public const float JumpVelocity = 7.5f;
    public const float Gravity = 22f;
    public const float MinSeparation = 0.9f;

    /// <summary>
    /// Half-width of the body column a strike must reach into — the
    /// "hurtbox". A robot is ~0.9 m wide at the shoulders after height
    /// normalization; F3 in a Brawl mode draws it.
    /// </summary>
    public const float BodyHalfWidth = 0.45f;
    public const float BodyHeight = 1.85f;

    /// <summary>
    /// The strike's own volume around its bone: the wrist joint sits ~this
    /// far behind the fist's surface, so contact is bone-to-column plus
    /// this pad — without it, a glove visibly touching the chest can still
    /// read as a miss.
    /// </summary>
    public const float StrikeRadius = 0.18f;

    public enum Limb { RightHand, LeftHand, RightForeArm, RightFoot, LeftFoot }

    /// <summary>
    /// One face of a move family. J and K each fire a RANDOM variant of
    /// their family — many martial moves on one kid-sized button — while
    /// every variant shares the family's frame data, so variety costs no
    /// balance. `meshyKey` names the Fight/&lt;robot&gt;-&lt;key&gt;.glb whose
    /// capture replaces the template when it exists.
    /// </summary>
    public struct Variant
    {
        public string trigger;    // animator state + trigger
        public string display;    // captions and the F3 log
        public string meshyKey;   // Meshy clip / trim key
        public Limb limb;         // which bone must reach the body

        public Variant(string trigger, string display, string meshyKey, Limb limb)
        {
            this.trigger = trigger;
            this.display = display;
            this.meshyKey = meshyKey;
            this.limb = limb;
        }
    }

    public static readonly Variant[] PunchVariants =
    {
        new Variant("Punch", "KUNG FU PUNCH", "punch", Limb.RightHand),
        new Variant("PunchJab", "JAB", "jab", Limb.RightHand),
        new Variant("PunchHook", "HOOK", "hook", Limb.LeftHand),
        new Variant("PunchUppercut", "UPPERCUT", "uppercut", Limb.RightHand),
        new Variant("PunchElbow", "ELBOW STRIKE", "elbow", Limb.RightForeArm),
    };

    public static readonly Variant[] KickVariants =
    {
        new Variant("Kick", "ROUNDHOUSE KICK", "kick", Limb.RightFoot),
        new Variant("KickHigh", "HIGH KICK", "highkick", Limb.RightFoot),
        new Variant("KickSide", "SIDE KICK", "sidekick", Limb.RightFoot),
        new Variant("KickLow", "LOW SWEEP", "lowkick", Limb.RightFoot),
        new Variant("KickSpin", "SPIN KICK", "spinkick", Limb.RightFoot),
    };

    public static readonly Variant FlyKickVariant =
        new Variant("FlyKick", "FLYING KICK", "flykick", Limb.RightFoot);

    public static readonly Variant BlastVariant =
        new Variant("Blast", "PHOTON BLAST", "blast", Limb.RightHand);

    public static Variant[] VariantsOf(Move family)
    {
        switch (family)
        {
            case Move.Punch: return PunchVariants;
            case Move.Kick: return KickVariants;
            default: return null;
        }
    }

    // Template clip lengths for the states gameplay doesn't time-box.
    public const float HitClipTime = 0.30f;
    public const float KnockdownClipTime = 0.90f;
    public const float VictoryClipTime = 1.20f;
    public const float BlockClipTime = 0.30f;
}

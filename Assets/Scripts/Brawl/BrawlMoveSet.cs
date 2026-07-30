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
        [Move.Punch] = new Data
        {
            move = Move.Punch,
            damage = 8,
            range = 1.6f,
            startup = 0.12f,
            active = 0.10f,
            recover = 0.20f,
        },
        [Move.Kick] = new Data
        {
            move = Move.Kick,
            damage = 12,
            range = 2.0f,
            startup = 0.20f,
            active = 0.12f,
            recover = 0.28f,
        },
        // FlyKick's active window really ends at landing; `active` here is
        // the cap so a full-height arc can't stay hot absurdly long.
        [Move.FlyKick] = new Data
        {
            move = Move.FlyKick,
            damage = 16,
            range = 1.8f,
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
    public const float KnockdownTime = 0.70f;
    public const float GetUpTime = 0.45f;
    public const float WalkSpeed = 3.0f;
    public const float JumpVelocity = 7.5f;
    public const float Gravity = 22f;
    public const float MinSeparation = 0.9f;

    // Template clip lengths for the states gameplay doesn't time-box.
    public const float HitClipTime = 0.30f;
    public const float KnockdownClipTime = 0.70f;
    public const float VictoryClipTime = 1.20f;
    public const float BlockClipTime = 0.30f;
}

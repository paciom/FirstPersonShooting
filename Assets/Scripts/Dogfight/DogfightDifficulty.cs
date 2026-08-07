using UnityEngine;

/// <summary>
/// The five CPU levels for DOGFIGHT's player card, and the memory of which
/// one is picked. A level is a row of dials in three families:
///
///  - ENEMY PILOTING (think cadence, aim smear, burner allowance, whether a
///    missile break or a flare hand happens at all). Reaction delay and
///    predictability are what human difficulty actually feels like — a
///    ROOKIE flies near-cruise in gentle lines, which is a target a
///    first-time player can actually track.
///  - HULLS. The enemy team's shields thin out below CONTENDER and the
///    player's team thickens, so at ROOKIE a short burst kills and a
///    mistake doesn't.
///  - PLAYER ORDNANCE. The missile lock widens and quickens and the tube
///    reloads faster at the low end; ACE tightens all three past today's
///    numbers.
///
/// CONTENDER is EXACTLY the tuning the mode shipped with — the war card
/// (AI v AI) always flies it, and every dial here is expressed relative to
/// it. Applied only where the mode passes it in; a brain nobody configured
/// behaves like CONTENDER.
/// </summary>
public static class DogfightDifficulty
{
    public struct Level
    {
        public string name;
        /// <summary>Enemy decision cadence bounds — the reaction delay.</summary>
        public float thinkMin, thinkMax;
        /// <summary>Enemy aim smear bounds, degrees.</summary>
        public float jitterMin, jitterMax;
        /// <summary>How much burner the enemy may use (1 = full boost).
        /// Low caps mean slow, trackable targets that missiles always catch.</summary>
        public float throttleCap;
        /// <summary>Chance an inbound missile gets the defensive break.</summary>
        public float evadeChance;
        /// <summary>Chance the flare hand moves at all for a given threat.</summary>
        public float flareChance;
        /// <summary>Seconds between enemy missile shots.</summary>
        public float missileEveryMin, missileEveryMax;
        /// <summary>Shield scale on the enemy team / the player's team.</summary>
        public float enemyShield, allyShield;
        /// <summary>The player's missile lock: cone (degrees) and hold time.</summary>
        public float lockCone, lockSeconds;
        /// <summary>Scale on the player's missile reload.</summary>
        public float playerReload;
    }

    public static readonly Level[] Levels =
    {
        new Level { name = "ROOKIE",
                    thinkMin = 0.55f, thinkMax = 0.80f, jitterMin = 7f, jitterMax = 11f,
                    throttleCap = 0.15f, evadeChance = 0.20f, flareChance = 0.05f,
                    missileEveryMin = 18f, missileEveryMax = 26f,
                    enemyShield = 0.55f, allyShield = 1.7f,
                    lockCone = 26f, lockSeconds = 0.25f, playerReload = 0.55f },
        new Level { name = "CADET",
                    thinkMin = 0.38f, thinkMax = 0.55f, jitterMin = 4.5f, jitterMax = 7f,
                    throttleCap = 0.45f, evadeChance = 0.50f, flareChance = 0.35f,
                    missileEveryMin = 12f, missileEveryMax = 17f,
                    enemyShield = 0.75f, allyShield = 1.3f,
                    lockCone = 20f, lockSeconds = 0.35f, playerReload = 0.75f },
        new Level { name = "CONTENDER",
                    thinkMin = 0.22f, thinkMax = 0.34f, jitterMin = 2f, jitterMax = 3.2f,
                    throttleCap = 1f, evadeChance = 1f, flareChance = 1f,
                    missileEveryMin = 6f, missileEveryMax = 9f,
                    enemyShield = 1f, allyShield = 1f,
                    lockCone = 16f, lockSeconds = 0.5f, playerReload = 1f },
        new Level { name = "CHAMPION",
                    thinkMin = 0.17f, thinkMax = 0.26f, jitterMin = 1.4f, jitterMax = 2.2f,
                    throttleCap = 1f, evadeChance = 1f, flareChance = 1f,
                    missileEveryMin = 4.5f, missileEveryMax = 7f,
                    enemyShield = 1.15f, allyShield = 0.9f,
                    lockCone = 13f, lockSeconds = 0.6f, playerReload = 1f },
        new Level { name = "ACE",
                    thinkMin = 0.12f, thinkMax = 0.20f, jitterMin = 0.8f, jitterMax = 1.4f,
                    throttleCap = 1f, evadeChance = 1f, flareChance = 1f,
                    missileEveryMin = 3.5f, missileEveryMax = 5.5f,
                    enemyShield = 1.35f, allyShield = 0.8f,
                    lockCone = 11f, lockSeconds = 0.7f, playerReload = 1.15f },
    };

    /// <summary>The war card's fixed level, and the dials every unconfigured
    /// brain flies: the shipped tuning.</summary>
    public const int Baseline = 3;

    const string PrefsKey = "PhotonArena.DogfightLevel";

    /// <summary>The remembered pick (1-based). First-timers open on CADET —
    /// beatable while learning the stick, one notch up from the floor.</summary>
    public static int Chosen
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, 2), 1, Levels.Length);
        set => PlayerPrefs.SetInt(PrefsKey, Mathf.Clamp(value, 1, Levels.Length));
    }

    public static Level Get(int level)
    {
        return Levels[Mathf.Clamp(level, 1, Levels.Length) - 1];
    }

    public static string NameOf(int level) => Get(level).name;
}

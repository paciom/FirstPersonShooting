using UnityEngine;

/// <summary>
/// The five CPU levels and the memory of which one each Brawl mode uses.
/// A level is a row of dials for BrawlBrain — reaction cadence first among
/// them, because reaction delay is what human difficulty actually feels
/// like. LEGEND's guard is deliberately 0.85, not 1.0: a CPU that blocks
/// everything is a staring contest, not a boss.
/// </summary>
public static class BrawlDifficulty
{
    public struct Level
    {
        public string name;
        /// <summary>Decision cadence bounds — the reaction delay.</summary>
        public float thinkMin, thinkMax;
        /// <summary>Chance to guard on seeing a swing.</summary>
        public float guardChance;
        public float aggression;
        /// <summary>Attack-range judgment: above 1 swings from too far (whiffs).</summary>
        public float spacingError;
        /// <summary>Remembers the PHOTON BLAST meter exists.</summary>
        public float blastChance;
        /// <summary>Presses openings — the foe's hit-stun and whiff recovery.</summary>
        public float punishChance;
    }

    public static readonly Level[] Levels =
    {
        new Level { name = "ROOKIE", thinkMin = 0.35f, thinkMax = 0.55f, guardChance = 0.10f,
                    aggression = 0.45f, spacingError = 1.25f, blastChance = 0.10f, punishChance = 0.05f },
        new Level { name = "CADET", thinkMin = 0.28f, thinkMax = 0.42f, guardChance = 0.25f,
                    aggression = 0.55f, spacingError = 1.10f, blastChance = 0.25f, punishChance = 0.15f },
        new Level { name = "CONTENDER", thinkMin = 0.20f, thinkMax = 0.32f, guardChance = 0.45f,
                    aggression = 0.65f, spacingError = 1.00f, blastChance = 0.45f, punishChance = 0.35f },
        new Level { name = "CHAMPION", thinkMin = 0.14f, thinkMax = 0.24f, guardChance = 0.65f,
                    aggression = 0.75f, spacingError = 0.92f, blastChance = 0.65f, punishChance = 0.60f },
        new Level { name = "LEGEND", thinkMin = 0.10f, thinkMax = 0.16f, guardChance = 0.85f,
                    aggression = 0.85f, spacingError = 0.85f, blastChance = 0.85f, punishChance = 0.85f },
    };

    /// <summary>
    /// The remembered pick for a mode (1-based). Player v AI opens on CADET;
    /// the AI war opens on CONTENDER — the watchable middle.
    /// </summary>
    public static int For(GameMode mode)
    {
        int fallback = mode == GameMode.BrawlWar ? 3 : 2;
        return Mathf.Clamp(PlayerPrefs.GetInt(Key(mode), fallback), 1, Levels.Length);
    }

    public static void Set(GameMode mode, int level)
    {
        PlayerPrefs.SetInt(Key(mode), Mathf.Clamp(level, 1, Levels.Length));
    }

    public static Level Get(int level)
    {
        return Levels[Mathf.Clamp(level, 1, Levels.Length) - 1];
    }

    public static string NameOf(int level) => Get(level).name;

    static string Key(GameMode mode)
    {
        return mode == GameMode.BrawlWar ? "PhotonArena.BrawlWarLevel" : "PhotonArena.BrawlLevel";
    }
}

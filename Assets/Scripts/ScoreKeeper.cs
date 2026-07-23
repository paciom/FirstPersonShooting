using System;

/// <summary>
/// Minimal score tracking for the greybox milestone: one point per de-rez the
/// player causes. Replaced by TeamManager match scoring in Phase 2.
/// </summary>
public static class ScoreKeeper
{
    public static int Score { get; private set; }
    public static event Action<int> OnScoreChanged;

    public static void AddPoint()
    {
        Score++;
        OnScoreChanged?.Invoke(Score);
    }

    public static void Reset()
    {
        Score = 0;
        OnScoreChanged?.Invoke(Score);
    }
}

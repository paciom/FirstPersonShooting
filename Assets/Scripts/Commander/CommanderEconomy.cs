using System;

/// <summary>
/// Credits per team — the Commander economy's one ledger. Static like
/// TeamBank and ScoreKeeper: one session, two teams, no instancing.
///
/// Deliberately separate from TeamBank: gold bars are an FPS-match currency
/// that buys reinforcements on a fixed price, and its Reset is wired into
/// every FPS mode change. Commander credits flow constantly (harvest in,
/// production out) and live and die with the Commander session instead.
///
/// A recompile during Play zeroes this (statics do not survive the reload);
/// the session's economy restarts at zero until the next Reset. Accepted:
/// it is a dev-editor-only wound, and a WebGL build never recompiles.
/// </summary>
public static class CommanderEconomy
{
    public const int Teams = 2;

    /// <summary>Enough to open with a Power Plant + Refinery and change.</summary>
    // 8000, up from 3000 by playtest decree: enough to open with power,
    // refinery AND factory on day one and still buy robots — the opening
    // act should be about building fast, not waiting on the first hauls.
    public const int StartingCredits = 8000;

    static readonly int[] _credits = new int[Teams];

    /// <summary>(teamId, newTotal)</summary>
    public static event Action<int, int> OnCreditsChanged;

    public static int Credits(int teamId) =>
        teamId >= 0 && teamId < Teams ? _credits[teamId] : 0;

    public static void Grant(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= Teams || amount <= 0)
            return;
        _credits[teamId] += amount;
        OnCreditsChanged?.Invoke(teamId, _credits[teamId]);
    }

    /// <summary>All or nothing — a build either pays in full or doesn't start.</summary>
    public static bool Spend(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= Teams || amount < 0 || _credits[teamId] < amount)
            return false;
        _credits[teamId] -= amount;
        OnCreditsChanged?.Invoke(teamId, _credits[teamId]);
        return true;
    }

    /// <summary>Back to the opening stake. Called on every session start.</summary>
    public static void Reset()
    {
        for (int team = 0; team < Teams; team++)
        {
            _credits[team] = StartingCredits;
            OnCreditsChanged?.Invoke(team, _credits[team]);
        }
    }
}

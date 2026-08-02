using System;

/// <summary>
/// The defender's wallet — one seat, one number. Deliberately NOT
/// CommanderEconomy: that ledger is two-team, opens with an RTS bankroll,
/// and its Reset is wired into Commander sessions. Tower Defense income
/// (bounties, wave bonuses, refinery ticks) lives and dies with a TD
/// session instead, and sharing a static ledger between modes is how one
/// mode's leftovers become another mode's opening exploit.
///
/// A recompile during Play zeroes this (statics do not survive the
/// reload) — same accepted dev-only wound as CommanderEconomy.
/// </summary>
public static class TDEconomy
{
    /// <summary>Two Pulse Turrets and change — the classic TD opening hand.</summary>
    public const int StartingCredits = 260;

    static int _credits;

    /// <summary>Fired with the new total on every change.</summary>
    public static event Action<int> OnCreditsChanged;

    public static int Credits => _credits;

    public static void Grant(int amount)
    {
        if (amount <= 0)
            return;
        _credits += amount;
        OnCreditsChanged?.Invoke(_credits);
    }

    /// <summary>All or nothing — a tower either pays in full or doesn't rise.</summary>
    public static bool Spend(int amount)
    {
        if (amount < 0 || _credits < amount)
            return false;
        _credits -= amount;
        OnCreditsChanged?.Invoke(_credits);
        return true;
    }

    /// <summary>Back to the opening stake. Called on every session start.</summary>
    public static void Reset()
    {
        _credits = StartingCredits;
        OnCreditsChanged?.Invoke(_credits);
    }
}

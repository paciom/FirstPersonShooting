using UnityEngine;

/// <summary>
/// The ammunition ledger: firing costs money. Cheap on purpose — fractions
/// of a credit per shot, a rounding error against a 300-credit robot — but
/// never free, so a long firefight shows up on the books and a broke army
/// feels it. Costs accrue as fractions per second of trigger time and are
/// billed to CommanderEconomy a whole credit at a time.
///
/// A team that hits zero keeps shooting (the fraction jar just empties):
/// this is pressure, not a soft-lock, in a game for eight-year-olds.
/// </summary>
public static class CommanderAmmo
{
    static readonly float[] _accrued = new float[CommanderEconomy.Teams];

    /// <summary>
    /// Credits per second of held trigger, by weapon family. Laser works out
    /// to ~0.03 a bolt at the standard 4/s; the heavier the shot, the dearer.
    /// </summary>
    public static float RatePerSecond(Weapon weapon)
    {
        switch (weapon)
        {
            case PlasmaLobber _: return 0.10f;
            case RailZapper _: return 0.08f;
            case PhotonBeam _: return 0.10f;
            default: return 0.12f;   // LaserBlaster and anything unlisted
        }
    }

    /// <summary>Bill <paramref name="seconds"/> of trigger time on this weapon.</summary>
    public static void AccrueFiring(int teamId, Weapon weapon, float seconds)
    {
        if (teamId < 0 || teamId >= _accrued.Length || weapon == null || seconds <= 0f)
            return;
        _accrued[teamId] += RatePerSecond(weapon) * seconds;
        while (_accrued[teamId] >= 1f)
        {
            if (!CommanderEconomy.Spend(teamId, 1))
            {
                _accrued[teamId] = 0f;   // broke: the jar empties, the guns keep talking
                return;
            }
            _accrued[teamId] -= 1f;
        }
    }

    public static void Reset()
    {
        for (int team = 0; team < _accrued.Length; team++)
            _accrued[team] = 0f;
    }
}

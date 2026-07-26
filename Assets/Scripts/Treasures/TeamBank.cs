using System;

/// <summary>
/// Gold bars a team has banked from airdrops. Reaching <see cref="RobotCost"/>
/// buys a reinforcement robot, which is what makes gold worth fighting over:
/// grabbing enough of it literally puts another fighter on your side.
///
/// Static like ScoreKeeper — one match, two teams, no instancing needed.
/// Spending is driven from <see cref="TreasureSpawner"/>'s tick rather than
/// straight out of <see cref="Add"/> so a team that can't build right now
/// (roster full, everyone mid-de-rez) keeps its gold and buys a moment later.
/// </summary>
public static class TeamBank
{
    public const int RobotCost = 100;
    public const int Teams = 2;

    static readonly int[] _gold = new int[Teams];

    /// <summary>(teamId, newTotal)</summary>
    public static event Action<int, int> OnGoldChanged;

    public static int Gold(int teamId) =>
        teamId >= 0 && teamId < Teams ? _gold[teamId] : 0;

    public static void Add(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= Teams || amount == 0)
            return;
        _gold[teamId] += amount;
        OnGoldChanged?.Invoke(teamId, _gold[teamId]);
    }

    /// <summary>
    /// Spend down to zero-or-remainder for every team that can afford and
    /// place a robot right now. Called on a slow tick by the spawner.
    /// </summary>
    public static void TryBuildReinforcements()
    {
        for (int team = 0; team < Teams; team++)
        {
            while (_gold[team] >= RobotCost && RobotReinforcements.TrySpawn(team))
            {
                _gold[team] -= RobotCost;
                OnGoldChanged?.Invoke(team, _gold[team]);
            }
        }
    }

    public static void Reset()
    {
        for (int team = 0; team < Teams; team++)
        {
            _gold[team] = 0;
            OnGoldChanged?.Invoke(team, 0);
        }
    }
}

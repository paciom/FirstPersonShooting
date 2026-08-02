using UnityEngine;

/// <summary>
/// The power grid, computed rather than kept: supply and draw are summed from
/// the live building registry on every ask. With a dozen buildings a side
/// there is nothing worth caching, and a derived answer cannot drift — not
/// out of sync with a building that just collapsed, and not wiped by the
/// recompile-during-Play reload the way ledger statics are.
///
/// Brown-outs are soft, Red Alert style: when draw exceeds supply, everything
/// powered runs at supply/draw speed — production crawls and turrets fire
/// slow, but nothing switches off outright.
/// </summary>
public static class CommanderPower
{
    public static int Supply(int teamId)
    {
        int total = 0;
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null && building.Definition.power > 0)
                total += building.Definition.power;
        return total;
    }

    public static int Draw(int teamId)
    {
        int total = 0;
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null && building.Definition.power < 0)
                total -= building.Definition.power;
        return total;
    }

    /// <summary>1 at healthy power, supply/draw under a brown-out, floored at 0.25 so nothing ever fully stalls.</summary>
    public static float Efficiency(int teamId)
    {
        int draw = Draw(teamId);
        if (draw <= 0)
            return 1f;
        int supply = Supply(teamId);
        if (supply >= draw)
            return 1f;
        return Mathf.Max(0.25f, supply / (float)draw);
    }
}

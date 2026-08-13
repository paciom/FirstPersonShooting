using UnityEngine;

/// <summary>
/// Air-power bookkeeping, derived like CommanderPower rather than kept:
/// each standing AIRBASE grants a number of flight slots, and a robot may
/// fold into its jet only while its team has a slot free. Derived counts
/// cannot drift and cannot be wiped by a mid-play recompile.
/// </summary>
public static class CommanderAir
{
    /// <summary>Flight slots one airbase sustains at a time.</summary>
    public const int JetsPerAirbase = 4;

    public static int Capacity(int teamId)
    {
        int airbases = 0;
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null
                && building.Definition.key == BuildingCatalog.Airbase)
                airbases++;
        return airbases * JetsPerAirbase;
    }

    public static int InFlight(int teamId)
    {
        int flying = 0;
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == teamId && unit.IsAlive && unit.IsAirborne)
                flying++;
        return flying;
    }

    /// <summary>Whether one more robot may take off right now.</summary>
    public static bool CanLaunch(int teamId) => InFlight(teamId) < Capacity(teamId);
}

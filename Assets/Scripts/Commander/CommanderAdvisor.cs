using UnityEngine;

/// <summary>
/// The "build THIS now" voice on the player's shoulder. One rule chain,
/// walked top to bottom, mirroring the priorities CommanderAI actually plays
/// by — so the recommendation is not a hint system's opinion, it is what a
/// competent commander at this board would do next. Derived fresh on every
/// ask, from the same registries everything else reads.
/// </summary>
public static class CommanderAdvisor
{
    public struct Advice
    {
        /// <summary>True: key is a UnitCatalog key. False: BuildingCatalog.</summary>
        public bool isUnit;
        public string key;
        /// <summary>One kid-readable sentence of WHY.</summary>
        public string reason;
    }

    public static Advice Recommend(int teamId)
    {
        int collectors = 0, fighters = 0;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId != teamId || !unit.IsAlive)
                continue;
            if (unit is CommanderCollector) collectors++;
            else fighters++;
        }

        if (Count(teamId, BuildingCatalog.PowerPlant) == 0)
            return Building(BuildingCatalog.PowerPlant, "everything runs on power");
        if (CommanderPower.Efficiency(teamId) < 1f)
            return Building(BuildingCatalog.PowerPlant,
                "your grid is browning out — guns and factories are slowed");
        if (Count(teamId, BuildingCatalog.Refinery) == 0)
            return Building(BuildingCatalog.Refinery, "unlocks heavy industry");
        if (Count(teamId, BuildingCatalog.Factory) == 0)
            return Building(BuildingCatalog.Factory, "you cannot build robots without one");
        if (collectors < 2)
            return Unit(UnitCatalog.Collector, "haulers feed the whole war");
        if (Count(teamId, BuildingCatalog.Turret) < 2)
            return Building(BuildingCatalog.Turret, "the base cannot defend itself yet");
        if (EnemyHasAirbase(teamId) && Count(teamId, BuildingCatalog.Missiles) < 2)
            return Building(BuildingCatalog.Missiles,
                "the enemy has an AIRBASE — answer their jets");
        if (Count(teamId, BuildingCatalog.TechLab) == 0)
            return Building(BuildingCatalog.TechLab, "unlocks the TITAN and the AIRBASE");
        if (fighters < 10)
            return Unit(UnitCatalog.Ranger, "grow the army before the next wave lands");
        if (Count(teamId, BuildingCatalog.Airbase) == 0)
            return Building(BuildingCatalog.Airbase,
                "jet capability — cross the map at twice tank speed");
        return Unit(UnitCatalog.Titan, "press the advantage with heavy armour");
    }

    static Advice Building(string key, string reason) =>
        new Advice { isUnit = false, key = key, reason = reason };

    static Advice Unit(string key, string reason) =>
        new Advice { isUnit = true, key = key, reason = reason };

    static int Count(int teamId, string buildingKey)
    {
        int count = 0;
        foreach (var building in global::Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive
                && building.Definition != null && building.Definition.key == buildingKey)
                count++;
        return count;
    }

    static bool EnemyHasAirbase(int teamId)
    {
        foreach (var building in global::Building.All)
            if (building != null && building.TeamId != teamId && building.IsAlive
                && building.Definition != null
                && building.Definition.key == BuildingCatalog.Airbase)
                return true;
        return false;
    }
}

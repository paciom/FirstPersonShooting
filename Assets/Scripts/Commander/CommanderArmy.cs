using UnityEngine;

/// <summary>
/// Spawns and sweeps up the armies. In Phase 1 that is a fixed skirmish force
/// per team so there is something to command; factories take over the spawning
/// in Phase 4 and this keeps only the sweep.
/// </summary>
public static class CommanderArmy
{
    const int SkirmishSize = 12;

    /// <summary>
    /// Two ranks of six in front of each base pad, facing the enemy. Spawn
    /// points sit on open ground between the pad and the ridge line, clear of
    /// the flanking crystal fields.
    /// </summary>
    public static void SpawnSkirmish(RobotRoster roster)
    {
        GameObject model = null, vehicle = null;
        GameObject[] stages = null;
        if (roster != null && roster.HasRobots)
        {
            var entry = roster.Get(0);
            model = entry.modelPrefab;
            vehicle = entry.vehiclePrefab;
            stages = entry.transformStages;
        }

        for (int team = 0; team < 2; team++)
        {
            Vector3 site = CommanderMap.BaseSite(team);
            // Toward the map centre from the pad's front edge.
            float forward = team == 0 ? 1f : -1f;
            float yaw = team == 0 ? 0f : 180f;

            for (int i = 0; i < SkirmishSize; i++)
            {
                int column = i % 6;
                int rank = i / 6;
                var pos = site + new Vector3(
                    (column - 2.5f) * 2f,
                    0f,
                    forward * (11f + rank * 2.2f));
                CommanderUnit.Build<CommanderUnit>($"CmdUnit{team}_{i}", model, vehicle,
                    team, pos, yaw, armed: true, secondaryWeapon: "plasma",
                    transformStages: stages);
            }

            // Two collectors, offset east and west so each picks the safe
            // field on its own side rather than both queueing on one.
            for (int side = 0; side < 2; side++)
            {
                var pos = site + new Vector3(side == 0 ? -7f : 7f, 0f, forward * 8f);
                CommanderCollector.BuildCollector($"CmdCollector{team}_{side}",
                    model, team, pos, yaw);
            }
        }
    }

    /// <summary>
    /// Destroy every unit, immediately. Teardown re-bakes the arena NavMesh in
    /// the same frame, and a deferred destroy would leave live agents standing
    /// on a mesh that is being replaced under them. Scene sweep rather than
    /// CommanderUnit.All: a unit mid-death has already left the registry but
    /// still exists.
    /// </summary>
    public static void DespawnAll()
    {
        foreach (var unit in Object.FindObjectsByType<CommanderUnit>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(unit.gameObject);
        CommanderUnit.All.Clear();

        // In-flight bolts too, and just as immediately: teardown restores the
        // FPS cast later this same frame, and a surviving bolt's next Update
        // would raycast against robots that were never in this fight.
        foreach (var bolt in Object.FindObjectsByType<LaserBolt>(FindObjectsSortMode.None))
            Object.DestroyImmediate(bolt.gameObject);
    }
}

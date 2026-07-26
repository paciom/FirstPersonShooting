using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds extra robots mid-match when a team banks enough gold.
///
/// Rather than re-running the editor-time robot construction at runtime, a
/// healthy team-mate is cloned: Instantiate remaps every internal reference
/// (weapons → the clone's own components, DeRezEffect.body → the clone's body,
/// muzzle/ownerRoot → the clone's rig), so the new robot is wired exactly like
/// the ones ArenaBuilder made — including whichever roster model the team
/// picked on the select screen.
/// </summary>
public static class RobotReinforcements
{
    /// <summary>Ceiling per team, counting the three built into the scene.</summary>
    public const int MaxPerTeam = 6;

    static readonly List<GameObject> Spawned = new List<GameObject>();

    /// <summary>
    /// Clone a robot onto <paramref name="teamId"/>. Returns false — with no
    /// gold spent — when the roster is full or every team-mate is currently
    /// de-rezzed (a shrunken body would clone its shrunken scale as "normal").
    /// </summary>
    public static bool TrySpawn(int teamId)
    {
        AIBrain template = null;
        int roster = 0;

        foreach (var brain in Object.FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
        {
            var shield = brain.GetComponent<EnergyShield>();
            if (shield == null || shield.teamId != teamId)
                continue;
            roster++;
            if (template == null && !shield.IsDown)
                template = brain;
        }

        if (roster >= MaxPerTeam || template == null)
            return false;

        Vector3 spawn = PickSpawnPoint(teamId);
        var clone = Object.Instantiate(template.gameObject, spawn, template.transform.rotation);
        clone.name = $"{StripClone(template.name)}_Reinforcement{Spawned.Count + 1}";
        Spawned.Add(clone);

        // A NavMeshAgent placed by Instantiate hasn't attached to the mesh yet.
        var agent = clone.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
            agent.Warp(spawn);

        var cloneBrain = clone.GetComponent<AIBrain>();
        if (cloneBrain != null)
        {
            cloneBrain.SetActive(true);
            GameModeController.Instance?.RegisterBot(cloneBrain);
        }

        // Fresh robots start at full shield and with no inherited treasure gun.
        clone.GetComponent<EnergyShield>()?.Rematerialize();
        clone.GetComponent<WeaponLoadout>()?.ClearSpecial();

        Color tint = MatchAnnouncer.TeamColor(teamId);
        VfxUtil.Explosion(spawn + Vector3.up * 1f, tint, 1.6f);
        MatchAnnouncer.Say($"{MatchAnnouncer.TeamName(teamId)} BUILT A NEW ROBOT",
            $"{TeamBank.RobotCost} gold spent — that's {roster + 1} on the field", tint);

        // The old silhouette set doesn't include the newcomer's meshes.
        Object.FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include)?.InvalidateSilhouettes();
        return true;
    }

    /// <summary>
    /// Remove every bought robot. Called on match start and on returning to the
    /// menu so team sizes never leak from one match into the next.
    /// </summary>
    public static void DespawnAll()
    {
        foreach (var robot in Spawned)
        {
            if (robot == null)
                continue;
            var brain = robot.GetComponent<AIBrain>();
            if (brain != null)
                GameModeController.Instance?.UnregisterBot(brain);
            Object.Destroy(robot);
        }
        Spawned.Clear();
    }

    /// <summary>Team home strip, nudged onto the navmesh and away from the exact spawn line.</summary>
    static Vector3 PickSpawnPoint(int teamId)
    {
        Vector3 wanted = new Vector3(Random.Range(-9f, 9f), 0f, teamId == 0 ? -16f : 16f);
        if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            return hit.position;
        return wanted;
    }

    static string StripClone(string name) =>
        name.EndsWith("(Clone)") ? name.Substring(0, name.Length - "(Clone)".Length) : name;
}

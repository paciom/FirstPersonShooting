using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds extra robots mid-match when a team banks enough gold.
///
/// Rather than re-running the editor-time robot construction at runtime, a
/// healthy team-mate is cloned through <see cref="TeamRoster.Clone"/> — the
/// same path the select screen's team size builds its robots on, so a bought
/// robot and a tenth starter are wired identically. This class owns the rest:
/// who is eligible to be copied, where the gold puts them, and taking them off
/// the field again when the match ends.
/// </summary>
public static class RobotReinforcements
{
    /// <summary>
    /// Stamped into the name of every robot bought here, so TeamRoster can tell
    /// them apart from the scene's own permanent cast.
    /// </summary>
    public const string BoughtMarker = "_Reinforcement";

    static readonly List<GameObject> Spawned = new List<GameObject>();

    /// <summary>
    /// Clone a robot onto <paramref name="teamId"/>. Returns false — with no
    /// gold spent — when the roster is full or no team-mate is currently
    /// standing there as a robot.
    ///
    /// Only a robot in robot form will do. A clone inherits the template's body
    /// as it stands, so a de-rezzed one hands over its shrunken scale as
    /// "normal" and a transformed one hands over a tank: the vehicle models
    /// hanging off Body, the wheel rig, and a CharacterController already cut
    /// down to vehicle height, which TransformMode then measures as the height
    /// this robot stands at. Waiting is free — the gold stays banked and
    /// TeamBank tries again on its next tick.
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
            if (template == null && !shield.IsDown && IsStandingRobot(brain))
                template = brain;
        }

        if (roster >= TeamRoster.BotCapacity(teamId) || template == null)
            return false;

        Vector3 spawn = PickSpawnPoint(teamId);
        // Every trap Instantiate sets for a robot is handled in there, and the
        // robot-form template check above is what lands this clone on a form it
        // actually owns rather than a half-folded tank.
        var cloneBrain = TeamRoster.Clone(template, spawn, template.transform.rotation,
            $"{StripClone(template.name)}{BoughtMarker}{Spawned.Count + 1}");
        if (cloneBrain == null)
            return false;

        Spawned.Add(cloneBrain.gameObject);
        // Bought mid-match, so unlike a team the roster screen sized, this one
        // starts thinking the moment it lands.
        cloneBrain.SetActive(true);

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

    /// <summary>
    /// Robot form, and not on its way out of it — mid-fold is a half-built tank
    /// and clones as one.
    /// </summary>
    static bool IsStandingRobot(AIBrain brain)
    {
        var mode = brain.GetComponent<TransformMode>();
        return mode == null || (!mode.IsVehicle && !mode.IsBusy);
    }

    /// <summary>Team home strip, nudged onto the navmesh and away from the exact spawn line.</summary>
    static Vector3 PickSpawnPoint(int teamId)
    {
        Vector3 wanted = ArenaContext.ReinforcementSpawn(teamId);
        if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            return hit.position;
        return wanted;
    }

    static string StripClone(string name) =>
        name.EndsWith("(Clone)") ? name.Substring(0, name.Length - "(Clone)".Length) : name;
}

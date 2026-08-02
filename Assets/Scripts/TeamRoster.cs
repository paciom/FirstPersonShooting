using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sizes the two Gunfight teams to whatever the select screen asked for.
///
/// The scene is built with exactly three bots a side (ArenaBuilder.BuildBots),
/// which used to be the only matchup there was. A smaller team parks the ones
/// it does not need; a bigger one clones the ones it has, the same way a team
/// that banks enough gold buys a reinforcement — see <see cref="Clone"/>, which
/// both paths now share, because the list of things Instantiate gets wrong on a
/// robot is long and only worth getting right once.
///
/// Cloning rather than building from scratch is also what keeps the select
/// screen honest: a clone inherits the roster model, the paint and the vehicle
/// rig its team was just reskinned with, so robot ten looks like robot one.
/// </summary>
public static class TeamRoster
{
    /// <summary>
    /// Stamped into the name of every robot this class builds. A recompile
    /// during Play wipes the statics below but not the robots they were
    /// tracking, so the name is the only handle left to sweep them up by.
    /// </summary>
    const string ExtraMarker = "_Field";

    /// <summary>How far apart the rows of a deep formation stand.</summary>
    const float RowSpacing = 2.4f;

    /// <summary>Sideways shuffle per row, so a deep team is not a column.</summary>
    const float RowStagger = 1.4f;

    /// <summary>How far past its starting size gold may grow a team.</summary>
    const int ReinforcementGrowth = 2;

    /// <summary>Robots built for this match. Emptied back out between matches.</summary>
    static readonly List<GameObject> Extras = new List<GameObject>();

    /// <summary>The scene's own bots, by team — captured once, never rebuilt.</summary>
    static readonly List<AIBrain>[] SceneBots = { new List<AIBrain>(), new List<AIBrain>() };

    /// <summary>Bots each team actually started this match with.</summary>
    static readonly int[] Fielded = { 0, 0 };

    /// <summary>
    /// Stand <paramref name="perTeam"/> robots on each side.
    ///
    /// Call it AFTER the arena has loaded and BEFORE the brains are switched on:
    /// slots are read off the live arena's spawn lines, and a robot that starts
    /// thinking before it has been placed walks off toward the last arena.
    ///
    /// <paramref name="playerPlays"/> counts the player as one of the cyan
    /// robots — Player v AI at 1 v 1 is the player against one enemy, not the
    /// player and three friends against one.
    /// </summary>
    public static void Apply(int perTeam, bool playerPlays)
    {
        Discover();
        Clear();

        perTeam = Mathf.Clamp(perTeam, TeamSize.Min, TeamSize.Max);
        for (int team = 0; team <= 1; team++)
        {
            int bots = team == 0 && playerPlays ? perTeam - 1 : perTeam;
            Fielded[team] = bots;
            Field(team, bots, playerPlays);
        }

        // The old silhouette set knows nothing about the newcomers, and nothing
        // about the ones that just left the field either.
        Object.FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include)?.InvalidateSilhouettes();
    }

    /// <summary>
    /// Hand the scene back as it was found: every built robot destroyed, every
    /// parked one standing again. Runs on every mode entry and on returning to
    /// the menu, so a team size can never leak into the next match — or into
    /// the menu backdrop, which expects the full cast.
    /// </summary>
    public static void Reset()
    {
        Discover();
        Clear();

        foreach (var team in SceneBots)
            foreach (var brain in team)
                if (brain != null && !brain.gameObject.activeSelf)
                    brain.gameObject.SetActive(true);

        Fielded[0] = Fielded[1] = 0;
    }

    /// <summary>
    /// Ceiling on a team's bot count, for the gold that buys reinforcements:
    /// twice the size it started at. That is exactly the six this used to be
    /// fixed at, back when every match was three a side — and it is a rule
    /// rather than a number, so a 10 v 10 can still field an eleventh robot and
    /// a 1 v 1 cannot quietly become a 1 v 6.
    /// </summary>
    public static int BotCapacity(int teamId)
    {
        int team = Mathf.Clamp(teamId, 0, 1);
        return Mathf.Max(1, Fielded[team]) * ReinforcementGrowth;
    }

    /// <summary>
    /// Copy a robot onto the field, wired the way ArenaBuilder wires one.
    ///
    /// Instantiate remaps every internal reference (weapons → the clone's own
    /// components, DeRezEffect.body → the clone's body, muzzle/ownerRoot → the
    /// clone's rig), which is the whole reason robots are cloned rather than
    /// rebuilt. What it does NOT do is any of the four things below.
    ///
    /// The brain is left switched off: both callers switch it on themselves,
    /// and at their own moment.
    /// </summary>
    internal static AIBrain Clone(AIBrain template, Vector3 spawn, Quaternion rotation, string name)
    {
        var clone = Object.Instantiate(template.gameObject, spawn, rotation);
        clone.name = name;

        // A NavMeshAgent placed by Instantiate hasn't attached to the mesh yet.
        var agent = clone.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
            agent.Warp(spawn);

        // Fresh robots start at full shield and with no inherited treasure gun.
        clone.GetComponent<EnergyShield>()?.Rematerialize();
        clone.GetComponent<WeaponLoadout>()?.ClearSpecial();

        // Instantiate copies the models VehicleSkin and TransformMode built, but
        // none of the private fields tracking them, so the clone would otherwise
        // build a second set over the top of the inherited one and leave that
        // one hanging under its Body forever.
        clone.GetComponent<TransformMode>()?.ForceRobotForm();

        var brain = clone.GetComponent<AIBrain>();
        if (brain != null)
            GameModeController.Instance?.RegisterBot(brain);
        return brain;
    }

    // ---------- one team ----------

    static void Field(int teamId, int bots, bool playerPlays)
    {
        var anchors = ArenaContext.Current.TeamSpawns(teamId);
        float yaw = teamId == 0 ? 0f : 180f;
        var scene = SceneBots[teamId];

        // Cyan slot zero belongs to the player when they are playing; their own
        // spawn is PlayerSpawn, which is where ArenaRuntime already put them.
        int firstSlot = teamId == 0 && playerPlays ? 1 : 0;

        int fromScene = Mathf.Min(bots, scene.Count);
        for (int i = 0; i < scene.Count; i++)
        {
            var brain = scene[i];
            if (brain == null)
                continue;
            bool fielded = i < fromScene;
            if (brain.gameObject.activeSelf != fielded)
                brain.gameObject.SetActive(fielded);
            if (fielded)
                ArenaRuntime.PlaceCharacter(brain.gameObject, Slot(anchors, firstSlot + i, teamId), yaw);
        }

        // Nothing to copy from: a scene with no bots on this team at all. The
        // arena still runs, one side simply empty, which beats a null crash.
        if (fromScene == 0)
            return;

        var rotation = Quaternion.Euler(0f, yaw, 0f);
        for (int i = fromScene; i < bots; i++)
        {
            // Cycled rather than all copied from the first: the three scene bots
            // are a hoarder, a brawler and a middle-of-the-road one, and a team
            // of ten identical hoarders all run the same line to the same crate.
            var template = scene[i % fromScene];
            if (template == null)
                continue;

            Vector3 spot = Slot(anchors, firstSlot + i, teamId);
            var brain = Clone(template, spot, rotation, $"{template.name}{ExtraMarker}{i + 1}");
            if (brain != null)
                Extras.Add(brain.gameObject);
        }
    }

    /// <summary>
    /// Where the nth robot of a team stands. The arena gives three anchors a
    /// side; past that, rows stack toward the MIDDLE of the arena rather than
    /// behind the spawn line — the back wall is a couple of units behind it,
    /// and the middle is the one part of any arena guaranteed to be open floor.
    /// </summary>
    static Vector3 Slot(Vector3[] anchors, int index, int teamId)
    {
        if (anchors == null || anchors.Length == 0)
            return ArenaContext.ReinforcementSpawn(teamId);

        Vector3 wanted = anchors[index % anchors.Length];
        int row = index / anchors.Length;
        if (row > 0)
        {
            float inward = teamId == 0 ? 1f : -1f;
            wanted += new Vector3(row % 2 == 1 ? RowStagger : -RowStagger,
                                  0f, inward * row * RowSpacing);
        }

        return NavMesh.SamplePosition(wanted, out NavMeshHit hit, 6f, NavMesh.AllAreas)
            ? hit.position
            : wanted;
    }

    // ---------- bookkeeping ----------

    /// <summary>
    /// Catalogue the scene's own bots, once. Anything this class or the gold
    /// reinforcements built is excluded by name, so a later call can never
    /// mistake a clone for part of the permanent cast and start cloning clones.
    /// </summary>
    static void Discover()
    {
        if (SceneBots[0].Count > 0 || SceneBots[1].Count > 0)
            return;

        foreach (var brain in Object.FindObjectsByType<AIBrain>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
        {
            if (brain == null || IsBuilt(brain.name))
                continue;
            var shield = brain.GetComponent<EnergyShield>();
            int team = shield != null ? Mathf.Clamp(shield.teamId, 0, 1) : 1;
            SceneBots[team].Add(brain);
        }

        // FindObjectsByType promises no order, and "the first two bots" has to
        // mean the same two every match or the personas shuffle between them.
        foreach (var team in SceneBots)
            team.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    static void Clear()
    {
        // Sweep first: a recompile during Play empties Extras but leaves the
        // robots standing, and they are only findable by their stamped name.
        foreach (var brain in Object.FindObjectsByType<AIBrain>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
            if (brain != null && brain.name.Contains(ExtraMarker) && !Extras.Contains(brain.gameObject))
                Extras.Add(brain.gameObject);

        foreach (var extra in Extras)
        {
            if (extra == null)
                continue;
            var brain = extra.GetComponent<AIBrain>();
            if (brain != null)
                GameModeController.Instance?.UnregisterBot(brain);
            // Deactivated as well as destroyed: Destroy only lands at the end of
            // the frame, and Apply re-counts the field before then.
            extra.SetActive(false);
            Object.Destroy(extra);
        }
        Extras.Clear();
    }

    static bool IsBuilt(string name) =>
        name.Contains(ExtraMarker) || name.Contains(RobotReinforcements.BoughtMarker);
}

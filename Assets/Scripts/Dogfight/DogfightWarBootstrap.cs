using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Command-line entry for an offline DOGFIGHT: AI v AI render run: launching
/// the editor with `-dogfightWar` starts the exhibition as soon as the menu
/// world has booted, no clicks involved. Same shape as StoryBootstrap — a
/// [RuntimeInitializeOnLoadMethod] reading the process arguments, because
/// that is the one hook that reliably fires when play mode actually begins.
/// The editor-side recorder (DogfightRender) watches Dogfight.Active.
///
/// Optional dials, each a flag with one value:
///     -dogfightSize 4                     jets per side (1..8)
///     -dogfightRobots ranger,titan        cyan,magenta by roster displayName
///     -dogfightMap donut                  airfield | planet | donut
/// </summary>
public static class DogfightWarBootstrap
{
    public const string Arg = "-dogfightWar";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Arg) < 0)
            return;
        var go = new GameObject("DogfightWarBootstrap");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<DogfightWarBootstrapRunner>();
    }
}

class DogfightWarBootstrapRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        // Let the runtime-built world settle: menu, roster, arena, post-FX.
        for (int i = 0; i < 90; i++)
            yield return null;
        while (GameModeController.Instance == null)
            yield return null;

        int size = 4;
        string sizeArg = ArgAfter("-dogfightSize");
        if (sizeArg != null && int.TryParse(sizeArg, out int parsed))
            size = parsed;
        DogfightTeamSize.PerTeam = size;

        string mapArg = ArgAfter("-dogfightMap");
        if (mapArg != null)
            DogfightMapPick.Chosen = mapArg.ToLowerInvariant() switch
            {
                "planet" => DogfightMapKind.TinyPlanet,
                "donut" => DogfightMapKind.DonutStation,
                _ => DogfightMapKind.Airfield,
            };

        var controller = GameModeController.Instance;
        var roster = controller.Roster;
        if (roster == null || !roster.HasRobots)
        {
            // No roster, no picks to make — the controller's own fallback
            // starts the war with whatever it has.
            controller.OpenRobotSelect(GameMode.DogfightWar);
            Destroy(gameObject);
            yield break;
        }

        int cyan = 0;
        int magenta = Mathf.Min(1, roster.robots.Length - 1);
        string robotsArg = ArgAfter("-dogfightRobots");
        if (robotsArg != null)
        {
            var names = robotsArg.Split(',');
            if (names.Length > 0) cyan = IndexOf(roster, names[0], cyan);
            if (names.Length > 1) magenta = IndexOf(roster, names[1], magenta);
        }

        // The select screen's own path: opening it sets the pending mode,
        // launching with picks closes it and starts the war.
        controller.OpenRobotSelect(GameMode.DogfightWar);
        controller.LaunchSelectedMatch(cyan, magenta);
        Destroy(gameObject);
    }

    static int IndexOf(RobotRoster roster, string name, int fallback)
    {
        for (int i = 0; i < roster.robots.Length; i++)
            if (string.Equals(roster.robots[i].displayName, name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return i;
        Debug.LogWarning($"DogfightWarBootstrap: no robot named '{name}', using roster slot {fallback}.");
        return fallback;
    }

    static string ArgAfter(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, flag);
        if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("-"))
            return null;
        return args[at + 1];
    }
}

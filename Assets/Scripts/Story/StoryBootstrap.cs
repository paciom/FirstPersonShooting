using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Command-line entry for a render run: launching the editor with
/// `-storyEpisode &lt;id&gt;` plays that episode as soon as the menu world has
/// booted, no clicks involved. The same shape as MenuCapture's boot — a
/// [RuntimeInitializeOnLoadMethod] reading the process arguments, because
/// that is the one hook that reliably fires when play mode actually begins.
/// The editor-side recorder (EpisodeRender) watches StoryDirector.Completed.
/// </summary>
public static class StoryBootstrap
{
    public const string Arg = "-storyEpisode";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, Arg);
        if (at < 0)
            return;
        string id = at + 1 < args.Length && !args[at + 1].StartsWith("-")
            ? args[at + 1] : "pilot";
        var go = new GameObject("StoryBootstrap");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StoryBootstrapRunner>().episode = id;
    }
}

class StoryBootstrapRunner : MonoBehaviour
{
    public string episode;

    IEnumerator Start()
    {
        // Let the runtime-built world settle: menu, roster, arena, post-FX.
        for (int i = 0; i < 90; i++)
            yield return null;
        while (GameModeController.Instance == null)
            yield return null;

        GameModeController.Instance.StartStory(episode, autoExit: true);
        Destroy(gameObject);
    }
}

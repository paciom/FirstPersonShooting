using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates the arena scene from ArenaBuilder every time you enter Play mode,
/// so pressing Play always reflects the latest code — no need to run the
/// "Build Greybox Arena" menu item or restart Unity after editing scripts.
///
/// It cancels the initial Play request, rebuilds in edit mode, then re-enters
/// Play with the fresh scene. Toggle via Photon Arena > Auto-Rebuild Arena On
/// Play (on by default). Turn it off once the arena is stable to skip the
/// few-second rebuild on each Play.
/// </summary>
[InitializeOnLoad]
public static class AutoRebuildOnPlay
{
    const string MenuName = "Photon Arena/Auto-Rebuild Arena On Play";
    const string Pref = "PhotonArena.AutoRebuildOnPlay";
    static bool _skipRebuild;

    static AutoRebuildOnPlay()
    {
        EditorApplication.playModeStateChanged += OnChange;

        // Script recompiles during Play wipe this project's runtime-built state
        // (weapon specs, AI arrays, event wiring) and leave a broken session.
        // 2 = "Stop Playing And Recompile" in Preferences > General.
        if (EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0) != 2)
        {
            EditorPrefs.SetInt("ScriptCompilationDuringPlay", 2);
            Debug.Log("[AutoRebuildOnPlay] Set 'Script Changes While Playing' to Stop Playing And Recompile.");
        }
    }

    static bool Enabled
    {
        get => EditorPrefs.GetBool(Pref, true);
        set => EditorPrefs.SetBool(Pref, value);
    }

    [MenuItem(MenuName)]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuName, true)]
    static bool ToggleValidate()
    {
        UnityEditor.Menu.SetChecked(MenuName, Enabled);
        return true;
    }

    static void OnChange(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode || !Enabled)
            return;

        // This transition is the one we re-triggered after rebuilding — let it through.
        if (_skipRebuild)
        {
            _skipRebuild = false;
            return;
        }

        EditorApplication.isPlaying = false;   // cancel; rebuild first, then replay
        try
        {
            Debug.Log("[AutoRebuildOnPlay] Rebuilding arena before Play...");
            ArenaBuilder.BuildAll();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AutoRebuildOnPlay] Rebuild failed, playing existing scene: {e}");
        }

        _skipRebuild = true;
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }
}

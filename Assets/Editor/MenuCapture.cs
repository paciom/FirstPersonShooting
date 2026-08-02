#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnostic: runs the real title screen in batch play mode and writes a
/// screenshot to PreviewCaptures/MenuIcons. The menu is built entirely at
/// runtime, so there is nothing to look at from edit mode — this has to
/// actually press Play.
///
/// Batch: -executeMethod MenuCapture.CaptureBatch   (no -quit; exits itself)
///
/// The EditorPrefs flag is how the request survives the TWO domain reloads
/// between the -executeMethod call and a running menu (initial script load,
/// then the play-mode reload). The runner consumes it only once play mode is
/// real, and a non-batch editor clears it defensively so a stale flag from a
/// crashed run can never hijack the user's next normal editor session.
/// </summary>
public static class MenuCapture
{
    internal const string Flag = "PhotonArena_MenuCapture";
    internal const string OutDir = "PreviewCaptures/MenuIcons";
    const string ScenePath = "Assets/Scenes/GreyboxArena.unity";

    public static void CaptureBatch()
    {
        EditorPrefs.SetBool(Flag, true);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }
}

[InitializeOnLoad]
static class MenuCaptureRunner
{
    static int _frames;

    static MenuCaptureRunner()
    {
        if (!EditorPrefs.GetBool(MenuCapture.Flag, false))
            return;
        if (!Application.isBatchMode)
        {
            // A stale flag in an interactive editor must die, not fire.
            EditorPrefs.SetBool(MenuCapture.Flag, false);
            return;
        }
        if (!Application.isPlaying)
            return;   // initial compile load — leave the flag for the play reload
        EditorPrefs.SetBool(MenuCapture.Flag, false);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        // ~1.5s of play: scene built, menu raised, art loaded off Resources.
        _frames++;
        if (_frames < 90)
            return;
        EditorApplication.update -= Tick;

        try
        {
            Directory.CreateDirectory(MenuCapture.OutDir);
            ScreenCapture.CaptureScreenshot($"{MenuCapture.OutDir}/menu_full.png");
            // CaptureScreenshot lands at the END of the current frame, so the
            // editor cannot exit until one more has been pumped.
            EditorApplication.update += Finish;
        }
        catch
        {
            EditorApplication.Exit(1);
        }
    }

    static int _settle;

    static void Finish()
    {
        if (++_settle < 10)
            return;
        EditorApplication.update -= Finish;
        Debug.Log($"MenuCapture: wrote {MenuCapture.OutDir}/menu_full.png");
        EditorApplication.Exit(0);
    }
}
#endif

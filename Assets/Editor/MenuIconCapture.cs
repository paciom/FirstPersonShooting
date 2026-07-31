#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnostic: runs the real menu in batch play mode and writes every app-icon
/// diorama's RenderTexture to PreviewCaptures/MenuIcons as PNGs, plus a whole-
/// screen shot of the menu itself. The icons are built by runtime-only code
/// (Object.Destroy, TeamPaint), so unlike PreviewCaptureTool this cannot render
/// them from edit mode — it has to actually press Play.
///
/// Batch: -executeMethod MenuIconCapture.CaptureBatch   (no -quit; exits itself)
///
/// The EditorPrefs flag is how the request survives the TWO domain reloads
/// between the -executeMethod call and a running menu (initial script load,
/// then the play-mode reload). The runner consumes it only once play mode is
/// real, and a non-batch editor clears it defensively so a stale flag from a
/// crashed run can never hijack the user's next normal editor session.
/// </summary>
public static class MenuIconCapture
{
    internal const string Flag = "PhotonArena_MenuIconCapture";
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
static class MenuIconCaptureRunner
{
    static int _frames;

    static MenuIconCaptureRunner()
    {
        if (!EditorPrefs.GetBool(MenuIconCapture.Flag, false))
            return;
        if (!Application.isBatchMode)
        {
            // A stale flag in an interactive editor must die, not fire.
            EditorPrefs.SetBool(MenuIconCapture.Flag, false);
            return;
        }
        if (!Application.isPlaying)
            return;   // initial compile load — leave the flag for the play reload
        EditorPrefs.SetBool(MenuIconCapture.Flag, false);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        // ~1.5s of play: menu built, team paints blitted, icon cameras rendering.
        _frames++;
        if (_frames == 60)
            ScreenCapture.CaptureScreenshot($"{MenuIconCapture.OutDir}/menu_full.png");
        if (_frames < 90)
            return;
        EditorApplication.update -= Tick;

        try
        {
            Capture();
        }
        finally
        {
            EditorApplication.Exit(0);
        }
    }

    static void Capture()
    {
        Directory.CreateDirectory(MenuIconCapture.OutDir);
        var sync = Object.FindFirstObjectByType<MenuIconRigSync>();
        if (sync == null || sync.textures == null)
        {
            Debug.LogError("MenuIconCapture: no MenuIconRigSync in the running menu.");
            return;
        }

        for (int i = 0; i < sync.textures.Length; i++)
        {
            var rt = sync.textures[i];
            if (rt == null)
                continue;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes($"{MenuIconCapture.OutDir}/{(MenuIcon)i}.png", shot.EncodeToPNG());
            Object.Destroy(shot);
        }
        Debug.Log($"MenuIconCapture: wrote {sync.textures.Length} icons to {MenuIconCapture.OutDir}");
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Diagnostic: runs the real title screen in batch play mode and writes a
/// screenshot to PreviewCaptures/MenuIcons. The menu is built entirely at
/// runtime, so there is nothing to look at from edit mode — this has to
/// actually press Play.
///
///     Unity -batchmode -force-d3d11 -projectPath . \
///           -executeMethod MenuCapture.Batch -captureMenu
///
/// Same shape as MenuArtForge, and for the same reason: the capture hangs off
/// [RuntimeInitializeOnLoadMethod], which fires when the GAME starts. The
/// earlier [InitializeOnLoad] + EditorPrefs version sat through an entire
/// batch run without ever firing, because that hook does not reliably observe
/// play mode on the domain reload that begins it.
///
/// It does NOT use ScreenCapture.CaptureScreenshot. A batch-mode editor has no
/// Game View to capture, and that call reports success into the log while
/// writing no file at all. Instead the menu canvas is temporarily switched to
/// ScreenSpaceCamera and rendered through a camera of our own into a render
/// texture, which also fixes the output size at 1920x1080 rather than
/// whatever a headless window claims to be.
/// </summary>
public static class MenuCapture
{
    public const string Arg = "-captureMenu";
    public const string SizeArg = "-captureSize";      // e.g. -captureSize 2560x1080
    public const string OutDir = "PreviewCaptures/MenuIcons";

    const string ScenePath = "Assets/Scenes/GreyboxArena.unity";

    public static void Batch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        UnityEditor.EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Arg) < 0)
            return;
        var go = new GameObject("MenuCapture");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MenuCaptureRunner>();
    }
}

class MenuCaptureRunner : MonoBehaviour
{
    int _width = 1920, _height = 1080;
    string _suffix = "";

    /// <summary>`-captureSize 2560x1080` shoots the menu at another window
    /// shape, which is the only way to check that the layout is actually
    /// aspect-invariant rather than merely correct at the reference size.</summary>
    void Awake()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, MenuCapture.SizeArg);
        if (at < 0 || at + 1 >= args.Length)
            return;
        var parts = args[at + 1].Split('x');
        if (parts.Length == 2
            && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
            && w > 0 && h > 0)
        {
            _width = w;
            _height = h;
            _suffix = $"_{w}x{h}";
        }
        else
        {
            Debug.LogWarning($"MenuCapture: cannot read {MenuCapture.SizeArg} '{args[at + 1]}'");
        }
    }

    IEnumerator Start()
    {
        // Long enough for the scene to settle and the menu's art to come off
        // Resources.
        for (int i = 0; i < 150; i++)
            yield return null;

        var canvas = FindCanvas();
        if (canvas == null)
        {
            Debug.LogError("MenuCapture: no MainMenu canvas in the running scene.");
            UnityEditor.EditorApplication.Exit(1);
            yield break;
        }

        var rt = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var camGo = new GameObject("MenuCaptureCam");
        // Parked far below every world this game builds (arena y=0, select
        // rigs -150..-210, icon rigs -300) with a far plane barely past the
        // canvas. The canvas has to stay in the culling mask to be drawn at
        // all, and the arena is on that same layer -- so the only way to keep
        // scenery out of the shot is to have none of it in frame.
        camGo.transform.position = new Vector3(0f, -5000f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 1 << canvas.gameObject.layer;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 12f;

        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;

        // Frames, not WaitForEndOfFrame: a batch-mode editor has no render
        // loop to end, so that yield never resolves and the coroutine parks
        // forever. cam.Render() is an explicit draw and needs no such hook.
        for (int i = 0; i < 5; i++)
            yield return null;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(_width, _height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        Directory.CreateDirectory(MenuCapture.OutDir);
        string path = $"{MenuCapture.OutDir}/menu_full{_suffix}.png";
        File.WriteAllBytes(path, shot.EncodeToPNG());

        canvas.renderMode = previousMode;
        canvas.worldCamera = previousCamera;

        Debug.Log($"MenuCapture: wrote {path}");
        yield return null;
        UnityEditor.EditorApplication.Exit(0);
    }

    static Canvas FindCanvas()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
            if (canvas.gameObject.name == "MainMenu")
                return canvas;
        return null;
    }
}
#endif

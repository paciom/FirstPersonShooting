#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostic: opens the robot select screen in batch play mode and
/// screenshots it, once per Gunfight mode, so the team-size row can be looked
/// at rather than reasoned about. Same shape as MenuCapture, for the same
/// reasons — see its notes on why the canvas is re-pointed at a camera of our
/// own instead of ScreenCapture.
///
///     Unity -batchmode -force-d3d11 -projectPath . \
///           -executeMethod TeamSizeCapture.Batch -captureTeamSize
/// </summary>
public static class TeamSizeCapture
{
    public const string Arg = "-captureTeamSize";
    public const string OutDir = "PreviewCaptures/TeamSize";

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
        var go = new GameObject("TeamSizeCapture");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<TeamSizeCaptureRunner>();
    }
}

class TeamSizeCaptureRunner : MonoBehaviour
{
    const int Width = 1920, Height = 1080;

    IEnumerator Start()
    {
        for (int i = 0; i < 150; i++)
            yield return null;

        var controller = GameModeController.Instance;
        if (controller == null)
        {
            Debug.LogError("TeamSizeCapture: no GameModeController.");
            UnityEditor.EditorApplication.Exit(1);
            yield break;
        }

        Directory.CreateDirectory(TeamSizeCapture.OutDir);

        foreach (var mode in new[] { GameMode.PlayerVsAI, GameMode.AIvAI })
        {
            // One shot on the default (3, which is no preset) and one on the
            // widest preset, so both the lit and the unlit chip states get seen.
            TeamSize.PerTeam = mode == GameMode.AIvAI ? 10 : TeamSize.Default;
            controller.OpenRobotSelect(mode);
            for (int i = 0; i < 40; i++)
                yield return null;

            var canvas = FindSelectCanvas();
            if (canvas == null)
            {
                Debug.LogError($"TeamSizeCapture: no robot-select canvas for {mode}.");
                UnityEditor.EditorApplication.Exit(1);
                yield break;
            }

            yield return Shoot(canvas, $"{TeamSizeCapture.OutDir}/select_{mode}.png");
            controller.CancelRobotSelect();
            for (int i = 0; i < 10; i++)
                yield return null;
        }

        // And the half that matters more: does the number actually put that
        // many robots in the arena, standing somewhere sane?
        foreach (int size in new[] { 10, 1 })
        {
            TeamSize.PerTeam = size;
            controller.OpenRobotSelect(GameMode.AIvAI);
            for (int i = 0; i < 20; i++)
                yield return null;
            controller.LaunchSelectedMatch(0, 0);
            for (int i = 0; i < 20; i++)
                yield return null;
            controller.LaunchArena(0);
            for (int i = 0; i < 90; i++)
                yield return null;

            Report(size);
            yield return TopDown($"{TeamSizeCapture.OutDir}/arena_{size}v{size}.png");
            controller.EnterMenu();
            for (int i = 0; i < 30; i++)
                yield return null;
            Debug.Log($"TeamSizeCapture: back in the menu after {size} v {size} — " + Census());
        }

        UnityEditor.EditorApplication.Exit(0);
    }

    static void Report(int size)
    {
        Debug.Log($"TeamSizeCapture: asked for {size} v {size} — {Census()}");
    }

    static string Census()
    {
        int cyan = 0, magenta = 0, parked = 0;
        foreach (var brain in FindObjectsByType<AIBrain>(FindObjectsInactive.Include,
                                                        FindObjectsSortMode.None))
        {
            if (!brain.gameObject.activeInHierarchy) { parked++; continue; }
            var shield = brain.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId == 0) cyan++; else magenta++;
        }
        return $"cyan {cyan}, magenta {magenta}, parked {parked}";
    }

    /// <summary>Straight down over the arena, so a formation can be counted by eye.</summary>
    IEnumerator TopDown(string path)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var camGo = new GameObject("TopDownCam");
        camGo.transform.position = new Vector3(0f, 46f, 0f);
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = 60f;
        cam.farClipPlane = 200f;

        for (int i = 0; i < 3; i++)
            yield return null;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, shot.EncodeToPNG());

        Destroy(camGo);
        Debug.Log($"TeamSizeCapture: wrote {path}");
    }

    IEnumerator Shoot(Canvas canvas, string path)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var camGo = new GameObject("TeamSizeCaptureCam");
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

        for (int i = 0; i < 5; i++)
            yield return null;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes(path, shot.EncodeToPNG());

        canvas.renderMode = previousMode;
        canvas.worldCamera = previousCamera;
        Destroy(camGo);
        Debug.Log($"TeamSizeCapture: wrote {path}");
    }

    static Canvas FindSelectCanvas()
    {
        var root = GameObject.Find("RobotSelect");
        return root != null ? root.GetComponentInChildren<Canvas>() : null;
    }
}
#endif

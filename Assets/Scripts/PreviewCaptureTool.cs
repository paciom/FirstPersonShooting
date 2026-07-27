#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Diagnostic: renders a robot exactly the way the select screen's preview does
/// and writes it to Temp/PreviewCapture as a PNG.
///
/// WHY IT EXISTS. The robot previews looked wrong in ways that could not be
/// reproduced by rasterising the same glTF buffers offline — same mesh, same
/// UVs, same texture, different picture. That means something between the file
/// and the screen is responsible (importer, compression, mip selection,
/// shader), and the only way to see which is to capture what Unity actually
/// draws rather than argue about it.
///
/// Lives in Assets/Scripts rather than Assets/Editor on purpose: the helpers it
/// exercises (AddThreePointLights, NormalizeToCenter) are internal to
/// Assembly-CSharp, and an Editor-assembly script could not reach them without
/// widening their access. #if UNITY_EDITOR keeps it out of player builds.
///
/// Menu: Photon Arena > Capture Robot Previews
/// Batch: -executeMethod PreviewCaptureTool.CaptureBatch
/// </summary>
public static class PreviewCaptureTool
{
    const string OutDir = "Temp/PreviewCapture";
    const int Size = 560;
    const string ScenePath = "Assets/Scenes/GreyboxArena.unity";

    [MenuItem("Photon Arena/Capture Robot Previews")]
    public static void CaptureAll()
    {
        var roster = Object.FindFirstObjectByType<RobotRoster>();
        if (roster == null || !roster.HasRobots)
        {
            Debug.LogError("PreviewCapture: no RobotRoster in the open scene. " +
                           "Open GreyboxArena first.");
            return;
        }
        Capture(roster);
    }

    /// <summary>Entry point for -batchmode -executeMethod (opens the scene itself).</summary>
    public static void CaptureBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        var roster = Object.FindFirstObjectByType<RobotRoster>();
        if (roster == null || !roster.HasRobots)
        {
            Debug.LogError("PreviewCapture: no RobotRoster in " + ScenePath);
            EditorApplication.Exit(1);
            return;
        }
        Capture(roster);
        EditorApplication.Exit(0);
    }

    static void Capture(RobotRoster roster)
    {
        Directory.CreateDirectory(OutDir);

        for (int i = 0; i < roster.robots.Length; i++)
        {
            var entry = roster.robots[i];
            if (entry.modelPrefab == null)
                continue;

            // Far below the arena, same reasoning as the real preview rigs: the
            // short far plane then sees nothing but this robot.
            var rig = new GameObject("CaptureRig");
            rig.transform.position = new Vector3(i * 25f, -400f, 0f);

            var holder = new GameObject("Spin");
            holder.transform.SetParent(rig.transform, false);

            var model = Object.Instantiate(entry.modelPrefab, holder.transform);
            RobotSelectMenu.NormalizeToCenter(model, holder.transform);
            RobotSelectMenu.AddThreePointLights(rig.transform);

            var camGo = new GameObject("CaptureCam");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.45f, 3.1f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 12f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.05f, 0.10f, 1f);
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

            // Front and back, because the reported screenshots were of the back
            // and the offline render was of the front.
            Shoot(cam, holder.transform, 0f, $"{OutDir}/{entry.displayName}_front.png");
            Shoot(cam, holder.transform, 180f, $"{OutDir}/{entry.displayName}_back.png");

            Object.DestroyImmediate(rig);
        }

        Debug.Log($"PreviewCapture: wrote {roster.robots.Length * 2} PNGs to {OutDir}");
        AssetDatabase.Refresh();
    }

    static void Shoot(Camera cam, Transform subject, float yaw, string path)
    {
        subject.localRotation = Quaternion.Euler(0f, yaw, 0f);

        var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes(path, shot.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(shot);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
#endif

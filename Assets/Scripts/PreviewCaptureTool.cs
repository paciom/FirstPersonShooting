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
    // NOT Temp/: that is Unity's own scratch directory and it is wiped on
    // editor shutdown, so a batchmode run writes its PNGs and then deletes them
    // on the way out. PREVIEW_CAPTURE_DIR overrides for one-off runs.
    static string OutDir =>
        System.Environment.GetEnvironmentVariable("PREVIEW_CAPTURE_DIR") ?? "PreviewCaptures";
    // Overridable so a one-off run can render a large hero frame (e.g. as the
    // starting image for an image-to-video transformation test) without
    // changing what the routine diagnostic captures look like.
    static int Size
    {
        get
        {
            var raw = System.Environment.GetEnvironmentVariable("PREVIEW_CAPTURE_SIZE");
            return int.TryParse(raw, out int parsed) && parsed >= 64 ? parsed : 560;
        }
    }
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


    /// <summary>Dumps material info for arbitrary GLB assets, to compare how the
    /// importer treats candidate source-level fixes. Paths via UVTEST_GLBS.</summary>
    public static void DescribeGlbsBatch()
    {
        var report = new System.Text.StringBuilder();
        var paths = (System.Environment.GetEnvironmentVariable("UVTEST_GLBS") ?? "")
            .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
            if (go == null)
            {
                report.AppendLine($"===== {path}");
                report.AppendLine("  FAILED TO LOAD");
                continue;
            }
            var inst = Object.Instantiate(go);
            Describe(inst, path.Trim(), report);
            Object.DestroyImmediate(inst);
        }
        Directory.CreateDirectory(OutDir);
        File.WriteAllText($"{OutDir}/uvtest.txt", report.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>Logs what the roster actually carries per robot, so a missing
    /// stage folder or clip shows up as data rather than as a blank panel.</summary>
    public static void RosterReportBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        var roster = Object.FindFirstObjectByType<RobotRoster>();
        if (roster == null)
        {
            Debug.LogError("PreviewCapture: no RobotRoster");
            EditorApplication.Exit(1);
            return;
        }
        foreach (var entry in roster.robots)
        {
            Debug.Log($"ROSTER {entry.displayName,-9} model={(entry.modelPrefab != null)} " +
                      $"vehicle={(entry.vehiclePrefab != null)} " +
                      $"stages={(entry.transformStages == null ? 0 : entry.transformStages.Length)} " +
                      $"hasStages={entry.HasStages} " +
                      $"clip={(entry.transformVideo != null ? entry.transformVideo.name : "-")}");
        }
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Renders a robot built the way the ARENA builds it — through
    /// RobotFactory.InstantiateNormalized, standing in the real scene under the
    /// real lights — and dumps its material state.
    ///
    /// The select screen and the arena take different paths to the same model:
    /// the cards instantiate the prefab directly under studio lights, the arena
    /// runs it through RobotFactory, which copies and tints every material and
    /// then lights it with the arena rig. Either step can change how it reads,
    /// so this reproduces the arena path exactly rather than approximating it.
    /// </summary>
    public static void ArenaRobotReportBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        var roster = Object.FindFirstObjectByType<RobotRoster>();
        if (roster == null || !roster.HasRobots)
        {
            Debug.LogError("ArenaRobotReport: no roster");
            EditorApplication.Exit(1);
            return;
        }
        Directory.CreateDirectory(OutDir);
        var report = new System.Text.StringBuilder();

        var entry = roster.robots[0];
        var body = new GameObject("ArenaProbe").transform;
        // Where a bot actually stands, so the arena's own lights and ambient
        // apply exactly as they do in a match.
        body.position = new Vector3(0f, 0.9f, 0f);

        var teamTint = new Color(0.2f, 0.9f, 1f);
        var model = RobotFactory.InstantiateNormalized(entry.modelPrefab, body, teamTint);
        // RobotFactory's own repaint is a no-op outside play mode (a repaint is
        // a RenderTexture and would not survive a scene save). This probe never
        // saves anything, so it asks for the real thing — a report of the arena
        // path in factory colours would be a report of something else.
        TeamPaint.Apply(model, teamTint, editTime: true);

        report.AppendLine($"===== ARENA PATH: {entry.displayName}  tint={teamTint}");
        report.AppendLine($"  ambient mode  {RenderSettings.ambientMode}");
        report.AppendLine($"  ambient light {RenderSettings.ambientLight}");
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            report.AppendLine($"  light {light.name,-16} {light.type,-11} " +
                              $"intensity={light.intensity:F2} colour={light.color} " +
                              $"range={light.range:F1}");
        DescribeArenaMaterials(model, report);

        var camGo = new GameObject("ArenaProbeCam");
        camGo.transform.position = body.position + new Vector3(0f, 0.15f, 3.0f);
        camGo.transform.rotation = Quaternion.Euler(4f, 180f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        Shoot(cam, body, 0f, $"{OutDir}/ARENA_{entry.displayName}.png");

        File.WriteAllText($"{OutDir}/arena_report.txt", report.ToString());
        Debug.Log(report.ToString());
        Object.DestroyImmediate(body.gameObject);
        Object.DestroyImmediate(camGo);
        EditorApplication.Exit(0);
    }

    /// <summary>Dumps colour and texture state, including the tinted copies.</summary>
    static void DescribeArenaMaterials(GameObject model, System.Text.StringBuilder report)
    {
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            var material = renderer.sharedMaterial;
            if (material == null)
                continue;
            report.AppendLine($"  renderer {renderer.name} -> {material.shader.name}");
            report.AppendLine($"    keywords {string.Join(",", material.shaderKeywords)}");
            report.AppendLine($"    HasProperty _BaseColor={material.HasProperty("_BaseColor")} " +
                              $"_Color={material.HasProperty("_Color")} " +
                              $"baseColorFactor={material.HasProperty("baseColorFactor")}");
            var shader = material.shader;
            for (int p = 0; p < ShaderUtil.GetPropertyCount(shader); p++)
            {
                string prop = ShaderUtil.GetPropertyName(shader, p);
                var kind = ShaderUtil.GetPropertyType(shader, p);
                if (kind == ShaderUtil.ShaderPropertyType.Color)
                    report.AppendLine($"    COLOR {prop,-24} {material.GetColor(prop)}");
                else if (kind == ShaderUtil.ShaderPropertyType.Vector && prop.EndsWith("_ST"))
                    report.AppendLine($"    ST    {prop,-24} {material.GetVector(prop)}");
            }
        }
    }

    static void Capture(RobotRoster roster)
    {
        Directory.CreateDirectory(OutDir);
        var report = new System.Text.StringBuilder();

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
            // Three-quarter hero angle: reads the silhouette better than a flat
            // front-on shot, which matters when the frame is seeding a video.
            Shoot(cam, holder.transform, 35f, $"{OutDir}/{entry.displayName}_hero.png");

            Describe(model, entry.displayName, report);
            DescribeVehicle(entry, report);

            Object.DestroyImmediate(rig);
        }

        File.WriteAllText($"{OutDir}/report.txt", report.ToString());
        Debug.Log($"PreviewCapture: wrote {roster.robots.Length * 2} PNGs to {OutDir}");
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Records what Unity believes it loaded, so the imported mesh and texture
    /// can be compared numerically against the glTF file they came from. The
    /// render disagrees with an offline rasterisation of the same buffers, and
    /// only the imported data can say where the two diverge.
    /// </summary>
    static void Describe(GameObject model, string name, System.Text.StringBuilder report)
    {
        report.AppendLine($"===== {name}");

        var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var plain = model.GetComponentInChildren<MeshRenderer>();
        var mesh = skinned != null ? skinned.sharedMesh
                 : plain != null ? plain.GetComponent<MeshFilter>()?.sharedMesh : null;
        var material = skinned != null ? skinned.sharedMaterial : plain?.sharedMaterial;

        if (mesh == null)
        {
            report.AppendLine("  no mesh found");
            return;
        }

        var uv = mesh.uv;
        var verts = mesh.vertices;
        report.AppendLine($"  renderer      {(skinned != null ? "Skinned" : "MeshRenderer")}");
        report.AppendLine($"  mesh          {mesh.name}");
        report.AppendLine($"  vertexCount   {mesh.vertexCount}   subMeshes {mesh.subMeshCount}");
        report.AppendLine($"  uv.Length     {uv.Length}   uv2 {mesh.uv2.Length}   normals {mesh.normals.Length}");
        for (int k = 0; k < 4 && k < uv.Length; k++)
            report.AppendLine($"  uv[{k}]        ({uv[k].x:F6}, {uv[k].y:F6})   " +
                              $"pos[{k}] ({verts[k].x:F4}, {verts[k].y:F4}, {verts[k].z:F4})");

        if (material == null)
        {
            report.AppendLine("  no material");
            return;
        }
        report.AppendLine($"  shader        {material.shader.name}");
        report.AppendLine($"  keywords      {string.Join(",", material.shaderKeywords)}");

        // Enumerate rather than guess property names: this is glTFast's own
        // Shader Graph, not URP Lit, so _BaseMap and friends do not exist. The
        // texture ST (scale/offset) is the value in question — a scale.y of -1
        // is a V flip on top of the one already baked into the mesh UVs.
        var shader = material.shader;
        int count = ShaderUtil.GetPropertyCount(shader);
        for (int p = 0; p < count; p++)
        {
            string prop = ShaderUtil.GetPropertyName(shader, p);
            var kind = ShaderUtil.GetPropertyType(shader, p);
            if (kind == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                var tex = material.GetTexture(prop) as Texture2D;
                report.AppendLine($"  TEX {prop,-24} " +
                    (tex == null ? "(none)"
                     : $"{tex.width}x{tex.height} format={tex.format} mips={tex.mipmapCount} " +
                       $"filter={tex.filterMode}") +
                    $"  scale={material.GetTextureScale(prop)} offset={material.GetTextureOffset(prop)}");
            }
            else if (kind == ShaderUtil.ShaderPropertyType.Vector)
            {
                report.AppendLine($"  VEC {prop,-24} {material.GetVector(prop)}");
            }
        }
    }

    /// <summary>Describes a robot's vehicle form too — those render correctly,
    /// so any material difference between the two is the interesting part.</summary>
    static void DescribeVehicle(RobotRoster.Entry entry, System.Text.StringBuilder report)
    {
        if (entry.vehiclePrefab == null)
            return;
        var instance = Object.Instantiate(entry.vehiclePrefab);
        Describe(instance, entry.displayName + " [VEHICLE]", report);
        Object.DestroyImmediate(instance);
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

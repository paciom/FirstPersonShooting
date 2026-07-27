using System;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the entire greybox arena from code: URP setup, neon materials,
/// arena geometry, NavMesh, player rig, target dummies, AI bots, lighting and
/// bloom. Run from the menu (Photon Arena > Build Greybox Arena) or batch:
///   Unity.exe -batchmode -projectPath <path> -executeMethod ArenaBuilder.BuildAllBatch
/// </summary>
public static class ArenaBuilder
{
    const string ScenePath = "Assets/Scenes/GreyboxArena.unity";

    /// <summary>
    /// Robot every character is built with, and roster entry 0. Must be a
    /// rigged walker: the arena is a ground game, so the default has to have
    /// legs that move (the old static hover-robot is retired).
    /// </summary>
    const string DefaultRobot = "ranger";

    static readonly Color NeonCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color NeonMagenta = new Color(1f, 0.25f, 0.9f);
    static readonly Color NeonOrange = new Color(1f, 0.55f, 0.1f);

    public static void BuildAllBatch()
    {
        try
        {
            BuildAll();
            Debug.Log("[ArenaBuilder] SUCCESS");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArenaBuilder] FAILED: {e}");
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Photon Arena/Build Greybox Arena")]
    public static void BuildAll()
    {
        // Text serialization: diffable scenes for version control and tooling.
        EditorSettings.serializationMode = SerializationMode.ForceText;

        System.IO.Directory.CreateDirectory("Assets/Materials");
        ConfigureUrp();
        EnsureShadersIncluded("PhotonArena/Additive", "PhotonArena/ForceField", "PhotonArena/XRay",
            "PhotonArena/SciFiPanel");
        RepairModelImports();
        SpriteForge.GenerateAll();
        // Rigged walkers are assets, not scene objects: forge any that are
        // missing before the roster scan so one build produces a complete list.
        // (WalkerRigForge's procedural block robot is deliberately NOT forged
        // here — it's a no-credits fallback, kept out of the roster because the
        // Meshy fleet looks far better. Run its menu item to bring it back.)
        MeshyWalkerForge.ForgeIfMissing();
        // Must follow the walker forge: it solves against the prefabs and the
        // idle clips that forge produces, and patches the controller it built.
        TransformRigForge.ForgeIfMissing();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var environment = BuildEnvironment();
        BakeNavMesh(environment);
        BuildDynamicCover(environment);   // after bake: blocks carve the navmesh
        BuildLighting();
        BuildPostProcessing();
        BuildPlayer();
        BuildBots();
        BuildGameController(environment);

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[ArenaBuilder] Scene saved to {ScenePath}");
    }

    // ---------- URP ----------

    static void ConfigureUrp()
    {
        // Idempotent: recreating these assets over live ones briefly leaves the
        // active pipeline without a renderer, which breaks every import that
        // depends on the render pipeline (e.g. glTFast material generation).
        const string pipelinePath = "Assets/Settings/PhotonArena_URP.asset";
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            System.IO.Directory.CreateDirectory("Assets/Settings");

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, "Assets/Settings/PhotonArena_Renderer.asset");

            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.supportsHDR = true;   // HDR emissives feed bloom — the whole neon look
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        Debug.Log("[ArenaBuilder] URP configured.");
    }

    /// <summary>
    /// Ensure runtime-loaded custom shaders (Shader.Find from code) are compiled
    /// into player builds, not just kept alive by editor references.
    /// </summary>
    static void EnsureShadersIncluded(params string[] shaderNames)
    {
        var settings = GraphicsSettings.GetGraphicsSettings();
        var so = new SerializedObject(settings);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in shaderNames)
        {
            var shader = Shader.Find(name);
            if (shader == null)
                continue;

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }

            if (!present)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
        }
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Reimport any model whose cached import result is broken (e.g. imported
    /// while the render pipeline was in a transitional state).
    /// </summary>
    static void RepairModelImports()
    {
        if (!System.IO.Directory.Exists("Assets/Models"))
            return;
        // Recursive: the Meshy rig/walk/run sources live in Assets/Models/Meshy.
        foreach (var file in System.IO.Directory.GetFiles("Assets/Models", "*.glb",
                     System.IO.SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.Log($"[ArenaBuilder] Reimporting broken model: {path}");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            var main = AssetDatabase.LoadMainAssetAtPath(path);
            Debug.Log($"[ArenaBuilder] {path}: main={(main == null ? "NULL" : main.GetType().Name)}, " +
                      $"asGameObject={(AssetDatabase.LoadAssetAtPath<GameObject>(path) == null ? "NULL" : "ok")}");
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (sub != null)
                    Debug.Log($"[ArenaBuilder]   sub: {sub.GetType().Name} '{sub.name}'");
        }
    }

    // ---------- Materials ----------

    static Material MakeLitMaterial(string name, Color color, Color? emission = null, float emissionIntensity = 3f)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission.Value * emissionIntensity);
        }
        AssetDatabase.CreateAsset(mat, $"Assets/Materials/{name}.mat");
        return mat;
    }

    /// <summary>
    /// Creates a sci-fi panel material (triplanar grooves + glowing seams) for
    /// arena surfaces. panelSize is the world-space panel spacing in metres.
    /// </summary>
    static Material MakeSciFiMaterial(string name, Color baseColor, Color seamColor,
        float panelSize, float seamGlow = 1.5f, int patternMode = 0, Color? accent = null)
    {
        var mat = new Material(Shader.Find("PhotonArena/SciFiPanel")) { name = name };
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_PanelColor", baseColor * 0.35f);
        mat.SetColor("_SeamColor", seamColor);
        mat.SetFloat("_Tiling", panelSize);
        mat.SetFloat("_SeamGlow", seamGlow);
        mat.SetFloat("_PatternMode", patternMode);
        mat.SetColor("_AccentColor", accent ?? new Color(1f, 0.6f, 0.1f));
        AssetDatabase.CreateAsset(mat, $"Assets/Materials/{name}.mat");
        return mat;
    }

    static readonly Color WarnAmber = new Color(1f, 0.62f, 0.12f);

    // ---------- Imported models (Meshy) ----------

    static GameObject LoadModel(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.glb")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.fbx");
    }

    /// <summary>
    /// Loads a roster robot by bare name, preferring the forged rigged prefab
    /// (walk/run animated) over a plain static .glb of the same name.
    /// </summary>
    static GameObject LoadRobotModel(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Generated/{name}-robot.prefab")
            ?? LoadModel($"{name}-robot");
    }

    /// <summary>Ground-vehicle form for a roster robot, or null if it has none.</summary>
    static GameObject LoadVehicleModel(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Meshy/{name}-vehicle.glb");
    }

    /// <summary>Instantiate a model, scale to targetHeight, sit its base on groundPosition.</summary>
    static GameObject PlaceProp(GameObject prefab, GameObject parent, Vector3 groundPosition,
        float targetHeight, float yRotation, bool addBoxCollider)
    {
        var instance = (GameObject)UnityEngine.Object.Instantiate(prefab, parent.transform);
        instance.name = prefab.name;
        instance.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        instance.transform.position = groundPosition;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return instance;

        var bounds = CombinedBounds(renderers);
        float scale = targetHeight / Mathf.Max(0.01f, bounds.size.y);
        instance.transform.localScale *= scale;

        bounds = CombinedBounds(renderers);
        instance.transform.position += new Vector3(
            groundPosition.x - bounds.center.x,
            groundPosition.y - bounds.min.y,
            groundPosition.z - bounds.center.z);

        if (addBoxCollider)
        {
            bounds = CombinedBounds(renderers);
            var box = instance.AddComponent<BoxCollider>();
            box.center = instance.transform.InverseTransformPoint(bounds.center);
            var lossy = instance.transform.lossyScale;
            box.size = new Vector3(bounds.size.x / lossy.x, bounds.size.y / lossy.y, bounds.size.z / lossy.z);
        }
        return instance;
    }

    static Bounds CombinedBounds(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }

    // ---------- Environment ----------

    static GameObject BuildEnvironment()
    {
        var root = new GameObject("Environment");

        // Darker gunmetal plating with a fine tech grid — reads as a ship deck,
        // not a neon box. Walls get bolted hull plates.
        var floorMat = MakeSciFiMaterial("Floor", new Color(0.12f, 0.13f, 0.16f), NeonCyan, 3.5f, 0.7f, 2);
        var wallMat = MakeSciFiMaterial("Wall", new Color(0.16f, 0.18f, 0.23f), NeonCyan, 3.0f, 0.7f, 3);
        var trimCyan = MakeLitMaterial("TrimCyan", Color.black, NeonCyan, 4f);
        var trimMagenta = MakeLitMaterial("TrimMagenta", Color.black, NeonMagenta, 4f);

        Box(root, "Floor", new Vector3(0, -0.25f, 0), new Vector3(40, 0.5f, 40), floorMat);

        // Perimeter walls + glowing top trim.
        Box(root, "WallN", new Vector3(0, 1.5f, 20), new Vector3(40, 3, 0.5f), wallMat);
        Box(root, "WallS", new Vector3(0, 1.5f, -20), new Vector3(40, 3, 0.5f), wallMat);
        Box(root, "WallE", new Vector3(20, 1.5f, 0), new Vector3(0.5f, 3, 40), wallMat);
        Box(root, "WallW", new Vector3(-20, 1.5f, 0), new Vector3(0.5f, 3, 40), wallMat);
        Trim(root, new Vector3(0, 3.05f, 20), new Vector3(40, 0.12f, 0.12f), trimCyan);
        Trim(root, new Vector3(0, 3.05f, -20), new Vector3(40, 0.12f, 0.12f), trimCyan);
        Trim(root, new Vector3(20, 3.05f, 0), new Vector3(0.12f, 0.12f, 40), trimMagenta);
        Trim(root, new Vector3(-20, 3.05f, 0), new Vector3(0.12f, 0.12f, 40), trimMagenta);

        // Cover blocks are created after the NavMesh bake (see BuildDynamicCover)
        // so they can carve the navmesh dynamically as they move and regrow.

        // Four glowing corner pillars — landmarks that read on camera.
        Box(root, "PillarNE", new Vector3(16, 2f, 16), new Vector3(0.8f, 4f, 0.8f), trimCyan);
        Box(root, "PillarNW", new Vector3(-16, 2f, 16), new Vector3(0.8f, 4f, 0.8f), trimMagenta);
        Box(root, "PillarSE", new Vector3(16, 2f, -16), new Vector3(0.8f, 4f, 0.8f), trimMagenta);
        Box(root, "PillarSW", new Vector3(-16, 2f, -16), new Vector3(0.8f, 4f, 0.8f), trimCyan);

        // Meshy-generated props — placed before the NavMesh bake so bots path
        // around them. Silently skipped until the models exist on disk.
        var crate = LoadModel("energy-crate");
        if (crate != null)
        {
            PlaceProp(crate, root, new Vector3(10, 0, -12), 1.3f, 0f, true);
            PlaceProp(crate, root, new Vector3(-7, 0, -13), 1.3f, 0f, true);
            PlaceProp(crate, root, new Vector3(16.5f, 0, 8), 1.3f, 0f, true);
            PlaceProp(crate, root, new Vector3(-16.5f, 0, -8), 1.3f, 0f, true);
        }

        var portal = LoadModel("spawn-portal");
        if (portal != null)
        {
            PlaceProp(portal, root, new Vector3(0, 0, -18.2f), 3.2f, 0f, false);
            PlaceProp(portal, root, new Vector3(0, 0, 18.2f), 3.2f, 180f, false);
        }

        return root;
    }

    /// <summary>
    /// Dynamic cover: destructible/regrowing/moving ArenaBlocks with carving
    /// NavMeshObstacles. Created after the bake so their carving drives bot
    /// pathing instead of a static bake.
    /// </summary>
    static void BuildDynamicCover(GameObject environment)
    {
        // A spread of ship-interior surfaces so the cover looks varied and
        // high-tech rather than a row of identical neon boxes.
        var coverMats = new[]
        {
            MakeSciFiMaterial("Cover_Gunmetal", new Color(0.19f, 0.20f, 0.23f), NeonCyan, 1.6f, 0.8f, 0),
            MakeSciFiMaterial("Cover_Hull", new Color(0.16f, 0.19f, 0.27f), NeonCyan, 2.2f, 0.9f, 3),
            MakeSciFiMaterial("Cover_Hazard", new Color(0.22f, 0.20f, 0.15f), WarnAmber, 1.4f, 1.1f, 1, WarnAmber),
            MakeSciFiMaterial("Cover_Vent", new Color(0.14f, 0.15f, 0.18f), NeonCyan, 1.2f, 0.7f, 2),
            MakeSciFiMaterial("Cover_Reactor", new Color(0.20f, 0.15f, 0.26f), NeonMagenta, 1.5f, 1.5f, 0),
        };
        var coverSpecs = new (Vector3 pos, Vector3 size, float yRot)[]
        {
            (new Vector3(-8, 0.75f, -6), new Vector3(3, 1.5f, 1.2f), 15f),
            (new Vector3(7, 0.75f, -8), new Vector3(2.5f, 1.5f, 1.2f), -20f),
            (new Vector3(-10, 1f, 4), new Vector3(1.5f, 2f, 1.5f), 0f),
            (new Vector3(11, 1f, 5), new Vector3(1.5f, 2f, 1.5f), 45f),
            (new Vector3(0, 0.75f, 0), new Vector3(4, 1.5f, 1.2f), 90f),
            (new Vector3(-4, 0.75f, 10), new Vector3(3, 1.5f, 1.2f), -30f),
            (new Vector3(5, 0.75f, 12), new Vector3(2.5f, 1.5f, 1.2f), 10f),
            (new Vector3(14, 0.6f, -3), new Vector3(2, 1.2f, 2), 0f),
            (new Vector3(-14, 0.6f, -1), new Vector3(2, 1.2f, 2), 0f),
            (new Vector3(2, 1.25f, -13), new Vector3(1.8f, 2.5f, 1.8f), 30f),
        };

        // Quadrupled cover density (user request): 30 extra blocks scattered on
        // a FIXED seed (deterministic rebuilds) join the 10 hand-placed ones.
        // Guards: spawn strips (|z| > 12) stay clear so teams never materialize
        // inside cover, and blocks keep clearance from each other and the
        // corner pillars so walkways survive.
        var allSpecs = new System.Collections.Generic.List<(Vector3 pos, Vector3 size, float yRot)>(coverSpecs);
        var rng = new System.Random(7261);
        int attempts = 0;
        while (allSpecs.Count < coverSpecs.Length * 4 && attempts++ < 600)
        {
            float NextRange(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

            var size = new Vector3(NextRange(1.4f, 3.4f), NextRange(1.2f, 2.4f), NextRange(1f, 1.9f));
            var pos = new Vector3(NextRange(-15f, 15f), size.y * 0.5f, NextRange(-11.5f, 11.5f));

            bool blocked = false;
            foreach (var existing in allSpecs)
            {
                Vector3 delta = existing.pos - pos;
                delta.y = 0f;
                if (delta.magnitude < 3f) { blocked = true; break; }
            }
            for (int cx = -1; cx <= 1 && !blocked; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(cx * 16f, cz * 16f)) < 2.6f)
                    { blocked = true; break; }
            if (blocked)
                continue;

            allSpecs.Add((pos, size, NextRange(-45f, 45f)));
        }

        for (int i = 0; i < allSpecs.Count; i++)
        {
            var (pos, size, yRot) = allSpecs[i];
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Cover";
            block.transform.SetParent(environment.transform);
            block.transform.position = pos;
            block.transform.rotation = Quaternion.Euler(0, yRot, 0);
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = coverMats[i % coverMats.Length];

            var obstacle = block.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;   // scaled by the transform to match the cube
            obstacle.carving = true;

            var ab = block.AddComponent<ArenaBlock>();
            ab.color = NeonMagenta;
            ab.maxHealth = 55f;
        }
    }

    static GameObject Box(GameObject parent, string name, Vector3 position, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void Trim(GameObject parent, Vector3 position, Vector3 size, Material mat)
    {
        var go = Box(parent, "Trim", position, size, mat);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static void BakeNavMesh(GameObject environment)
    {
        var surface = environment.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();
        Debug.Log("[ArenaBuilder] NavMesh baked.");
    }

    // ---------- Lighting & post ----------

    static void BuildLighting()
    {
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.75f, 0.8f, 1f);
        // Was 0.7, which left the robots reading as grey plastic. This key
        // travels toward +Z, so it lights the FAR side of whatever a camera is
        // pointed at; camera-facing armour was surviving on ambient alone.
        light.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        // Raised with the key. Flat ambient is the only thing reaching surfaces
        // the key misses, so it sets the floor the robots are read against.
        RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.42f);

        // Neon fills, as DIRECTIONAL lights rather than the corner points.
        //
        // The corner accents below are 22.6 units from mid-arena and fall off
        // with the square of distance, so at the centre they deliver about
        // 2.5/511 = 0.005 — nothing. Reaching that far would need roughly 150
        // intensity, which would blow the corners out completely. Directional
        // light does not attenuate, so a pair of dim coloured fills from
        // opposing sides puts the neon rim on every robot wherever it stands.
        //
        // Yaws are roughly opposite so a surface missed by one catches the
        // other, and both are angled down to sit under the key.
        DirectionalFill("Neon Fill Cyan", NeonCyan, 0.35f, new Vector3(25f, 200f, 0f));
        DirectionalFill("Neon Fill Magenta", NeonMagenta, 0.30f, new Vector3(25f, 20f, 0f));

        // Corner accent lights, kept for the glow they throw on the corners
        // themselves — that is all they were ever actually doing.
        PointLight(new Vector3(16, 3.5f, 16), NeonCyan);
        PointLight(new Vector3(-16, 3.5f, 16), NeonMagenta);
        PointLight(new Vector3(16, 3.5f, -16), NeonMagenta);
        PointLight(new Vector3(-16, 3.5f, -16), NeonCyan);
    }

    static void DirectionalFill(string name, Color color, float intensity, Vector3 euler)
    {
        var go = new GameObject(name);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;   // only the key casts shadows
        go.transform.rotation = Quaternion.Euler(euler);
    }

    static void PointLight(Vector3 position, Color color)
    {
        var go = new GameObject("AccentLight");
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 2.5f;
        light.range = 14f;
    }

    static void BuildPostProcessing()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, "Assets/Settings/PhotonArena_PostFX.asset");

        var bloom = profile.Add<Bloom>();
        bloom.active = true;
        bloom.intensity.Override(2.2f);
        bloom.threshold.Override(0.8f);
        bloom.scatter.Override(0.75f);

        var tonemapping = profile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.ACES);

        var vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.22f);

        // VolumeProfile.Add only creates in-memory components; without
        // registering them as sub-assets the profile serializes EMPTY and the
        // whole neon look silently dies on the next editor restart.
        AssetDatabase.AddObjectToAsset(bloom, profile);
        AssetDatabase.AddObjectToAsset(tonemapping, profile);
        AssetDatabase.AddObjectToAsset(vignette, profile);
        EditorUtility.SetDirty(profile);

        var volumeGo = new GameObject("Global Volume");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;
    }

    // ---------- Characters ----------

    static void BuildPlayer()
    {
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0, 0.1f, -15);

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.center = new Vector3(0, 0.9f, 0);
        controller.radius = 0.35f;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = new Vector3(0, 0.9f, 0);
        body.GetComponent<MeshRenderer>().sharedMaterial =
            MakeLitMaterial("PlayerBody", new Color(0.15f, 0.3f, 0.5f), NeonCyan, 1.2f);

        var head = new GameObject("Head");
        head.transform.SetParent(player.transform, false);
        head.transform.localPosition = new Vector3(0, 1.6f, 0);

        var cam = head.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        head.AddComponent<AudioListener>();
        var camData = head.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;

        // Blaster hangs off the head so it aims with the view.
        var blasterBodyMat = MakeLitMaterial("BlasterBody", new Color(0.1f, 0.1f, 0.14f), NeonCyan, 2f);
        var blasterGo = BuildBlaster(head.transform, new Vector3(0.32f, -0.30f, 0.35f), 0.5f,
            blasterBodyMat, out Transform muzzleT);

        // All 54 weapon components ride on the one viewmodel, but only the two
        // basics are unlocked — see AttachArsenal / BasicWeaponIndices.
        var playerWeapons = AttachArsenal(blasterGo, muzzleT, player.transform, NeonCyan, botTuning: false);

        var shield = player.AddComponent<EnergyShield>();
        shield.teamId = 0;

        var motor = player.AddComponent<CharacterMotor>();
        motor.head = head.transform;

        var scope = player.AddComponent<XRayScope>();

        var loadout = player.AddComponent<WeaponLoadout>();
        loadout.all = playerWeapons;
        loadout.basicIndices = BasicWeaponIndices;
        loadout.vehicleCannon = AttachVehicleCannon(blasterGo, muzzleT, player.transform, NeonCyan);

        var brain = player.AddComponent<PlayerBrain>();
        brain.weapons = new[] { playerWeapons[BasicWeaponIndices[0]], playerWeapons[BasicWeaponIndices[1]] };
        brain.scope = scope;

        var deRez = player.AddComponent<DeRezEffect>();
        deRez.body = body.transform;
        deRez.burstColor = NeonCyan;

        var vehicle = player.AddComponent<TransformMode>();
        player.AddComponent<VehicleRam>();   // driving through cover opens lanes
        vehicle.body = body.transform;
        vehicle.burstColor = NeonCyan;

        player.AddComponent<HudController>();

        var bubble = player.AddComponent<ShieldBubble>();
        bubble.color = NeonCyan;
    }

    /// <summary>
    /// Builds a blaster viewmodel under <paramref name="parent"/> using the
    /// Meshy laser-blaster model (falls back to a glowing cube), normalizes it
    /// to <paramref name="targetLength"/>, and returns its muzzle transform.
    /// Shared by the player and every bot so they all hold the real gun.
    /// </summary>
    static GameObject BuildBlaster(Transform parent, Vector3 localPos, float targetLength,
        Material cubeMat, out Transform muzzle)
    {
        var model = LoadModel("laser-blaster");
        if (model != null)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(model);
            go.name = "Blaster";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;

            var muzzleGo = new GameObject("Muzzle");
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                // Point the model's longest axis forward (+Z), then scale to length.
                var bounds = CombinedBounds(renderers);
                if (bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z)
                    go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                else if (bounds.size.y > bounds.size.z)
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                bounds = CombinedBounds(renderers);
                go.transform.localScale *= targetLength / Mathf.Max(0.01f, bounds.size.z);
                bounds = CombinedBounds(renderers);
                muzzleGo.transform.SetParent(go.transform, true);
                muzzleGo.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
            }
            else
            {
                muzzleGo.transform.SetParent(go.transform, false);
                muzzleGo.transform.localPosition = Vector3.forward * targetLength;
            }
            muzzle = muzzleGo.transform;
            return go;
        }

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Blaster";
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localScale = new Vector3(0.09f, 0.09f, targetLength * 0.85f);
        cube.GetComponent<MeshRenderer>().sharedMaterial = cubeMat;

        var m = new GameObject("Muzzle");
        m.transform.SetParent(cube.transform, false);
        m.transform.localPosition = new Vector3(0, 0, 0.6f);
        muzzle = m.transform;
        return cube;
    }

    /// <summary>Attach and configure a weapon component on a host object.</summary>
    static T AddWeapon<T>(GameObject host, Transform muzzle, Transform owner, Color color) where T : Weapon
    {
        var w = host.AddComponent<T>();
        w.muzzle = muzzle;
        w.ownerRoot = owner;
        w.color = color;
        return w;
    }

    enum WeaponKind { Laser, Beam, Plasma, Rail }

    /// <summary>
    /// Which slots of the canonical 54-weapon order every character starts
    /// with: the Laser Blaster (steady all-rounder) and the Plasma Lobber
    /// (slow arcing splash). The other 52 components are attached but locked —
    /// they only ever come out of a Weapon Pod airdrop. See WeaponLoadout.
    /// </summary>
    static readonly int[] BasicWeaponIndices = { 0, 2 };

    /// <summary>
    /// Attach all 54 weapon components to a blaster viewmodel in the canonical
    /// order (index i == debug console number i+1): the four originals first,
    /// then the expanded catalogue. <paramref name="botTuning"/> applies the
    /// gentler per-shot numbers bots fight with.
    /// </summary>
    /// <summary>
    /// The heavy gun a character uses while transformed. Attached alongside the
    /// arsenal but kept out of `all`, so it can never be rolled as a treasure
    /// weapon or fired on foot — it exists only for vehicle form.
    /// </summary>
    static VehicleCannon AttachVehicleCannon(GameObject host, Transform muzzle,
                                             Transform owner, Color color)
    {
        var cannon = host.AddComponent<VehicleCannon>();
        cannon.muzzle = muzzle;
        cannon.ownerRoot = owner;
        cannon.color = color;
        return cannon;
    }

    static Weapon[] AttachArsenal(GameObject host, Transform muzzle, Transform owner, Color color, bool botTuning)
    {
        var core = botTuning
            ? new[]
            {
                AddBotWeapon(host, WeaponKind.Laser, muzzle, owner, color),
                AddBotWeapon(host, WeaponKind.Beam, muzzle, owner, color),
                AddBotWeapon(host, WeaponKind.Plasma, muzzle, owner, color),
                AddBotWeapon(host, WeaponKind.Rail, muzzle, owner, color),
            }
            : new Weapon[]
            {
                AddWeapon<LaserBlaster>(host, muzzle, owner, color),
                AddWeapon<PhotonBeam>(host, muzzle, owner, color),
                AddWeapon<PlasmaLobber>(host, muzzle, owner, color),
                AddWeapon<RailZapper>(host, muzzle, owner, color),
            };

        var extras = WeaponCatalog.AttachAll(host, muzzle, owner);
        var all = new Weapon[core.Length + extras.Length];
        core.CopyTo(all, 0);
        extras.CopyTo(all, core.Length);
        return all;
    }

    static Weapon AddBotWeapon(GameObject host, WeaponKind kind, Transform muzzle, Transform owner, Color color)
    {
        switch (kind)
        {
            case WeaponKind.Beam:
            {
                var w = AddWeapon<PhotonBeam>(host, muzzle, owner, color);
                w.damagePerSecond = 34f;
                return w;
            }
            case WeaponKind.Plasma:
            {
                var w = AddWeapon<PlasmaLobber>(host, muzzle, owner, color);
                w.damage = 26f;
                w.shotsPerSecond = 0.9f;
                return w;
            }
            case WeaponKind.Rail:
            {
                var w = AddWeapon<RailZapper>(host, muzzle, owner, color);
                w.damage = 45f;
                w.chargeTime = 1.6f;
                return w;
            }
            default:
            {
                var w = AddWeapon<LaserBlaster>(host, muzzle, owner, color);
                w.damage = 12f;
                w.shotsPerSecond = 2.5f;
                return w;
            }
        }
    }

    // Target dummies removed 2026-07-25: with real bot teams fighting, the
    // team-1 dummies just distracted cyan bots mid-match (and confused the
    // "why three robot types?" read of the arena). TargetDummy the component
    // lives on: enemy bots carry it for player score attribution.

    /// <summary>
    /// Per-bot personality. With only two basic guns, what separates one robot
    /// from another on camera is how it plays the drops: how far it will detour
    /// for a crate, how twitchy it is, how well it shoots.
    /// </summary>
    struct BotPersona
    {
        public int favoriteBasic;    // 0 = Laser Blaster, 1 = Plasma Lobber
        public float greed;          // 0-1: appetite for chasing airdrops
        public float reactionDelay;
        public float aimError;
    }

    static void BuildBots()
    {
        var enemyArmor = MakeLitMaterial("BotArmor", new Color(0.32f, 0.12f, 0.30f));
        var enemyGlow = MakeLitMaterial("BotGlow", Color.black, NeonMagenta, 4f);
        var allyArmor = MakeLitMaterial("AllyArmor", new Color(0.12f, 0.24f, 0.34f));
        var allyGlow = MakeLitMaterial("AllyGlow", Color.black, NeonCyan, 4f);

        // Magenta enemies (team 1) at the north spawn, cyan allies (team 0)
        // flanking the player at the south spawn. Each team gets a hoarder, a
        // brawler and a middle-of-the-road bot so the scramble for a crate is
        // never three identical robots running the same line.
        BuildBotTeam("Bot", 1, NeonMagenta, enemyArmor, enemyGlow, 180f,
            new[] { new Vector3(-8, 0, 15), new Vector3(0, 0, 16), new Vector3(8, 0, 15) },
            new[]
            {
                new BotPersona { favoriteBasic = 1, greed = 0.45f, reactionDelay = 0.45f, aimError = 5.5f },
                new BotPersona { favoriteBasic = 0, greed = 0.95f, reactionDelay = 0.25f, aimError = 3.5f },
                new BotPersona { favoriteBasic = 1, greed = 0.70f, reactionDelay = 0.35f, aimError = 4.5f },
            });
        BuildBotTeam("Ally", 0, NeonCyan, allyArmor, allyGlow, 0f,
            new[] { new Vector3(-8, 0, -15), new Vector3(-4, 0, -16), new Vector3(8, 0, -15) },
            new[]
            {
                new BotPersona { favoriteBasic = 0, greed = 0.85f, reactionDelay = 0.30f, aimError = 4.0f },
                new BotPersona { favoriteBasic = 1, greed = 0.50f, reactionDelay = 0.40f, aimError = 5.0f },
                new BotPersona { favoriteBasic = 0, greed = 1.00f, reactionDelay = 0.28f, aimError = 4.5f },
            });
    }

    static void BuildBotTeam(string namePrefix, int teamId, Color teamColor,
        Material armor, Material glow, float yRotation, Vector3[] positions, BotPersona[] personas)
    {
        var darkMetal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DarkMetal.mat");
        var robotModel = LoadRobotModel(DefaultRobot);
        for (int i = 0; i < positions.Length; i++)
        {
            var bot = new GameObject($"{namePrefix}_{i + 1}");
            bot.transform.position = positions[i];
            bot.transform.rotation = Quaternion.Euler(0, yRotation, 0);

            var body = robotModel != null
                ? RobotFactory.BuildFromModel(bot, robotModel, teamColor, glow)
                : RobotFactory.Build(bot, armor, glow, darkMetal);

            var hitCapsule = bot.AddComponent<CapsuleCollider>();
            hitCapsule.center = new Vector3(0, 1f, 0);
            hitCapsule.height = 2f;
            hitCapsule.radius = 0.45f;

            var agent = bot.AddComponent<NavMeshAgent>();
            agent.speed = 4.5f;
            agent.acceleration = 12f;
            agent.angularSpeed = 240f;
            agent.radius = 0.4f;
            agent.height = 2f;

            // The real Meshy blaster, parented under the Body rig so it shrinks
            // away with the robot on de-rez.
            var cubeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlasterBody.mat");
            var blasterGo = BuildBlaster(body, new Vector3(0.35f, 0.15f, 0.35f), 0.5f, cubeMat, out Transform muzzleT);

            var shield = bot.AddComponent<EnergyShield>();
            shield.teamId = teamId;

            // Every bot carries all 54 weapon components, but the loadout only
            // unlocks the two basics — the rest have to be won off an airdrop.
            var botWeapons = AttachArsenal(blasterGo, muzzleT, bot.transform, teamColor, botTuning: true);

            var loadout = bot.AddComponent<WeaponLoadout>();
            loadout.all = botWeapons;
            loadout.basicIndices = BasicWeaponIndices;
            loadout.vehicleCannon = AttachVehicleCannon(blasterGo, muzzleT, bot.transform, teamColor);

            var persona = personas[i % personas.Length];
            var brain = bot.AddComponent<AIBrain>();
            // favoriteWeapon indexes the usable set (the two basics), not the
            // full arsenal — the loadout is what the brain actually reads.
            brain.weapons = new[]
            {
                botWeapons[BasicWeaponIndices[0]],
                botWeapons[BasicWeaponIndices[1]],
            };
            brain.favoriteWeapon = persona.favoriteBasic;
            brain.greed = persona.greed;
            brain.reactionDelay = persona.reactionDelay;
            brain.aimErrorDegrees = persona.aimError;

            var deRez = bot.AddComponent<DeRezEffect>();
            deRez.body = body;
            deRez.burstColor = teamColor;

            // VehicleSkin sits on Body, where the "Model" child it swaps lives;
            // TransformMode finds it with GetComponentInChildren.
            var skin = body.gameObject.AddComponent<VehicleSkin>();
            skin.holder = body;
            skin.tint = teamColor;
            skin.vehiclePrefab = LoadVehicleModel(DefaultRobot);

            var vehicle = bot.AddComponent<TransformMode>();
            bot.AddComponent<VehicleRam>();
            vehicle.body = body;
            vehicle.burstColor = teamColor;

            // Only enemy de-rezzes score points for the player.
            if (teamId != 0)
                bot.AddComponent<TargetDummy>();
            bot.AddComponent<HoverBob>();
            var bubble = bot.AddComponent<ShieldBubble>();
            bubble.color = teamColor;
        }
    }

    static void BuildGameController(GameObject environment)
    {
        var controller = new GameObject("GameController");
        controller.AddComponent<ArenaBlockManager>();
        controller.AddComponent<TreasureSpawner>();   // airdrops; driven by GameModeController
        controller.AddComponent<GameModeController>();
        BuildRobotRoster(controller);
    }

    /// <summary>
    /// Fills the RobotRoster from every *-robot model in Assets/Models so the
    /// runtime robot select screen can list them. DefaultRobot stays entry 0 —
    /// the scene's bots are built with it, so it must be the default pick.
    /// Rigged walkers (Assets/Models/Generated/*-robot.prefab, forged by
    /// MeshyWalkerForge and WalkerRigForge) join the same roster.
    /// </summary>
    static void BuildRobotRoster(GameObject controller)
    {
        var roster = controller.AddComponent<RobotRoster>();
        var files = new System.Collections.Generic.List<string>(
            System.IO.Directory.GetFiles("Assets/Models", "*-robot.glb"));
        if (System.IO.Directory.Exists("Assets/Models/Generated"))
            files.AddRange(System.IO.Directory.GetFiles("Assets/Models/Generated", "*-robot.prefab"));
        files.Sort(StringComparer.OrdinalIgnoreCase);

        var entries = new System.Collections.Generic.List<RobotRoster.Entry>();
        foreach (var raw in files)
        {
            string path = raw.Replace('\\', '/');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[ArenaBuilder] Robot model failed to load, skipping: {path}");
                continue;
            }
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            string robot = file.Replace("-robot", "");
            var entry = new RobotRoster.Entry
            {
                displayName = robot.ToUpperInvariant(),
                modelPrefab = prefab,
                // Optional: robots without a generated vehicle just fold.
                vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Models/Meshy/{robot}-vehicle.glb"),
                transformStages = LoadTransformStages(robot),
                transformVideo = AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(
                    $"Assets/Video/{robot}-transform.mp4"),
            };
            if (file == $"{DefaultRobot}-robot")
                entries.Insert(0, entry);
            else
                entries.Add(entry);
        }
        roster.robots = entries.ToArray();
        Debug.Log($"[ArenaBuilder] Robot roster: {entries.Count} robots.");
    }

    /// <summary>
    /// Transformation stages for a robot, in order, or an empty array.
    ///
    /// Sorted numerically rather than lexically: stage10 has to follow stage9,
    /// and a plain string sort puts it between stage1 and stage2.
    /// </summary>
    static GameObject[] LoadTransformStages(string robot)
    {
        string dir = $"Assets/Models/Stages/{robot}";
        if (!System.IO.Directory.Exists(dir))
            return new GameObject[0];

        var found = new System.Collections.Generic.List<(int order, GameObject model)>();
        foreach (var raw in System.IO.Directory.GetFiles(dir, "stage*.glb"))
        {
            string path = raw.Replace('\\', '/');
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning($"[ArenaBuilder] Stage failed to load, skipping: {path}");
                continue;
            }
            string digits = System.IO.Path.GetFileNameWithoutExtension(path).Substring("stage".Length);
            found.Add((int.TryParse(digits, out int order) ? order : int.MaxValue, model));
        }
        found.Sort((a, b) => a.order.CompareTo(b.order));
        return found.ConvertAll(f => f.model).ToArray();
    }
}

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
        RepairModelImports();
        SpriteForge.GenerateAll();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var environment = BuildEnvironment();
        BakeNavMesh(environment);
        BuildLighting();
        BuildPostProcessing();
        BuildPlayer();
        BuildDummies();
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
    /// Reimport any model whose cached import result is broken (e.g. imported
    /// while the render pipeline was in a transitional state).
    /// </summary>
    static void RepairModelImports()
    {
        if (!System.IO.Directory.Exists("Assets/Models"))
            return;
        foreach (var file in System.IO.Directory.GetFiles("Assets/Models", "*.glb"))
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

    // ---------- Imported models (Meshy) ----------

    static GameObject LoadModel(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.glb")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.fbx");
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

        var floorMat = MakeLitMaterial("Floor", new Color(0.12f, 0.13f, 0.18f));
        var wallMat = MakeLitMaterial("Wall", new Color(0.16f, 0.17f, 0.25f));
        var coverMat = MakeLitMaterial("Cover", new Color(0.22f, 0.2f, 0.35f));
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

        // Cover blocks — varied sizes, camera-friendly sight lines.
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
        foreach (var (pos, size, yRot) in coverSpecs)
        {
            var block = Box(root, "Cover", pos, size, coverMat);
            block.transform.rotation = Quaternion.Euler(0, yRot, 0);
        }

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
        light.intensity = 0.7f;
        lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.20f, 0.30f);

        // Corner accent lights for the neon mood.
        PointLight(new Vector3(16, 3.5f, 16), NeonCyan);
        PointLight(new Vector3(-16, 3.5f, 16), NeonMagenta);
        PointLight(new Vector3(16, 3.5f, -16), NeonMagenta);
        PointLight(new Vector3(-16, 3.5f, -16), NeonCyan);
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

        // Blaster hangs off the head so it aims with the view. Uses the Meshy
        // viewmodel when available, otherwise the greybox cube.
        var blasterBodyMat = MakeLitMaterial("BlasterBody", new Color(0.1f, 0.1f, 0.14f), NeonCyan, 2f);
        GameObject blasterGo;
        Transform muzzleT;
        var blasterModel = LoadModel("laser-blaster");
        if (blasterModel != null)
        {
            blasterGo = (GameObject)UnityEngine.Object.Instantiate(blasterModel);
            blasterGo.name = "Blaster";
            blasterGo.transform.SetParent(head.transform, false);
            blasterGo.transform.localPosition = new Vector3(0.32f, -0.30f, 0.35f);
            blasterGo.transform.localRotation = Quaternion.identity;

            var renderers = blasterGo.GetComponentsInChildren<Renderer>();
            var muzzleGo = new GameObject("Muzzle");
            if (renderers.Length > 0)
            {
                // Point the model's longest axis forward (+Z) — generated guns
                // often have the barrel along X.
                var bounds = CombinedBounds(renderers);
                if (bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z)
                    blasterGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                else if (bounds.size.y > bounds.size.z)
                    blasterGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Multiply, never replace — the glTF root may carry its own scale.
                bounds = CombinedBounds(renderers);
                blasterGo.transform.localScale *= 0.5f / Mathf.Max(0.01f, bounds.size.z);
                bounds = CombinedBounds(renderers);
                muzzleGo.transform.SetParent(blasterGo.transform, true);
                muzzleGo.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
            }
            else
            {
                muzzleGo.transform.SetParent(blasterGo.transform, false);
                muzzleGo.transform.localPosition = Vector3.forward * 0.5f;
            }
            muzzleT = muzzleGo.transform;
        }
        else
        {
            blasterGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blasterGo.name = "Blaster";
            UnityEngine.Object.DestroyImmediate(blasterGo.GetComponent<Collider>());
            blasterGo.transform.SetParent(head.transform, false);
            blasterGo.transform.localPosition = new Vector3(0.32f, -0.28f, 0.45f);
            blasterGo.transform.localScale = new Vector3(0.09f, 0.09f, 0.42f);
            blasterGo.GetComponent<MeshRenderer>().sharedMaterial = blasterBodyMat;

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(blasterGo.transform, false);
            muzzle.transform.localPosition = new Vector3(0, 0, 0.6f);
            muzzleT = muzzle.transform;
        }

        var blaster = blasterGo.AddComponent<LaserBlaster>();
        blaster.muzzle = muzzleT;
        blaster.ownerRoot = player.transform;
        blaster.boltColor = NeonCyan;

        var shield = player.AddComponent<EnergyShield>();
        shield.teamId = 0;

        var motor = player.AddComponent<CharacterMotor>();
        motor.head = head.transform;

        var brain = player.AddComponent<PlayerBrain>();
        brain.blaster = blaster;

        var deRez = player.AddComponent<DeRezEffect>();
        deRez.body = body.transform;
        deRez.burstColor = NeonCyan;

        player.AddComponent<HudController>();

        var bubble = player.AddComponent<ShieldBubble>();
        bubble.color = NeonCyan;
    }

    static void BuildDummies()
    {
        var armor = MakeLitMaterial("DummyArmor", new Color(0.45f, 0.28f, 0.12f));
        var glow = MakeLitMaterial("DummyGlow", Color.black, NeonOrange, 4f);
        var darkMetal = MakeLitMaterial("DarkMetal", new Color(0.10f, 0.10f, 0.13f));
        var positions = new[]
        {
            new Vector3(-6, 0, 8), new Vector3(6, 0, 9),
            new Vector3(-12, 0, -4), new Vector3(13, 0, 2),
        };

        var robotModel = LoadModel("hover-robot");
        foreach (var pos in positions)
        {
            var dummy = new GameObject("TargetDummy");
            dummy.transform.position = pos;

            var body = robotModel != null
                ? RobotFactory.BuildFromModel(dummy, robotModel, NeonOrange, glow)
                : RobotFactory.Build(dummy, armor, glow, darkMetal);

            var hitCapsule = dummy.AddComponent<CapsuleCollider>();
            hitCapsule.center = new Vector3(0, 1f, 0);
            hitCapsule.height = 2f;
            hitCapsule.radius = 0.45f;

            var shield = dummy.AddComponent<EnergyShield>();
            shield.teamId = 1;
            shield.regenDelay = 2f;

            var deRez = dummy.AddComponent<DeRezEffect>();
            deRez.body = body;
            deRez.burstColor = NeonOrange;
            deRez.respawnDelay = 2.5f;

            dummy.AddComponent<TargetDummy>();
            dummy.AddComponent<HoverBob>();
            var bubble = dummy.AddComponent<ShieldBubble>();
            bubble.color = NeonOrange;
        }
    }

    static void BuildBots()
    {
        var enemyArmor = MakeLitMaterial("BotArmor", new Color(0.32f, 0.12f, 0.30f));
        var enemyGlow = MakeLitMaterial("BotGlow", Color.black, NeonMagenta, 4f);
        var allyArmor = MakeLitMaterial("AllyArmor", new Color(0.12f, 0.24f, 0.34f));
        var allyGlow = MakeLitMaterial("AllyGlow", Color.black, NeonCyan, 4f);

        // Magenta enemies (team 1) at the north spawn, cyan allies (team 0)
        // flanking the player at the south spawn.
        BuildBotTeam("Bot", 1, NeonMagenta, enemyArmor, enemyGlow, 180f,
            new[] { new Vector3(-8, 0, 15), new Vector3(0, 0, 16), new Vector3(8, 0, 15) });
        BuildBotTeam("Ally", 0, NeonCyan, allyArmor, allyGlow, 0f,
            new[] { new Vector3(-8, 0, -15), new Vector3(-4, 0, -16), new Vector3(8, 0, -15) });
    }

    static void BuildBotTeam(string namePrefix, int teamId, Color teamColor,
        Material armor, Material glow, float yRotation, Vector3[] positions)
    {
        var darkMetal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DarkMetal.mat");
        var robotModel = LoadModel("hover-robot");
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

            var blasterGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blasterGo.name = "Blaster";
            UnityEngine.Object.DestroyImmediate(blasterGo.GetComponent<Collider>());
            // Under the Body rig so the gun shrinks away with the robot on de-rez.
            blasterGo.transform.SetParent(body, false);
            blasterGo.transform.localPosition = new Vector3(0.3f, 0.3f, 0.4f);
            blasterGo.transform.localScale = new Vector3(0.09f, 0.09f, 0.42f);
            blasterGo.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlasterBody.mat");

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(blasterGo.transform, false);
            muzzle.transform.localPosition = new Vector3(0, 0, 0.6f);

            var shield = bot.AddComponent<EnergyShield>();
            shield.teamId = teamId;

            var blaster = blasterGo.AddComponent<LaserBlaster>();
            blaster.muzzle = muzzle.transform;
            blaster.ownerRoot = bot.transform;
            blaster.boltColor = teamColor;
            blaster.shotsPerSecond = 2.5f;
            blaster.boltDamage = 12f;

            var brain = bot.AddComponent<AIBrain>();
            brain.blaster = blaster;

            var deRez = bot.AddComponent<DeRezEffect>();
            deRez.body = body;
            deRez.burstColor = teamColor;

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
        var randomizer = controller.AddComponent<ArenaRandomizer>();
        randomizer.environmentRoot = environment.transform;
        randomizer.coverMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Cover.mat");
        controller.AddComponent<GameModeController>();
    }
}

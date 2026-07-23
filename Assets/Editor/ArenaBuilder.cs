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
        ConfigureUrp();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var environment = BuildEnvironment();
        BakeNavMesh(environment);
        BuildLighting();
        BuildPostProcessing();
        BuildPlayer();
        BuildDummies();
        BuildBots();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[ArenaBuilder] Scene saved to {ScenePath}");
    }

    // ---------- URP ----------

    static void ConfigureUrp()
    {
        System.IO.Directory.CreateDirectory("Assets/Settings");

        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/PhotonArena_Renderer.asset");

        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        pipeline.supportsHDR = true;   // HDR emissives feed bloom — the whole neon look
        AssetDatabase.CreateAsset(pipeline, "Assets/Settings/PhotonArena_URP.asset");

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        Debug.Log("[ArenaBuilder] URP configured.");
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
        RenderSettings.ambientLight = new Color(0.12f, 0.14f, 0.22f);

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
        bloom.intensity.Override(1.6f);
        bloom.threshold.Override(0.9f);

        var tonemapping = profile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.ACES);

        var vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.22f);

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
        var blasterGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blasterGo.name = "Blaster";
        UnityEngine.Object.DestroyImmediate(blasterGo.GetComponent<Collider>());
        blasterGo.transform.SetParent(head.transform, false);
        blasterGo.transform.localPosition = new Vector3(0.32f, -0.28f, 0.45f);
        blasterGo.transform.localScale = new Vector3(0.09f, 0.09f, 0.42f);
        blasterGo.GetComponent<MeshRenderer>().sharedMaterial =
            MakeLitMaterial("BlasterBody", new Color(0.1f, 0.1f, 0.14f), NeonCyan, 2f);

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(blasterGo.transform, false);
        muzzle.transform.localPosition = new Vector3(0, 0, 0.6f);

        var blaster = blasterGo.AddComponent<LaserBlaster>();
        blaster.muzzle = muzzle.transform;
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
    }

    static void BuildDummies()
    {
        var dummyMat = MakeLitMaterial("Dummy", new Color(0.5f, 0.25f, 0.1f), NeonOrange, 2f);
        var positions = new[]
        {
            new Vector3(-6, 0, 8), new Vector3(6, 0, 9),
            new Vector3(-12, 0, -4), new Vector3(13, 0, 2),
        };

        foreach (var pos in positions)
        {
            var dummy = new GameObject("TargetDummy");
            dummy.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(dummy.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.GetComponent<MeshRenderer>().sharedMaterial = dummyMat;

            var shield = dummy.AddComponent<EnergyShield>();
            shield.teamId = 1;
            shield.regenDelay = 2f;

            var deRez = dummy.AddComponent<DeRezEffect>();
            deRez.body = body.transform;
            deRez.burstColor = NeonOrange;
            deRez.respawnDelay = 2.5f;

            dummy.AddComponent<TargetDummy>();
        }
    }

    static void BuildBots()
    {
        var botMat = MakeLitMaterial("Bot", new Color(0.45f, 0.12f, 0.4f), NeonMagenta, 1.5f);
        var positions = new[]
        {
            new Vector3(-8, 0, 15), new Vector3(0, 0, 16), new Vector3(8, 0, 15),
        };

        for (int i = 0; i < positions.Length; i++)
        {
            var bot = new GameObject($"Bot_{i + 1}");
            bot.transform.position = positions[i];
            bot.transform.rotation = Quaternion.Euler(0, 180f, 0);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(bot.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.GetComponent<MeshRenderer>().sharedMaterial = botMat;

            var agent = bot.AddComponent<NavMeshAgent>();
            agent.speed = 4.5f;
            agent.acceleration = 12f;
            agent.angularSpeed = 240f;
            agent.radius = 0.4f;
            agent.height = 2f;

            var blasterGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blasterGo.name = "Blaster";
            UnityEngine.Object.DestroyImmediate(blasterGo.GetComponent<Collider>());
            blasterGo.transform.SetParent(bot.transform, false);
            blasterGo.transform.localPosition = new Vector3(0.3f, 1.3f, 0.4f);
            blasterGo.transform.localScale = new Vector3(0.09f, 0.09f, 0.42f);
            blasterGo.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlasterBody.mat");

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(blasterGo.transform, false);
            muzzle.transform.localPosition = new Vector3(0, 0, 0.6f);

            var shield = bot.AddComponent<EnergyShield>();
            shield.teamId = 1;

            var blaster = blasterGo.AddComponent<LaserBlaster>();
            blaster.muzzle = muzzle.transform;
            blaster.ownerRoot = bot.transform;
            blaster.boltColor = NeonMagenta;
            blaster.shotsPerSecond = 2.5f;
            blaster.boltDamage = 12f;

            var brain = bot.AddComponent<AIBrain>();
            brain.blaster = blaster;

            var deRez = bot.AddComponent<DeRezEffect>();
            deRez.body = body.transform;
            deRez.burstColor = NeonMagenta;

            bot.AddComponent<TargetDummy>();
        }
    }
}

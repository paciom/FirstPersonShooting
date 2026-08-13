using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Self-portrait rig for the VFX: builds an isolated stage far from the arena,
/// plays each contested effect in front of a bloom-matched camera, and saves
/// PNG frames to Renders/vfxproof so the results can be inspected off-screen —
/// the whiteout hunt burned five rounds on screenshots relayed by hand.
///
/// Activated by a Temp/vfxproof_request.txt marker (or the -vfxproof command
/// line arg) and completely inert otherwise. With the marker present, PRESS
/// PLAY: the rig hijacks the session, shoots its sequence in about seven
/// seconds, writes the frames, and exits play mode by itself.
/// </summary>
public static class VfxProof
{
    public const string Marker = "Temp/vfxproof_request.txt";
    public const string OutDir = "Renders/vfxproof";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        bool wanted = File.Exists(Marker)
            || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vfxproof") >= 0;
        if (!wanted)
            return;
        new GameObject("VfxProof").AddComponent<VfxProofDriver>();
    }
}

class VfxProofDriver : MonoBehaviour
{
    static readonly Vector3 Focus = new Vector3(400f, 1.2f, 400f);

    Camera _cam;
    float _t;
    int _next;
    float _nextSplash = float.MaxValue;
    GenericBolt _comet;
    List<(float at, string name, System.Action act)> _events;

    void Start()
    {
        Directory.CreateDirectory(VfxProof.OutDir);
        foreach (var stale in Directory.GetFiles(VfxProof.OutDir, "*.png"))
            File.Delete(stale);

        BuildStage();

        // A comet in flight, the pack's fireball ALONE, the full wreck, and a
        // stacked splash barrage — plus a dump of what materials the fireball
        // actually spawned with, because the calming pipeline believes one
        // thing and the frames show another.
        _events = new List<(float, string, System.Action)>
        {
            (0.1f, null, DumpFireballMaterials),
            (0.6f, null, SpawnComet),
            (1.2f, "comet_a", null),
            (1.7f, "comet_b", null),
            (2.2f, null, () =>
            {
                if (_comet != null) Destroy(_comet.gameObject);
                WarFx.Spawn(WarFx.Kind.Big, Focus, 1.3f);
            }),
            (2.4f, "fireball_a", null),
            (2.9f, "fireball_b", null),
            (3.8f, null, () => VfxUtil.Explosion(Focus, new Color(1f, 0.25f, 0.85f), 1.3f)),
            (3.95f, "wreck_a", null),
            (4.4f, "wreck_b", null),
            (5.2f, null, () => _nextSplash = 0f),
            (6.2f, "splash_a", null),
            (6.65f, "splash_b", null),
            (7.4f, null, Finish),
        };
    }

    /// <summary>Every renderer on a sanitized fireball instance: object,
    /// material, shader, tint. The ground truth the theorising lacked.</summary>
    void DumpFireballMaterials()
    {
        var probe = WarFx.Spawn(WarFx.Kind.Big, Focus + Vector3.up * 60f, 1.3f);
        if (probe == null)
        {
            File.WriteAllText(Path.Combine(VfxProof.OutDir, "materials.txt"), "SPAWN FAILED");
            return;
        }
        var sb = new System.Text.StringBuilder();
        foreach (var renderer in probe.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                sb.AppendLine(material == null
                    ? $"{renderer.gameObject.name} | NULL"
                    : $"{renderer.gameObject.name} | {material.name} | {material.shader?.name} | "
                      + (material.HasProperty("_TintColor")
                          ? material.GetColor("_TintColor").ToString() : "no tint"));
        File.WriteAllText(Path.Combine(VfxProof.OutDir, "materials.txt"), sb.ToString());
        Destroy(probe);
    }

    /// <summary>A deck, two suns, the game's own post profile, and a camera at
    /// the tank mode's pitch — far from the arena so the menu never shows.</summary>
    void BuildStage()
    {
        var deck = GameObject.CreatePrimitive(PrimitiveType.Plane);
        deck.name = "ProofDeck";
        deck.transform.position = new Vector3(Focus.x, 0f, Focus.z);
        deck.transform.localScale = Vector3.one * 6f;
        deck.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("vfxproof-deck", new Color(0.10f, 0.13f, 0.16f), 0.3f);

        Sun(new Vector3(46f, 24f, 0f), 1.2f, new Color(1f, 0.96f, 0.9f));
        Sun(new Vector3(24f, 200f, 0f), 0.4f, new Color(0.5f, 0.7f, 1f));

        var rig = new GameObject("ProofCamera");
        rig.transform.position = Focus + new Vector3(0f, 11f, -9f);
        rig.transform.LookAt(Focus);
        _cam = rig.AddComponent<Camera>();
        _cam.fieldOfView = 52f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.03f, 0.05f, 0.1f);
        _cam.depth = 999f;
        var data = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;

#if UNITY_EDITOR
        // The game's own bloom, so the proof shows what the player sees.
        var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(
            "Assets/Settings/PhotonArena_PostFX.asset");
        if (profile != null)
        {
            var volume = gameObject.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 999f;
            volume.sharedProfile = profile;
        }
#endif

        foreach (var cam in Camera.allCameras)
            if (cam != _cam)
                cam.enabled = false;
    }

    void Sun(Vector3 euler, float intensity, Color color)
    {
        var go = new GameObject("ProofSun");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
    }

    void SpawnComet()
    {
        // CometSling's spec, verbatim — the bolt under suspicion.
        var spec = new BoltSpec
        {
            speed = 13f,
            damage = 0f,
            color = new Color(1f, 0.5f, 0.2f),
            shape = PrimitiveType.Sphere,
            size = 0.8f,
            glow = 1.8f,
            trailTime = 0.3f,
            trailWidth = 0.16f,
            trailGlow = 1.3f,
            splashRadius = 4.5f,
            splashScale = 1.7f,
            lifetime = 6f,
        };
        _comet = GenericBolt.Spawn(Focus + new Vector3(-7f, 0.3f, 3f), Vector3.right, spec, 1,
            transform);
        WeaponUtil.DressAsFireball(_comet, spec.color);
    }

    void Update()
    {
        _t += Time.deltaTime;

        if (_t >= _nextSplash && _t < 6.7f)
        {
            _nextSplash = _t + 0.15f;
            VfxUtil.SplashPop(Focus + new Vector3(Random.Range(-0.5f, 0.5f), 0f,
                Random.Range(-0.5f, 0.5f)), new Color(1f, 0.5f, 0.2f), 1.7f);
        }

        while (_next < _events.Count && _t >= _events[_next].at)
        {
            var e = _events[_next++];
            if (e.name != null)
            {
                // Overlay canvases draw over every camera; anything the menu
                // put up since the last frame goes dark before the shot.
                foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    canvas.enabled = false;
                ScreenCapture.CaptureScreenshot(Path.Combine(VfxProof.OutDir, e.name + ".png"));
            }
            e.act?.Invoke();
        }
    }

    void Finish()
    {
        File.WriteAllText(Path.Combine(VfxProof.OutDir, "DONE.txt"), "ok");
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

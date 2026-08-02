using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Live 3D portraits for the intel panels: each requested unit or structure
/// gets a tiny stage — the actual model, slowly turning under its own point
/// light, filmed by a 160px RenderTexture camera. Same trick the robot
/// select screen established: stages parked far below and beside the world
/// (nothing else within the camera's 12 m far plane), plus a local light,
/// because the battlefield's key light was never aimed at any of this.
///
/// Rigs are cached per (kind, key, team) and built on demand; a rig nobody
/// has asked about for a second switches itself off so idle cameras cost
/// nothing. Everything lives under this component, so the whole studio is
/// struck when the session ends — except the RenderTextures, which Unity
/// will not destroy with a hierarchy and are released in OnDestroy.
/// </summary>
public class CommanderPortraits : MonoBehaviour
{
    const float StageY = -150f;
    const float StageX = 400f;
    const float StageSpacing = 14f;
    const float IdleOff = 1f;

    class Rig
    {
        public GameObject root;
        public RenderTexture texture;
        public float lastUsed;
    }

    readonly Dictionary<string, Rig> _rigs = new Dictionary<string, Rig>();
    Transform _stage;
    int _slot;

    class Spinner : MonoBehaviour
    {
        void Update() => transform.Rotate(0f, 36f * Time.unscaledDeltaTime, 0f);
    }

    void Awake()
    {
        _stage = new GameObject("PortraitStage").transform;
        _stage.SetParent(transform, false);
        _stage.position = new Vector3(StageX, StageY, 0f);
    }

    /// <summary>
    /// A recompile during Play wipes the rig dictionary (not serializable)
    /// but leaves the rig GameObjects standing — and the slot counter resets,
    /// so rebuilt rigs would stack new models on top of the orphans, two
    /// robots ghosting through each other in every portrait. OnEnable re-runs
    /// after the reload (Awake does not): strike the whole stage and let the
    /// next refresh rebuild clean.
    /// </summary>
    void OnEnable()
    {
        if (_stage == null || _rigs.Count == _stage.childCount)
            return;
        for (int i = _stage.childCount - 1; i >= 0; i--)
            Destroy(_stage.GetChild(i).gameObject);
        _rigs.Clear();
        _slot = 0;
    }

    void Update()
    {
        foreach (var rig in _rigs.Values)
            if (rig.root != null && rig.root.activeSelf
                && Time.unscaledTime - rig.lastUsed > IdleOff)
                rig.root.SetActive(false);
    }

    void OnDestroy()
    {
        foreach (var rig in _rigs.Values)
        {
            if (rig.texture != null)
            {
                rig.texture.Release();
                Destroy(rig.texture);
            }
        }
        _rigs.Clear();
    }

    public Texture UnitPortrait(string unitKey, int teamId) =>
        Get($"u_{unitKey}_{teamId}", () => UnitModel(unitKey, teamId));

    public Texture BuildingPortrait(string buildingKey, int teamId) =>
        Get($"b_{buildingKey}_{teamId}", () => BuildingModel(buildingKey, teamId));

    Texture Get(string key, System.Func<GameObject> maker)
    {
        if (!_rigs.TryGetValue(key, out var rig) || rig.root == null)
        {
            rig = Build(maker);
            if (rig == null)
                return null;
            _rigs[key] = rig;
        }
        rig.lastUsed = Time.unscaledTime;
        if (!rig.root.activeSelf)
            rig.root.SetActive(true);
        return rig.texture;
    }

    Rig Build(System.Func<GameObject> maker)
    {
        var model = maker();
        if (model == null)
            return null;

        var root = new GameObject("Portrait");
        root.transform.SetParent(_stage, false);
        root.transform.localPosition = new Vector3(_slot++ * StageSpacing, 0f, 0f);
        model.transform.SetParent(root.transform, false);
        model.AddComponent<Spinner>();

        // The one light this stage gets — small range so neighbouring stages
        // stay on their own lighting.
        var lightGo = new GameObject("Key");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(1.3f, 2.2f, -1.6f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = 2.4f;
        light.range = 6.5f;

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(root.transform, false);
        // Slightly above and pulled back, tilted down — robots read at eye
        // level, buildings show their roofs, both fit one framing.
        camGo.transform.localPosition = new Vector3(0f, 1.5f, -2.5f);
        camGo.transform.LookAt(root.transform.position + Vector3.up * 0.7f);
        var cam = camGo.AddComponent<Camera>();
        // NOT tagged MainCamera — Camera.main must keep meaning the RTS view.
        cam.fieldOfView = 36f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 12f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 1f);
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;

        var texture = new RenderTexture(160, 160, 16);
        cam.targetTexture = texture;

        return new Rig { root = root, texture = texture, lastUsed = Time.unscaledTime };
    }

    /// <summary>The unit's roster robot, team-painted — or the capsule stand-in.</summary>
    GameObject UnitModel(string unitKey, int teamId)
    {
        var def = UnitCatalog.Get(unitKey);
        if (def == null)
            return null;
        Color tint = MatchAnnouncer.TeamColor(teamId);

        var body = new GameObject("Model");
        var prefab = UnitCatalog.Model(FindFirstObjectByType<RobotRoster>(), def.robotName);
        if (prefab != null)
        {
            RobotFactory.InstantiateNormalized(prefab, body.transform, tint);
        }
        else
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(body.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            capsule.transform.localScale = new Vector3(0.7f, 0.8f, 0.7f);
            capsule.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Lit($"Cmd_Unit{teamId}", tint * 0.6f);
        }
        return body;
    }

    /// <summary>
    /// The structure's Meshy model scaled into portrait frame — or, missing
    /// that, a block in the building's accent colour, which is exactly what
    /// stands on the battlefield in that case too.
    /// </summary>
    GameObject BuildingModel(string buildingKey, int teamId)
    {
        var def = BuildingCatalog.Get(buildingKey);
        if (def == null)
            return null;

        var holder = new GameObject("Model");
        var prefab = Resources.Load<GameObject>($"Buildings/{buildingKey}-building");
        if (prefab != null)
        {
            var instance = Instantiate(prefab, holder.transform);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                float scale = 1.5f / Mathf.Max(0.01f, Mathf.Max(bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z)));
                instance.transform.localScale *= scale;
                Vector3 center = holder.transform.InverseTransformPoint(bounds.center);
                Vector3 bottom = holder.transform.InverseTransformPoint(
                    new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                instance.transform.localPosition = new Vector3(
                    -center.x * scale, -bottom.y * scale, -center.z * scale);
            }
        }
        else
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(block.GetComponent<Collider>());
            block.transform.SetParent(holder.transform, false);
            float height = Mathf.Min(1.4f, def.height * 0.28f);
            block.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            block.transform.localScale = new Vector3(1.2f, height, 1.2f);
            block.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Emissive($"Cmd_BldAccent_{def.key}", def.accent, 1.6f);
        }
        return holder;
    }
}

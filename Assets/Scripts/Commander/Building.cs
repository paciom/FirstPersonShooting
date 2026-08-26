using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A placed structure: a shielded block that materializes out of the ground,
/// carves the NavMesh so traffic flows around it, and de-rezzes for good when
/// its shield breaks.
///
/// Buildings live at the SCENE ROOT for the same reason units do — weapon
/// projectiles resolve their victim via transform.root, so anything parented
/// deeper is unhittable. The static registry plus <see cref="DespawnAll"/>
/// stand in for a parent object at teardown.
///
/// Visuals are deliberate block placeholders (see COMMANDER_PLAN.md: blocks
/// first, Meshy after the footprints stop moving). The silhouette carries the
/// identity: footprint, height, accent trim, team stripe.
/// </summary>
public class Building : MonoBehaviour
{
    public static readonly List<Building> All = new List<Building>();

    /// <summary>Raised after a building's de-rez completes. Phase 5's win check listens.</summary>
    public static event System.Action<Building> OnBuildingLost;

    [SerializeField] string _defKey;
    [SerializeField] int _teamId;

    EnergyShield _shield;
    Transform _body;
    bool _dying;
    BuildingDefinition _definition;

    public int TeamId => _teamId;
    public bool IsAlive => !_dying && _shield != null && !_shield.IsDown;

    /// <summary>
    /// Re-derived from the key on demand: BuildingDefinition is a plain C#
    /// class, so the reference itself does not survive a recompile during
    /// Play — the string key (serialized) does.
    /// </summary>
    public BuildingDefinition Definition =>
        _definition ?? (_definition = BuildingCatalog.Get(_defKey));

    // ------------------------------------------------------------- factory

    public static Building Construct(BuildingDefinition def, int teamId, Vector3 center)
    {
        Color tint = MatchAnnouncer.TeamColor(teamId);

        var root = new GameObject($"CmdBuilding_{def.key}{teamId}");
        root.transform.position = center;

        var shield = root.AddComponent<EnergyShield>();
        shield.teamId = teamId;
        shield.maxShield = def.maxShield;
        // Buildings heal slowly — a raid that half-kills a factory should
        // still mean something two minutes later.
        shield.regenDelay = 8f;
        shield.regenPerSecond = 6f;
        shield.Rematerialize();   // resync Current after the change, as ever

        var body = BuildBlockModel(root.transform, def, tint);

        // One collider for the bolts, matching the block.
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, def.height * 0.5f, 0f);
        collider.size = new Vector3(def.footprint.x, def.height, def.footprint.y);

        // Carve rather than re-bake: same trick as the arena's cover blocks.
        // Units flow around the building the moment it lands.
        var obstacle = root.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = collider.center;
        obstacle.size = collider.size;
        obstacle.carving = true;

        var building = root.AddComponent<Building>();
        building._defKey = def.key;
        building._teamId = teamId;
        building._definition = def;
        building._body = body;
        building.StartCoroutine(building.MaterializeRoutine());

        // Structures with behaviour get it here, keyed off the catalog —
        // the placer and the AI commander build through this one door.
        if (def.key == BuildingCatalog.Turret)
            root.AddComponent<BuildingTurret>();
        if (def.key == BuildingCatalog.Factory)
            root.AddComponent<ProductionQueue>();
        if (def.key == BuildingCatalog.Missiles)
            root.AddComponent<BuildingMissiles>();
        // The airbase needs no component: its whole behaviour is existing,
        // which CommanderAir counts.

        CommanderOps.Log(teamId, $"+ {def.displayName}");
        return building;
    }

    /// <summary>
    /// The structure's visual: the Meshy model when one is in Resources,
    /// otherwise the block placeholder — a panelled block, a darker roof
    /// cap, an accent trim ring and a team stripe. The fallback is permanent
    /// on purpose: a missing or failed model can never break the mode.
    /// </summary>
    static Transform BuildBlockModel(Transform root, BuildingDefinition def, Color tint)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(root, false);

        // Resources rather than a scene-serialized roster: Commander owns no
        // scene data, and Resources.Load works identically in editor Play
        // and the WebGL player with zero ArenaBuilder involvement.
        var modelPrefab = Resources.Load<GameObject>(
            $"Buildings/{def.modelKey ?? def.key}-building");
        if (modelPrefab != null)
        {
            BuildFromMeshyModel(body, modelPrefab, def, tint);
            return body;
        }

        var wall = ArenaMaterials.Style($"Cmd_Bld_{def.key}", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.16f, 0.19f, 0.24f), new Color(0.09f, 0.11f, 0.15f), 1.4f, 0.6f);
        var roof = ArenaMaterials.Lit("Cmd_BldRoof", new Color(0.10f, 0.12f, 0.16f), 0.35f);
        var accent = ArenaMaterials.Emissive($"Cmd_BldAccent_{def.key}", def.accent, 1.6f);
        var team = ArenaMaterials.Emissive($"Cmd_BldTeam{(tint.r > tint.b ? 1 : 0)}", tint, 1.6f);

        float w = def.footprint.x, d = def.footprint.y, h = def.height;

        Block(body, "Walls", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), wall);
        Block(body, "Roof", new Vector3(0f, h + 0.12f, 0f), new Vector3(w * 0.82f, 0.24f, d * 0.82f), roof);

        // Accent ring at 80% height, proud of the walls on all four sides.
        float ringY = h * 0.8f;
        Block(body, "Trim_N", new Vector3(0f, ringY, d * 0.5f + 0.06f), new Vector3(w * 0.9f, 0.28f, 0.1f), accent);
        Block(body, "Trim_S", new Vector3(0f, ringY, -d * 0.5f - 0.06f), new Vector3(w * 0.9f, 0.28f, 0.1f), accent);
        Block(body, "Trim_E", new Vector3(w * 0.5f + 0.06f, ringY, 0f), new Vector3(0.1f, 0.28f, d * 0.9f), accent);
        Block(body, "Trim_W", new Vector3(-w * 0.5f - 0.06f, ringY, 0f), new Vector3(0.1f, 0.28f, d * 0.9f), accent);

        // Team stripe: one bright vertical on the south face (the camera-facing
        // side for cyan, whose base is at the bottom of the screen).
        Block(body, "TeamStripe", new Vector3(0f, h * 0.45f, -d * 0.5f - 0.06f),
            new Vector3(0.35f, h * 0.7f, 0.1f), team);

        return body;
    }

    /// <summary>
    /// Fit the generated model into the footprint the whole game was
    /// balanced around: uniform scale to the tightest of the three axis
    /// ratios (MULTIPLIED into the prefab's own scale — glTF roots carry
    /// unit-conversion factors that must survive), grounded at the root, and
    /// ringed in team colour, since the Meshy texture carries the building's
    /// identity but not its allegiance.
    /// </summary>
    static void BuildFromMeshyModel(Transform body, GameObject prefab, BuildingDefinition def,
        Color tint)
    {
        var instance = Object.Instantiate(prefab, body);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            float scale = Mathf.Min(
                def.footprint.x / Mathf.Max(0.01f, bounds.size.x),
                def.height / Mathf.Max(0.01f, bounds.size.y),
                def.footprint.y / Mathf.Max(0.01f, bounds.size.z));
            instance.transform.localScale *= scale;

            // Centre on the root in XZ, feet on the ground in Y — the root
            // IS ground level for buildings.
            Vector3 localCenter = body.InverseTransformPoint(bounds.center);
            Vector3 localBottom = body.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            instance.transform.localPosition = new Vector3(
                -localCenter.x * scale, -localBottom.y * scale, -localCenter.z * scale);
        }

        float ringSize = Mathf.Max(def.footprint.x, def.footprint.y) + 2.5f;
        CommanderUnit.GlowQuad(body, "TeamRing", "VFX/ring", tint, 1.4f, ringSize, 0.05f);
    }

    /// <summary>A renderer-only block — the root's BoxCollider is the hitbox.</summary>
    static void Block(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ------------------------------------------------------------- lifecycle

    void OnEnable()
    {
        if (_dying)
        {
            Destroy(gameObject);   // reload resurrected a mid-collapse ruin
            return;
        }
        if (!All.Contains(this))
            All.Add(this);
        _shield = GetComponent<EnergyShield>();
        if (_body == null)
            _body = transform.Find("Body");
        if (_shield != null)
        {
            _shield.OnDeRezzed -= HandleDeRez;
            _shield.OnDeRezzed += HandleDeRez;
        }
    }

    void OnDisable()
    {
        All.Remove(this);
        if (_shield != null)
            _shield.OnDeRezzed -= HandleDeRez;
    }

    void Update()
    {
        // Same reload self-heal as the units: down but not dying means the
        // de-rez event fired into a severed delegate.
        if (!_dying && _shield != null && _shield.IsDown)
            HandleDeRez();
    }

    // ------------------------------------------------------------- effects

    /// <summary>Rise out of the ground in under a second — construction as re-materialization.</summary>
    IEnumerator MaterializeRoutine()
    {
        VfxUtil.Explosion(transform.position + Vector3.up * 0.5f,
            Definition != null ? Definition.accent : Color.white, 0.9f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
        {
            float rise = Mathf.SmoothStep(0.04f, 1f, t);
            if (_body != null)
                _body.localScale = new Vector3(1f, rise, 1f);
            yield return null;
        }
        if (_body != null)
            _body.localScale = Vector3.one;
    }

    void HandleDeRez()
    {
        if (!_dying)
            StartCoroutine(CollapseRoutine());
    }

    /// <summary>Fold into light, permanently — same fiction as a unit's death, bigger flash.</summary>
    IEnumerator CollapseRoutine()
    {
        _dying = true;
        All.Remove(this);
        CommanderOps.Log(_teamId, $"{Definition?.displayName ?? "STRUCTURE"} DESTROYED");

        var collider = GetComponent<BoxCollider>();
        if (collider != null)
            collider.enabled = false;
        var obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
            obstacle.enabled = false;

        VfxUtil.Explosion(transform.position + Vector3.up * (Definition?.height ?? 3f) * 0.5f,
            MatchAnnouncer.TeamColor(_teamId), 1.8f);
        GameAudio.Play(GameAudio.Id.Explosion,
            transform.position + Vector3.up * (Definition?.height ?? 3f) * 0.5f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            if (_body != null)
                _body.localScale = new Vector3(1f - t * 0.4f, 1f - t, 1f - t * 0.4f);
            yield return null;
        }

        OnBuildingLost?.Invoke(this);
        Destroy(gameObject);
    }

    // ------------------------------------------------------------- sweep

    /// <summary>Teardown sweep, immediate for the same reasons the units'. </summary>
    public static void DespawnAll()
    {
        foreach (var building in Object.FindObjectsByType<Building>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(building.gameObject);
        All.Clear();
    }
}

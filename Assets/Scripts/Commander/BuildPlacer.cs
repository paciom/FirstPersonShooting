using UnityEngine;

/// <summary>
/// The build cursor: a holographic ghost of the pending structure that
/// follows the mouse, judges the ground under it, and turns into a real
/// Building on a valid click.
///
/// Placement law, Red Alert's own: on the map, on flat open ground, within
/// <see cref="AdjacencyRange"/> of a standing friendly structure, and paid
/// for in full at the moment of placement — not at arming, so browsing the
/// build bar never costs anything.
/// </summary>
public class BuildPlacer : MonoBehaviour
{
    public const float AdjacencyRange = 12f;
    const int PlayerTeam = 0;

    /// <summary>True while a ghost is up — CommanderSelection yields the mouse.</summary>
    public static bool Active { get; private set; }

    /// <summary>
    /// Last frame the placer owned the mouse. Update order between the
    /// components on the Commander object is undefined, so the frame the
    /// ghost is cancelled (right-click) or spent (placement click) must ALSO
    /// read as placer-owned, or the very same click falls through to
    /// CommanderSelection as a world order.
    /// </summary>
    public static int LastActiveFrame { get; private set; } = -1;

    BuildingDefinition _pending;
    GameObject _ghost;
    Material _validMat;
    Material _invalidMat;
    bool _valid;
    Camera _camera;

    void Awake()
    {
        _validMat = GhostMaterial(new Color(0.25f, 1f, 0.6f));
        _invalidMat = GhostMaterial(new Color(1f, 0.25f, 0.2f));
    }

    static Material GhostMaterial(Color color)
    {
        // Additive with the soft glow texture: reads as hologram, needs no
        // transparent-surface fiddling, and can never z-fight the ground.
        var mat = new Material(Shader.Find("PhotonArena/Additive"));
        mat.SetTexture("_MainTex", Resources.Load<Texture2D>("VFX/glow"));
        mat.SetColor("_Color", color);
        mat.SetFloat("_Intensity", 0.9f);
        return mat;
    }

    void OnDestroy()
    {
        Active = false;
        if (_validMat != null) Destroy(_validMat);
        if (_invalidMat != null) Destroy(_invalidMat);
    }

    /// <summary>Build-bar entry point: put this building's ghost on the cursor.</summary>
    public void Arm(BuildingDefinition def)
    {
        Cancel();
        _pending = def;
        _ghost = BuildGhost(def);
        Active = true;
    }

    /// <summary>Put the ghost away. Safe to call armed or not.</summary>
    public void Cancel()
    {
        if (Active)
            LastActiveFrame = Time.frameCount;
        if (_ghost != null)
            Destroy(_ghost);
        _ghost = null;
        _pending = null;
        Active = false;
    }

    /// <summary>Escape-chain hook: cancels if armed, reports whether it did.</summary>
    public bool CancelPending()
    {
        if (!Active)
            return false;
        Cancel();
        return true;
    }

    void Update()
    {
        // Self-heal the static after a mid-play recompile.
        Active = _ghost != null;
        if (!Active)
            return;
        LastActiveFrame = Time.frameCount;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null)
                return;
        }

        // Right-click is the universal "never mind".
        if (Input.GetMouseButtonDown(1))
        {
            Cancel();
            return;
        }

        // Clicks on the build bar belong to the build bar — arming a
        // different building must not also place this one through the button.
        bool onUi = UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        var ray = _camera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 600f))
            return;

        // Whole-metre grid: bases end up looking planned, not scattered, and
        // two same-size buildings placed "next to each other" actually align.
        var center = new Vector3(Mathf.Round(hit.point.x), CommanderMap.GroundY,
                                 Mathf.Round(hit.point.z));
        _ghost.transform.position = center;

        _valid = Judge(center, hit);
        var mat = _valid ? _validMat : _invalidMat;
        foreach (var renderer in _ghost.GetComponentsInChildren<MeshRenderer>())
            renderer.sharedMaterial = mat;

        if (Input.GetMouseButtonDown(0) && _valid && !onUi)
        {
            if (CommanderEconomy.Spend(PlayerTeam, _pending.cost))
            {
                Building.Construct(_pending, PlayerTeam, center);
                Cancel();
            }
            // Not affordable after all (a queue spent it first): ghost stays
            // up, so the click is a no-op rather than a lost order.
        }
    }

    bool Judge(Vector3 center, RaycastHit under)
    {
        // The cursor must be reading actual ground, not a ridge top or a roof.
        if (under.point.y > CommanderMap.GroundY + 0.5f)
            return false;

        return IsValidPlacement(_pending, PlayerTeam, center)
            && CommanderEconomy.Credits(PlayerTeam) >= _pending.cost;
    }

    /// <summary>
    /// The placement law, shared verbatim by the ghost and the AI commander —
    /// both sides build under the same rules, which is the plan's no-cheating
    /// promise. Affordability is the caller's business.
    /// </summary>
    public static bool IsValidPlacement(BuildingDefinition def, int teamId, Vector3 center)
    {
        // On the battlefield proper, clear of the border cliffs.
        float margin = Mathf.Max(def.footprint.x, def.footprint.y) * 0.5f + 2f;
        if (Mathf.Abs(center.x) > CommanderMap.HalfExtent - margin ||
            Mathf.Abs(center.z) > CommanderMap.HalfExtent - margin)
            return false;

        // Nothing already standing in the footprint. The ground plane sits
        // below y=0 so a box from the surface up never sees it; everything
        // else — ridges, crystals, rocks, pads, units, buildings — blocks.
        var half = new Vector3(def.footprint.x * 0.5f, def.height * 0.5f, def.footprint.y * 0.5f);
        var box = Physics.OverlapBox(center + Vector3.up * (def.height * 0.5f + 0.05f), half,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        if (box.Length > 0)
            return false;

        // The Red Alert adjacency rule: bases grow outward from what stands.
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != teamId || !building.IsAlive)
                continue;
            Vector3 flat = building.transform.position - center;
            flat.y = 0f;
            if (flat.magnitude <= AdjacencyRange + Mathf.Max(def.footprint.x, def.footprint.y) * 0.5f)
                return true;
        }
        return false;
    }

    /// <summary>Renderer-only copy of the building's block silhouette.</summary>
    GameObject BuildGhost(BuildingDefinition def)
    {
        var ghost = new GameObject($"Ghost_{def.key}");
        float w = def.footprint.x, d = def.footprint.y, h = def.height;
        GhostBlock(ghost.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        GhostBlock(ghost.transform, new Vector3(0f, h + 0.12f, 0f), new Vector3(w * 0.82f, 0.24f, d * 0.82f));
        return ghost;
    }

    void GhostBlock(Transform parent, Vector3 localPos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // Immediate, not deferred: the ghost may be raycast against THIS
        // frame (Arm runs from a button click, Update runs after), and a
        // ghost the cursor ray can hit judges its own roof instead of the
        // ground.
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = _validMat;
    }
}

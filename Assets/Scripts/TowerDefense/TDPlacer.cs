using UnityEngine;

/// <summary>
/// The build cursor: a holographic ghost that follows the mouse anywhere on
/// the high ground, judges the footing under it, and raises a real Building
/// plus its TDTower on a valid click.
///
/// Placement law: anywhere on the RIM — the plateau standing 1.2 m over the
/// lane. That one rule is the whole safety argument: the raiders' route runs
/// below the buildable surface, so no tower, anywhere, can ever block it —
/// free placement without free-placement's classic soft-lock. Footing is
/// judged by probing the ground itself (five down-rays: centre and corners),
/// so a footprint can't hang over the canyon or squat on a boulder.
///
/// Right-click with no ghost armed points the other way: sell the tower
/// under the cursor for most of its price back.
/// </summary>
public class TDPlacer : MonoBehaviour
{
    /// <summary>How far off rim height a footing probe may read and still count.</summary>
    const float FootingTolerance = 0.3f;

    /// <summary>Sold towers refund this share — mistakes cheap, shuffling not free.</summary>
    const float RefundShare = 0.7f;

    /// <summary>True while a ghost is up — CommanderTouch yields the finger to us.</summary>
    public static bool Active { get; private set; }

    TDTowerDefinition _pending;
    GameObject _ghost;
    GameObject _rangeRing;
    Material _validMat;
    Material _invalidMat;
    bool _valid;
    Camera _camera;
    Vector2 _touchDownAt;

    void Awake()
    {
        _validMat = GhostMaterial(new Color(0.25f, 1f, 0.6f));
        _invalidMat = GhostMaterial(new Color(1f, 0.25f, 0.2f));
    }

    static Material GhostMaterial(Color color)
    {
        // Additive with the soft glow texture — hologram for free, no
        // z-fighting, same recipe as Commander's BuildPlacer.
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

    /// <summary>Build-bar entry point: put this tower's ghost on the cursor.</summary>
    public void Arm(TDTowerDefinition def)
    {
        Cancel();
        _pending = def;
        _ghost = BuildGhost(def);
        // The range disc travels with the ghost — where a tower CAN shoot
        // is the entire placement decision, so it shows before any money moves.
        if (def.range > 0f)
            _rangeRing = CommanderUnit.GlowQuad(_ghost.transform, "RangeRing", "VFX/ring",
                new Color(0.2f, 0.9f, 1f), 0.5f, def.range * 2f, 0.1f);
        Active = true;
    }

    /// <summary>Put the ghost away. Safe to call armed or not.</summary>
    public void Cancel()
    {
        if (_ghost != null)
            Destroy(_ghost);
        _ghost = null;
        _rangeRing = null;
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

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null)
                return;
        }

        bool onUi = UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        if (!Active)
        {
            // No ghost: the right button is the wrecking ball.
            if (Input.GetMouseButtonDown(1) && !onUi)
                TrySell();
            return;
        }

        // Right-click is the universal "never mind".
        if (Input.GetMouseButtonDown(1))
        {
            Cancel();
            return;
        }

        // On touch the finger is the cursor; a lift that hasn't wandered is
        // the placement click — BuildPlacer's own touch grammar.
        Vector3 pointer = Input.mousePosition;
        bool placeClick = Input.GetMouseButtonDown(0) && !onUi;
        if (TouchControls.Active && Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            pointer = touch.position;
            if (touch.phase == TouchPhase.Began)
                _touchDownAt = touch.position;
            placeClick = touch.phase == TouchPhase.Ended
                && (touch.position - _touchDownAt).magnitude < 24f
                && !TouchControls.PointOver(touch.position)
                && !(UnityEngine.EventSystems.EventSystem.current != null
                     && UnityEngine.EventSystems.EventSystem.current
                         .IsPointerOverGameObject(touch.fingerId));
        }

        var ray = _camera.ScreenPointToRay(pointer);
        if (!Physics.Raycast(ray, out RaycastHit hit, 600f))
            return;

        // Whole-metre grid, as Commander places: a defense line built one
        // click at a time still ends up LOOKING like a line.
        var center = new Vector3(Mathf.Round(hit.point.x), TDMap.PlateauY,
                                 Mathf.Round(hit.point.z));
        _ghost.transform.position = center;

        _valid = Judge(center) && TDEconomy.Credits >= _pending.cost;
        var mat = _valid ? _validMat : _invalidMat;
        foreach (var renderer in _ghost.GetComponentsInChildren<MeshRenderer>())
        {
            if (_rangeRing != null && renderer.transform == _rangeRing.transform)
                continue;   // the range disc keeps its own quiet cyan
            renderer.sharedMaterial = mat;
        }

        if (placeClick && _valid && TDEconomy.Spend(_pending.cost))
        {
            var building = Building.Construct(_pending.building, 0, center);
            building.gameObject.AddComponent<TDTower>().Configure(_pending);
            Cancel();
        }
    }

    /// <summary>
    /// The placement law. Bounds first, then footing (centre and all four
    /// footprint corners stand on rim-height ground — not the lane, not a
    /// boulder, not thin air past the cliff edge), then a clear footprint.
    /// </summary>
    bool Judge(Vector3 center)
    {
        var def = _pending.building;
        float margin = Mathf.Max(def.footprint.x, def.footprint.y) * 0.5f + 2f;
        if (Mathf.Abs(center.x) > TDMap.HalfExtent - margin ||
            Mathf.Abs(center.z) > TDMap.HalfExtent - margin)
            return false;

        float halfW = def.footprint.x * 0.5f, halfD = def.footprint.y * 0.5f;
        if (!OnRim(center)
            || !OnRim(center + new Vector3(halfW, 0f, halfD))
            || !OnRim(center + new Vector3(halfW, 0f, -halfD))
            || !OnRim(center + new Vector3(-halfW, 0f, halfD))
            || !OnRim(center + new Vector3(-halfW, 0f, -halfD)))
            return false;

        // Nothing already standing in the footprint. The box starts just
        // above the plateau surface, so the plateau itself never trips it —
        // rocks, crystals, vents and other towers all do.
        var half = new Vector3(halfW, def.height * 0.5f, halfD);
        var box = Physics.OverlapBox(center + Vector3.up * (def.height * 0.5f + 0.05f), half,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        return box.Length == 0;
    }

    /// <summary>
    /// One footing probe: does the ground under this point read as the rim?
    /// A physics question rather than a map-grid lookup on purpose — it
    /// needs no static layout data, so a recompile during Play can never
    /// leave the placer approving lane floor.
    /// </summary>
    static bool OnRim(Vector3 point)
    {
        if (!Physics.Raycast(point + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 16f,
                ~0, QueryTriggerInteraction.Ignore))
            return false;
        return Mathf.Abs(hit.point.y - TDMap.PlateauY) <= FootingTolerance;
    }

    /// <summary>
    /// Right-click demolition: the tower under the cursor collapses and
    /// refunds most of its price. The Core is not for sale.
    /// </summary>
    void TrySell()
    {
        var ray = _camera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 600f))
            return;
        var tower = hit.transform.root.GetComponent<TDTower>();
        var building = hit.transform.root.GetComponent<Building>();
        if (tower == null || building == null || !building.IsAlive)
            return;
        var def = TDTowerCatalog.Get(building.Definition?.key);
        if (def == null)
            return;

        TDEconomy.Grant(Mathf.RoundToInt(def.cost * RefundShare));
        var shield = building.GetComponent<EnergyShield>();
        if (shield != null && !shield.IsDown)
            shield.TakeHit(999999f, building.transform.position + Vector3.up);
    }

    /// <summary>Renderer-only silhouette of the pending tower.</summary>
    GameObject BuildGhost(TDTowerDefinition def)
    {
        var ghost = new GameObject($"Ghost_{def.key}");
        float w = def.building.footprint.x, d = def.building.footprint.y, h = def.building.height;
        GhostBlock(ghost.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        GhostBlock(ghost.transform, new Vector3(0f, h + 0.12f, 0f),
            new Vector3(w * 0.82f, 0.24f, d * 0.82f));
        return ghost;
    }

    void GhostBlock(Transform parent, Vector3 localPos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // Immediate: Arm runs from a button click and Update raycasts this
        // same frame — a collider-bearing ghost would judge its own roof.
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = _validMat;
    }
}

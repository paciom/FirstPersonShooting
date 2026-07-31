using UnityEngine;

/// <summary>
/// The build cursor for socket country: a holographic ghost that snaps to
/// the nearest free foundation, judges only occupancy and price — the
/// sockets already settled every placement question Commander's free-ground
/// law exists to answer — and raises a real Building plus its TDTower on a
/// valid click.
///
/// Right-click with no ghost armed points the other way: sell the tower
/// under the cursor for most of its price back.
/// </summary>
public class TDPlacer : MonoBehaviour
{
    /// <summary>How far the cursor may miss a socket and still mean it.</summary>
    const float SnapRange = 7f;

    /// <summary>Sold towers refund this share — mistakes cheap, shuffling not free.</summary>
    const float RefundShare = 0.7f;

    /// <summary>True while a ghost is up — CommanderTouch yields the finger to us.</summary>
    public static bool Active { get; private set; }

    TDTowerDefinition _pending;
    GameObject _ghost;
    GameObject _rangeRing;
    Material _validMat;
    Material _invalidMat;
    TDSocket _socket;
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
        _socket = null;
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

        // Snap to the nearest free foundation; past snap range the ghost
        // rides the cursor, red, saying "not here — find a pad".
        _socket = TDSocket.NearestFree(hit.point, SnapRange);
        Vector3 at = _socket != null ? _socket.Center
            : new Vector3(hit.point.x, TDMap.PlateauY + 0.08f, hit.point.z);
        _ghost.transform.position = at;

        bool valid = _socket != null && TDEconomy.Credits >= _pending.cost;
        var mat = valid ? _validMat : _invalidMat;
        foreach (var renderer in _ghost.GetComponentsInChildren<MeshRenderer>())
        {
            if (_rangeRing != null && renderer.transform == _rangeRing.transform)
                continue;   // the range disc keeps its own quiet cyan
            renderer.sharedMaterial = mat;
        }

        if (placeClick && valid && TDEconomy.Spend(_pending.cost))
        {
            var building = Building.Construct(_pending.building, 0, _socket.Center);
            building.gameObject.AddComponent<TDTower>().Configure(_pending);
            Cancel();
        }
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

    /// <summary>Renderer-only silhouette of the pending tower, pad-sized.</summary>
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

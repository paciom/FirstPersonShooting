using UnityEngine;

/// <summary>
/// The F3 hit-volume overlay for the Brawl modes: translucent additive
/// glow, so it draws over the robots without hiding them.
///
/// Green capsule = the hurtbox, the body column a strike must actually
/// reach into (±BodyHalfWidth around the centre, BodyHeight tall). The
/// small sphere rides the striking limb's bone: yellow through the swing,
/// red for exactly the frames the hit window is open. If a hit ever lands
/// while the red sphere is outside a green column, THAT is the bug.
///
/// Off by default; F3 toggles. Debug only — nothing here exists until the
/// key is first pressed.
/// </summary>
public class BrawlDebug : MonoBehaviour
{
    BrawlFighter[] _fighters;
    GameObject[] _columns;
    GameObject[] _markers;
    MeshRenderer[] _markerRenderers;
    Material _bodyMaterial, _swingMaterial, _hotMaterial;
    bool _visible;

    public static void Attach(GameObject host, params BrawlFighter[] fighters)
    {
        var debug = host.AddComponent<BrawlDebug>();
        debug._fighters = fighters;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3))
        {
            _visible = !_visible;
            if (_visible && _columns == null)
                Build();
            Apply();
        }
        if (!_visible || _columns == null)
            return;

        for (int i = 0; i < _fighters.Length; i++)
        {
            var fighter = _fighters[i];
            if (fighter == null)
                continue;

            var effector = fighter.ActiveEffector;
            bool striking = effector != null;
            if (_markers[i].activeSelf != striking)
                _markers[i].SetActive(striking);
            if (striking)
            {
                _markers[i].transform.position = effector.position;
                _markerRenderers[i].sharedMaterial =
                    fighter.AttackWindowOpen ? _hotMaterial : _swingMaterial;
            }
        }
    }

    void Build()
    {
        _bodyMaterial = VfxUtil.MakeGlowMaterial(new Color(0.25f, 1f, 0.4f), 0.45f);
        _swingMaterial = VfxUtil.MakeGlowMaterial(new Color(1f, 0.9f, 0.2f), 0.8f);
        _hotMaterial = VfxUtil.MakeGlowMaterial(new Color(1f, 0.2f, 0.15f), 1.4f);

        _columns = new GameObject[_fighters.Length];
        _markers = new GameObject[_fighters.Length];
        _markerRenderers = new MeshRenderer[_fighters.Length];
        for (int i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i] == null)
                continue;

            var column = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            column.name = "DebugHurtbox";
            Destroy(column.GetComponent<Collider>());
            column.transform.SetParent(_fighters[i].transform, false);
            // Capsule primitive is radius 0.5 / height 2 at unit scale.
            column.transform.localPosition = new Vector3(0f, BrawlMoveSet.BodyHeight * 0.5f, 0f);
            column.transform.localScale = new Vector3(
                BrawlMoveSet.BodyHalfWidth * 2f,
                BrawlMoveSet.BodyHeight * 0.5f,
                BrawlMoveSet.BodyHalfWidth * 2f);
            column.GetComponent<MeshRenderer>().sharedMaterial = _bodyMaterial;
            _columns[i] = column;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "DebugStrikePoint";
            Destroy(marker.GetComponent<Collider>());
            // World-parented on purpose: it tracks the bone in Update and
            // must not inherit the model's normalization scale.
            marker.transform.SetParent(transform, true);
            marker.transform.localScale = Vector3.one * 0.26f;
            _markerRenderers[i] = marker.GetComponent<MeshRenderer>();
            marker.SetActive(false);
            _markers[i] = marker;
        }
    }

    void Apply()
    {
        if (_columns == null)
            return;
        foreach (var column in _columns)
            if (column != null)
                column.SetActive(_visible);
        if (!_visible)
            foreach (var marker in _markers)
                if (marker != null)
                    marker.SetActive(false);
    }
}

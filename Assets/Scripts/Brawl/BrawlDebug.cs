using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The F3 hit-volume overlay for the Brawl modes.
///
/// Green translucent capsule = the hurtbox, the body column a strike must
/// reach into; it renders with ZTest Always, so it is visible THROUGH the
/// robot — no need to make the robots transparent to see it. The sphere
/// rides the striking limb's bone: yellow through the swing, red for
/// exactly the frames the hit window is open.
///
/// The top-left log names every strike's outcome — HIT n / BLOCKED /
/// WHIFF — because a blocked hit and a whiff both look like "it hit but
/// the health bar didn't move" at couch distance.
///
/// Off by default; F3 toggles. Nothing is built until the first press.
/// </summary>
public class BrawlDebug : MonoBehaviour
{
    const int LogLines = 6;

    BrawlFighter[] _fighters;
    GameObject[] _columns;
    GameObject[] _markers;
    MeshRenderer[] _markerRenderers;
    Material _bodyMaterial, _swingMaterial, _hotMaterial;
    bool _visible;
    readonly List<string> _log = new List<string>();

    public static void Attach(GameObject host, params BrawlFighter[] fighters)
    {
        var debug = host.AddComponent<BrawlDebug>();
        debug._fighters = fighters;
        for (int i = 0; i < fighters.Length; i++)
        {
            var fighter = fighters[i];
            if (fighter == null)
                continue;
            string who = fighter.TeamId == 0 ? "P1" : "P2";
            fighter.OnHitLanded += (victim, damage, down) =>
                debug.Log($"{who} {debug.MoveName(fighter)} → HIT {damage}{(down ? "  (down)" : "")}");
            fighter.OnHitBlocked += (victim, move) =>
                debug.Log($"{who} {move.ToString().ToUpperInvariant()} → BLOCKED");
            fighter.OnWhiffed += move =>
                debug.Log($"{who} {move.ToString().ToUpperInvariant()} → WHIFF");
        }
    }

    string MoveName(BrawlFighter fighter)
    {
        // The landed event doesn't carry the move; the live one is right —
        // the event fires mid-swing, before the state can change.
        var effector = fighter.ActiveEffector;
        if (effector == null)
            return "BLAST";
        if (fighter.Phase == BrawlFighter.State.AirAttack)
            return "FLYKICK";
        return effector.name == "RightHand" ? "PUNCH" : "KICK";
    }

    void Log(string line)
    {
        _log.Add($"[{Time.time:0.0}s]  {line}");
        if (_log.Count > LogLines)
            _log.RemoveAt(0);
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

    void OnGUI()
    {
        if (!_visible)
            return;
        GUI.color = new Color(0.6f, 1f, 0.7f, 0.95f);
        for (int i = 0; i < _log.Count; i++)
            GUI.Label(new Rect(12, 12 + i * 20, 640, 20), _log[i]);
        GUI.color = Color.white;
    }

    void Build()
    {
        _bodyMaterial = Translucent(new Color(0.2f, 1f, 0.4f, 0.30f));
        _swingMaterial = Translucent(new Color(1f, 0.9f, 0.2f, 0.85f));
        _hotMaterial = Translucent(new Color(1f, 0.2f, 0.15f, 0.95f));

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
            marker.transform.localScale = Vector3.one * (BrawlMoveSet.StrikeRadius * 2f);
            _markerRenderers[i] = marker.GetComponent<MeshRenderer>();
            marker.SetActive(false);
            _markers[i] = marker;
        }
    }

    /// <summary>
    /// A runtime URP/Unlit set up for alpha blending with ZTest Always —
    /// visible through geometry, which is the whole point of a debug
    /// volume. (VfxUtil's glow material is OPAQUE unlit; at low intensity
    /// it was just a dark shell, which is why nothing readable appeared.)
    /// </summary>
    static Material Translucent(Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)CompareFunction.Always);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent + 50;
        mat.SetColor("_BaseColor", color);
        return mat;
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

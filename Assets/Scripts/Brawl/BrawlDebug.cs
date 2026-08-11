using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The F3 combat overlay for the Brawl modes, drawn over the REAL hurtbox
/// rig — per-bone colliders that follow the pose, so what you see is what
/// the strike query tests (the old fixed column visibly disagreed with any
/// crouched or fallen body).
///
/// Per part: a translucent fill (ZTest Always — visible through the
/// robots) plus a wireframe. Green = vital (damage), cyan = graze (sparks
/// only), and a part flashes BLUE for a beat when a strike actually
/// touches it. The sphere rides the striking limb's bone: yellow while
/// the swing is not touching anyone, red the moment it is.
///
/// Top-left: the outcome log (HIT n / BLOCKED / GRAZE / WHIFF) and a perf
/// line — frame ms, object/particle/light counts — so a bad frame rate
/// can name its own suspect.
/// </summary>
public class BrawlDebug : MonoBehaviour
{
    const int LogLines = 6;

    BrawlFighter[] _fighters;
    readonly List<BrawlBodyPart> _parts = new List<BrawlBodyPart>();
    readonly List<GameObject> _fills = new List<GameObject>();
    readonly Dictionary<BrawlBodyPart, List<MeshRenderer>> _partFills =
        new Dictionary<BrawlBodyPart, List<MeshRenderer>>();
    // Struck parts and when their blue flash expires.
    readonly Dictionary<BrawlBodyPart, float> _struckUntil =
        new Dictionary<BrawlBodyPart, float>();
    GameObject[] _markers;
    MeshRenderer[] _markerRenderers;
    Material _vitalMaterial, _grazeMaterial, _swingMaterial, _hotMaterial,
        _struckMaterial, _lineMaterial;
    bool _visible;
    readonly List<string> _log = new List<string>();

    float _smoothedMs;
    int _objectCount, _particleCount, _lightCount;
    int _countdown;

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
            fighter.OnGrazed += label =>
                debug.Log($"{who} STRIKE → GRAZE ({label})");
            fighter.OnPartStruck += part =>
                debug._struckUntil[part] = Time.time + 0.30f;
        }
    }

    string MoveName(BrawlFighter fighter)
    {
        string name = fighter.CurrentVariant.display;
        return string.IsNullOrEmpty(name) ? "STRIKE" : name;
    }

    void Log(string line)
    {
        _log.Add($"[{Time.time:0.0}s]  {line}");
        if (_log.Count > LogLines)
            _log.RemoveAt(0);
    }

    void Update()
    {
        _smoothedMs = Mathf.Lerp(_smoothedMs, Time.unscaledDeltaTime * 1000f, 0.05f);

        if (Input.GetKeyDown(KeyCode.F3))
        {
            _visible = !_visible;
            if (_visible && _parts.Count == 0)
                Build();
            Apply();
        }
        if (!_visible)
            return;

        // The census, refreshed every couple of seconds: when the frame
        // rate dies, one of these numbers is usually climbing.
        if (--_countdown <= 0)
        {
            _countdown = 120;
            _objectCount = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            _particleCount = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length;
            _lightCount = FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        }

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
                // Red means TOUCHING — the live overlap test, the same one
                // the strike query runs — yellow is a swing in empty air.
                var foe = fighter.Opponent;
                bool touching = foe != null && foe.HasHurtboxes
                    && BrawlHurtboxes.Query(effector.position,
                        BrawlMoveSet.StrikeRadius + 0.04f, foe) != null;
                _markerRenderers[i].sharedMaterial =
                    touching ? _hotMaterial : _swingMaterial;
            }
        }

        // The struck flash: any part a strike touched holds blue for a
        // beat, then falls back to its vital/graze colour.
        foreach (var pair in _partFills)
        {
            bool struck = _struckUntil.TryGetValue(pair.Key, out float until)
                          && Time.time < until;
            var material = struck ? _struckMaterial
                : pair.Key != null && pair.Key.Vital ? _vitalMaterial : _grazeMaterial;
            foreach (var renderer in pair.Value)
                if (renderer != null && renderer.sharedMaterial != material)
                    renderer.sharedMaterial = material;
        }
    }

    void OnGUI()
    {
        if (!_visible)
            return;
        GUI.color = new Color(0.6f, 1f, 0.7f, 0.95f);
        for (int i = 0; i < _log.Count; i++)
            GUI.Label(new Rect(12, 12 + i * 20, 700, 20), _log[i]);
        GUI.color = new Color(1f, 1f, 0.6f, 0.95f);
        GUI.Label(new Rect(12, 12 + LogLines * 20 + 6, 700, 20),
            $"frame {_smoothedMs:0.0} ms ({(1000f / Mathf.Max(0.1f, _smoothedMs)):0} FPS)   " +
            $"objects {_objectCount}   particles {_particleCount}   lights {_lightCount}");
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------- build

    void Build()
    {
        _vitalMaterial = Translucent(new Color(0.2f, 1f, 0.4f, 0.22f));
        _grazeMaterial = Translucent(new Color(0.2f, 0.75f, 1f, 0.18f));
        _swingMaterial = Translucent(new Color(1f, 0.9f, 0.2f, 0.85f));
        _hotMaterial = Translucent(new Color(1f, 0.2f, 0.15f, 0.95f));
        _struckMaterial = Translucent(new Color(0.15f, 0.35f, 1f, 0.55f));
        _lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        _lineMaterial.SetInt("_ZWrite", 0);

        foreach (var fighter in _fighters)
        {
            if (fighter == null)
                continue;
            foreach (var part in fighter.GetComponentsInChildren<BrawlBodyPart>(true))
            {
                _parts.Add(part);
                var renderers = new List<MeshRenderer>();
                foreach (var collider in part.GetComponents<Collider>())
                {
                    var fill = BuildFill(part, collider);
                    _fills.Add(fill);
                    if (fill != null)
                        renderers.Add(fill.GetComponent<MeshRenderer>());
                }
                _partFills[part] = renderers;
            }
        }

        _markers = new GameObject[_fighters.Length];
        _markerRenderers = new MeshRenderer[_fighters.Length];
        for (int i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i] == null)
                continue;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "DebugStrikePoint";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, true);
            marker.transform.localScale = Vector3.one * (BrawlMoveSet.StrikeRadius * 2f);
            _markerRenderers[i] = marker.GetComponent<MeshRenderer>();
            marker.SetActive(false);
            _markers[i] = marker;
        }
    }

    GameObject BuildFill(BrawlBodyPart part, Collider collider)
    {
        GameObject fill;
        if (collider is CapsuleCollider capsule)
        {
            fill = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(fill.GetComponent<Collider>());
            fill.transform.SetParent(part.transform, false);
            fill.transform.localPosition = capsule.center;
            fill.transform.localRotation =
                capsule.direction == 0 ? Quaternion.Euler(0f, 0f, 90f)
                : capsule.direction == 2 ? Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.identity;
            fill.transform.localScale = new Vector3(
                capsule.radius * 2f, capsule.height * 0.5f, capsule.radius * 2f);
        }
        else if (collider is SphereCollider sphere)
        {
            fill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(fill.GetComponent<Collider>());
            fill.transform.SetParent(part.transform, false);
            fill.transform.localPosition = sphere.center;
            fill.transform.localScale = Vector3.one * sphere.radius * 2f;
        }
        else
        {
            return null;
        }
        fill.name = "DebugHurtFill";
        fill.GetComponent<MeshRenderer>().sharedMaterial =
            part.Vital ? _vitalMaterial : _grazeMaterial;
        return fill;
    }

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
        foreach (var fill in _fills)
            if (fill != null)
                fill.SetActive(_visible);
        if (!_visible && _markers != null)
            foreach (var marker in _markers)
                if (marker != null)
                    marker.SetActive(false);
    }

    // --------------------------------------------------------- wireframe

    void OnRenderObject()
    {
        if (!_visible || _lineMaterial == null)
            return;
        _lineMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        foreach (var part in _parts)
        {
            if (part == null)
                continue;
            bool struck = _struckUntil.TryGetValue(part, out float until)
                          && Time.time < until;
            GL.Color(struck
                ? new Color(0.35f, 0.55f, 1f, 1f)
                : part.Vital
                    ? new Color(0.3f, 1f, 0.45f, 0.9f)
                    : new Color(0.35f, 0.8f, 1f, 0.8f));
            foreach (var collider in part.GetComponents<Collider>())
            {
                if (collider is CapsuleCollider capsule)
                    WireCapsule(capsule);
                else if (collider is SphereCollider sphere)
                    WireSphere(sphere);
            }
        }
        GL.End();
    }

    static void WireCapsule(CapsuleCollider capsule)
    {
        var t = capsule.transform;
        float scale = t.lossyScale.x;
        float radius = capsule.radius * scale;
        Vector3 axis = capsule.direction == 0 ? Vector3.right
            : capsule.direction == 2 ? Vector3.forward : Vector3.up;
        float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);

        Vector3 top = t.TransformPoint(capsule.center + axis * half);
        Vector3 bottom = t.TransformPoint(capsule.center - axis * half);
        Vector3 worldAxis = (top - bottom).sqrMagnitude > 1e-8f
            ? (top - bottom).normalized
            : t.TransformDirection(axis);
        Vector3 side = Vector3.Cross(worldAxis, Vector3.up).sqrMagnitude > 1e-4f
            ? Vector3.Cross(worldAxis, Vector3.up).normalized
            : Vector3.Cross(worldAxis, Vector3.right).normalized;
        Vector3 forward = Vector3.Cross(worldAxis, side).normalized;

        Circle(top, side, forward, radius);
        Circle(bottom, side, forward, radius);
        Line(top + side * radius, bottom + side * radius);
        Line(top - side * radius, bottom - side * radius);
        Line(top + forward * radius, bottom + forward * radius);
        Line(top - forward * radius, bottom - forward * radius);
    }

    static void WireSphere(SphereCollider sphere)
    {
        var t = sphere.transform;
        Vector3 center = t.TransformPoint(sphere.center);
        float radius = sphere.radius * t.lossyScale.x;
        Circle(center, Vector3.right, Vector3.forward, radius);
        Circle(center, Vector3.right, Vector3.up, radius);
        Circle(center, Vector3.up, Vector3.forward, radius);
    }

    static void Circle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius)
    {
        const int Segments = 14;
        Vector3 previous = center + axisA * radius;
        for (int i = 1; i <= Segments; i++)
        {
            float angle = i / (float)Segments * Mathf.PI * 2f;
            Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            Line(previous, next);
            previous = next;
        }
    }

    static void Line(Vector3 a, Vector3 b)
    {
        GL.Vertex(a);
        GL.Vertex(b);
    }
}

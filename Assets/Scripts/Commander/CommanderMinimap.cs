using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bottom-left minimap: team-coloured blips for every unit, squares for
/// structures, amber dots for the crystal fields, and a diamond marking
/// where the camera looks. Tap or click anywhere on it to jump the view —
/// which, being uGUI, works identically for mouse and touch, making the
/// minimap the tablet's fastest way around the battlefield.
/// </summary>
public class CommanderMinimap : MonoBehaviour
{
    const float PanelSize = 230f;
    const float RefreshSeconds = 0.1f;

    RectTransform _panel;
    readonly RectTransform[] _viewEdges = new RectTransform[4];
    Camera _viewCamera;
    float _nextRefresh;

    readonly System.Collections.Generic.List<Image> _unitBlips =
        new System.Collections.Generic.List<Image>();
    readonly System.Collections.Generic.List<Image> _buildingBlips =
        new System.Collections.Generic.List<Image>();

    /// <summary>Relay so the panel Image (a uGUI target) can hear its own clicks.</summary>
    class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public CommanderMinimap owner;
        public void OnPointerClick(PointerEventData eventData) => owner.JumpTo(eventData.position);
    }

    void Awake()
    {
        var canvasGo = new GameObject("MinimapCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 13;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var panelGo = new GameObject("Minimap");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var back = panelGo.AddComponent<Image>();
        back.color = new Color(0.02f, 0.05f, 0.08f, 0.85f);
        // raycastTarget stays TRUE: clicks here are jumps, and the pointer-
        // over-UI guard keeps them out of the battlefield underneath.
        _panel = back.rectTransform;
        _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 0f);
        _panel.pivot = new Vector2(0f, 0f);
        _panel.anchoredPosition = new Vector2(14f, 14f);
        _panel.sizeDelta = new Vector2(PanelSize, PanelSize);
        panelGo.AddComponent<ClickRelay>().owner = this;

        // Static geography: the eight crystal fields.
        foreach (var field in CommanderMap.CrystalFields)
        {
            var dot = Blip("Field", new Color(1f, 0.72f, 0.25f, 0.9f), 7f);
            dot.rectTransform.anchoredPosition = ToPanel(new Vector3(field.x, 0f, field.y));
        }

        // The camera's gaze: the actual view frustum's footprint on the
        // ground, drawn as four connected edges — the classic RTS trapezoid,
        // narrow at the near edge, wide at the far one.
        for (int i = 0; i < 4; i++)
        {
            var edge = Blip($"ViewEdge{i}", new Color(1f, 1f, 1f, 0.65f), 2f);
            _viewEdges[i] = edge.rectTransform;
        }
    }

    Image Blip(string name, Color color, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_panel, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;   // only the panel itself takes clicks
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        return image;
    }

    /// <summary>World ground position → panel-local anchored position.</summary>
    static Vector2 ToPanel(Vector3 world)
    {
        return new Vector2(world.x, world.z) / CommanderMap.HalfExtent * (PanelSize * 0.5f);
    }

    void JumpTo(Vector2 screenPoint)
    {
        // Overlay canvas: no camera involved in the conversion.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_panel, screenPoint, null,
                out Vector2 local))
            return;
        // Local is measured from the panel's pivot (bottom-left).
        var normalized = local / PanelSize * 2f - Vector2.one;
        var world = new Vector3(normalized.x * CommanderMap.HalfExtent, 0f,
                                normalized.y * CommanderMap.HalfExtent);

        var camera = FindFirstObjectByType<CommanderCamera>();
        if (camera != null)
            camera.SnapTo(world);
    }

    void Update()
    {
        // The viewport trapezoid tracks every frame — the camera glides, and
        // a 10 Hz rectangle stutters against it. Blips can afford the tick.
        UpdateViewport();

        if (Time.time < _nextRefresh)
            return;
        _nextRefresh = Time.time + RefreshSeconds;

        int unitIndex = 0;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || !unit.IsAlive)
                continue;
            var blip = PoolGet(_unitBlips, unitIndex++, 5f);
            blip.color = MatchAnnouncer.TeamColor(unit.TeamId);
            blip.rectTransform.anchoredPosition = ToPanel(unit.transform.position);
        }
        PoolTrim(_unitBlips, unitIndex);

        int buildingIndex = 0;
        foreach (var building in Building.All)
        {
            if (building == null || !building.IsAlive)
                continue;
            var blip = PoolGet(_buildingBlips, buildingIndex++, 9f);
            blip.color = MatchAnnouncer.TeamColor(building.TeamId);
            blip.rectTransform.anchoredPosition = ToPanel(building.transform.position);
        }
        PoolTrim(_buildingBlips, buildingIndex);

    }

    /// <summary>
    /// Project the camera's four viewport corners onto the ground and draw
    /// the resulting quad. Corner rays always point down at this rig's pitch
    /// and FOV, but the guard clamps a near-horizontal ray to a far point
    /// rather than trusting that forever; panel clamping keeps every edge
    /// inside the map square regardless.
    /// </summary>
    void UpdateViewport()
    {
        if (_viewCamera == null)
        {
            var rig = FindFirstObjectByType<CommanderCamera>();
            if (rig == null)
                return;
            _viewCamera = rig.GetComponent<Camera>();
            if (_viewCamera == null)
                return;
        }

        // Near-left, near-right, far-right, far-left — a closed loop.
        var corners = new Vector2[4];
        var viewport = new[]
        {
            new Vector3(0f, 0f), new Vector3(1f, 0f),
            new Vector3(1f, 1f), new Vector3(0f, 1f),
        };
        float half = PanelSize * 0.5f - 1f;
        for (int i = 0; i < 4; i++)
        {
            var ray = _viewCamera.ViewportPointToRay(viewport[i]);
            float t = ray.direction.y < -0.001f
                ? -ray.origin.y / ray.direction.y
                : 300f;
            Vector3 ground = ray.origin + ray.direction * t;
            Vector2 panel = ToPanel(ground);
            corners[i] = new Vector2(Mathf.Clamp(panel.x, -half, half),
                                     Mathf.Clamp(panel.y, -half, half));
        }

        for (int i = 0; i < 4; i++)
            SetEdge(_viewEdges[i], corners[i], corners[(i + 1) % 4]);
    }

    static void SetEdge(RectTransform edge, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        edge.anchoredPosition = (a + b) * 0.5f;
        edge.sizeDelta = new Vector2(Mathf.Max(2f, delta.magnitude), 2f);
        edge.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    Image PoolGet(System.Collections.Generic.List<Image> pool, int index, float size)
    {
        while (pool.Count <= index)
            pool.Add(Blip(index < _unitBlips.Count + 64 ? "Unit" : "Blip", Color.white, size));
        var blip = pool[index];
        if (!blip.gameObject.activeSelf)
            blip.gameObject.SetActive(true);
        return blip;
    }

    static void PoolTrim(System.Collections.Generic.List<Image> pool, int used)
    {
        for (int i = used; i < pool.Count; i++)
            if (pool[i].gameObject.activeSelf)
                pool[i].gameObject.SetActive(false);
    }
}

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
    RectTransform _viewMarker;
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

        // The camera's gaze: a hollow-reading diamond (a rotated square).
        var marker = Blip("View", new Color(1f, 1f, 1f, 0.7f), 10f);
        _viewMarker = marker.rectTransform;
        _viewMarker.localRotation = Quaternion.Euler(0f, 0f, 45f);
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

        var camera = FindFirstObjectByType<CommanderCamera>();
        if (camera != null && _viewMarker != null)
            _viewMarker.anchoredPosition = ToPanel(camera.Focus);
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

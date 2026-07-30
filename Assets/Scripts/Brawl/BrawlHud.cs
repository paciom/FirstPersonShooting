using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The versus HUD, built at runtime like every other screen in the game:
/// mirrored health bars that deplete toward the corners (with the classic
/// slow amber ghost trailing the damage), round pips, the countdown, a
/// centre announcement that pops, and the end-of-match panel.
/// </summary>
public class BrawlHud : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color HoloMagenta = new Color(1f, 0.25f, 0.9f);
    static readonly Color BarBack = new Color(0.03f, 0.07f, 0.12f, 0.92f);
    static readonly Color Ghost = new Color(1f, 0.7f, 0.25f, 0.9f);

    const float BarWidth = 640f;
    const float BarHeight = 30f;

    Canvas _canvas;
    RectTransform _cyanFill, _cyanGhost, _magentaFill, _magentaGhost;
    float _cyanShown = 1f, _magentaShown = 1f;
    float _cyanGhostShown = 1f, _magentaGhostShown = 1f;
    Image[] _cyanPips, _magentaPips;
    Text _timer;
    Text _announcement;
    float _announceUntil;
    GameObject _endPanel;

    public static BrawlHud Build(Transform parent, string cyanName, string magentaName)
    {
        var go = new GameObject("BrawlHud");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<BrawlHud>();

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(go.transform, false);
        hud._canvas = canvasGo.AddComponent<Canvas>();
        hud._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hud._canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        hud.BuildBars(canvasGo.transform, cyanName, magentaName);
        hud.BuildTimer(canvasGo.transform);
        hud.BuildAnnouncement(canvasGo.transform);
        return hud;
    }

    void BuildBars(Transform parent, string cyanName, string magentaName)
    {
        _cyanFill = BuildBar(parent, true, cyanName, HoloCyan, out _cyanGhost, out _cyanPips);
        _magentaFill = BuildBar(parent, false, magentaName, HoloMagenta, out _magentaGhost, out _magentaPips);
    }

    RectTransform BuildBar(Transform parent, bool left, string name, Color color,
        out RectTransform ghost, out Image[] pips)
    {
        float sign = left ? 1f : -1f;
        var anchor = new Vector2(left ? 0f : 1f, 1f);

        var back = MakeImage(parent, $"Bar_{name}", BarBack);
        var backRect = back.rectTransform;
        backRect.anchorMin = backRect.anchorMax = anchor;
        backRect.pivot = anchor;
        backRect.anchoredPosition = new Vector2(sign * 40f, -34f);
        backRect.sizeDelta = new Vector2(BarWidth, BarHeight);

        // Ghost under fill: it lingers at the old health and eases down, so
        // a combo's total bite stays readable for a beat.
        ghost = MakeFill(back.transform, "Ghost", Ghost, left);
        var fill = MakeFill(back.transform, "Fill", color, left);

        var label = MakeText(parent, $"Name_{name}", name.ToUpperInvariant(), 22, color, FontStyle.Bold);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = anchor;
        labelRect.pivot = anchor;
        labelRect.anchoredPosition = new Vector2(sign * 44f, -70f);
        labelRect.sizeDelta = new Vector2(400, 28);
        label.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

        pips = new Image[2];
        for (int i = 0; i < pips.Length; i++)
        {
            var pip = MakeImage(parent, $"Pip_{name}_{i}", new Color(1f, 1f, 1f, 0.15f));
            var pipRect = pip.rectTransform;
            pipRect.anchorMin = pipRect.anchorMax = anchor;
            pipRect.pivot = anchor;
            pipRect.anchoredPosition = new Vector2(sign * (44f + 410f + i * 34f), -70f);
            pipRect.sizeDelta = new Vector2(22, 22);
            pipRect.localRotation = Quaternion.Euler(0, 0, 45f);
            pips[i] = pip;
        }
        return fill;
    }

    RectTransform MakeFill(Transform back, string name, Color color, bool left)
    {
        var fill = MakeImage(back, name, color);
        var rect = fill.rectTransform;
        // Pivot at the OUTER edge: damage recedes toward the corner, the
        // Street Fighter read.
        var pivot = new Vector2(left ? 0f : 1f, 0.5f);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = pivot;
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        return rect;
    }

    void BuildTimer(Transform parent)
    {
        _timer = MakeText(parent, "Timer", "60", 58, Color.white, FontStyle.Bold);
        var rect = _timer.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -22f);
        rect.sizeDelta = new Vector2(200, 64);
    }

    void BuildAnnouncement(Transform parent)
    {
        _announcement = MakeText(parent, "Announcement", "", 110, Color.white, FontStyle.Bold);
        var rect = _announcement.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 130f);
        rect.sizeDelta = new Vector2(1400, 140);
        _announcement.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- API

    public void SetHealth(float cyan, float magenta)
    {
        _cyanShown = cyan;
        _magentaShown = magenta;
    }

    public void SetTimer(float seconds)
    {
        int shown = Mathf.Max(0, Mathf.CeilToInt(seconds));
        _timer.text = shown.ToString();
        _timer.color = shown <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
    }

    public void SetPips(int cyan, int magenta)
    {
        for (int i = 0; i < _cyanPips.Length; i++)
            _cyanPips[i].color = i < cyan ? HoloCyan : new Color(1f, 1f, 1f, 0.15f);
        for (int i = 0; i < _magentaPips.Length; i++)
            _magentaPips[i].color = i < magenta ? HoloMagenta : new Color(1f, 1f, 1f, 0.15f);
    }

    public void Announce(string message, float seconds, Color color)
    {
        _announcement.text = message;
        _announcement.color = color;
        _announcement.gameObject.SetActive(true);
        _announcement.transform.localScale = Vector3.one * 1.6f;
        _announceUntil = Time.time + seconds;
    }

    public void ShowEndPanel(string result, System.Action onRematch,
        System.Action onRobots, System.Action onMenu)
    {
        HideEndPanel();
        _endPanel = new GameObject("EndPanel");
        _endPanel.transform.SetParent(_canvas.transform, false);
        var stretch = _endPanel.AddComponent<RectTransform>();
        stretch.anchorMin = Vector2.zero;
        stretch.anchorMax = Vector2.one;
        stretch.offsetMin = stretch.offsetMax = Vector2.zero;

        var dim = MakeImage(_endPanel.transform, "Dim", new Color(0.01f, 0.03f, 0.06f, 0.72f));
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;

        var title = MakeText(_endPanel.transform, "Result", result, 96, HoloCyan, FontStyle.Bold);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0, 170);
        titleRect.sizeDelta = new Vector2(1400, 120);

        MakeButton(_endPanel.transform, "REMATCH", -10, onRematch);
        MakeButton(_endPanel.transform, "CHANGE  ROBOTS", -120, onRobots);
        MakeButton(_endPanel.transform, "MAIN  MENU", -230, onMenu);
    }

    public void HideEndPanel()
    {
        if (_endPanel != null)
        {
            Destroy(_endPanel);
            _endPanel = null;
        }
    }

    void Update()
    {
        // Bars chase their true value fast; ghosts follow slow. All the
        // damage feedback lives in that gap.
        float dt = Time.deltaTime;
        float fast = 1f - Mathf.Exp(-14f * dt);
        float slow = 1f - Mathf.Exp(-2.5f * dt);
        Scale(_cyanFill, _cyanShown, fast, ref _cyanShownLerp);
        Scale(_magentaFill, _magentaShown, fast, ref _magentaShownLerp);
        Scale(_cyanGhost, _cyanShown, slow, ref _cyanGhostShown);
        Scale(_magentaGhost, _magentaShown, slow, ref _magentaGhostShown);

        if (_announcement.gameObject.activeSelf)
        {
            _announcement.transform.localScale = Vector3.Lerp(
                _announcement.transform.localScale, Vector3.one, 1f - Mathf.Exp(-10f * dt));
            if (Time.time >= _announceUntil)
                _announcement.gameObject.SetActive(false);
        }
    }

    float _cyanShownLerp = 1f, _magentaShownLerp = 1f;

    void Scale(RectTransform fill, float target, float ease, ref float shown)
    {
        shown = Mathf.Lerp(shown, target, ease);
        fill.localScale = new Vector3(Mathf.Clamp01(shown), 1f, 1f);
    }

    // ------------------------------------------------------------ helpers

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    static Text MakeText(Transform parent, string name, string content, int size,
        Color color, FontStyle style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    static void MakeButton(Transform parent, string label, float y, System.Action onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", new Color(0.06f, 0.14f, 0.22f, 0.95f));
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(430, 84);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(0.10f, 0.30f, 0.42f, 1f);
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        button.onClick.AddListener(() => onClick());

        var underline = MakeImage(image.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.8f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(8, 0);
        underline.rectTransform.offsetMax = new Vector2(-8, 3);

        MakeText(image.transform, "Label", label, 30, Color.white, FontStyle.Bold);
    }
}

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
    const float PortraitSize = 84f;

    Canvas _canvas;
    RectTransform _cyanFill, _cyanGhost, _magentaFill, _magentaGhost;
    float _cyanShown = 1f, _magentaShown = 1f;
    float _cyanGhostShown = 1f, _magentaGhostShown = 1f;
    Image[] _cyanPips, _magentaPips;
    Text _timer;
    Text _announcement;
    float _announceUntil;
    GameObject _endPanel;
    Text _cyanMove, _magentaMove;
    float _cyanMoveUntil, _magentaMoveUntil;

    public static BrawlHud Build(Transform parent, string cyanName, string magentaName,
        string stageName = null, Texture cyanPortrait = null, Texture magentaPortrait = null)
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

        hud.BuildBars(canvasGo.transform, cyanName, magentaName, cyanPortrait, magentaPortrait);
        hud.BuildTimer(canvasGo.transform);
        hud.BuildAnnouncement(canvasGo.transform);
        hud.BuildChargeMeters(canvasGo.transform);
        hud.BuildHelpButton(canvasGo.transform);

        // Which stage the roll landed on — RANDOM made it a mystery.
        if (!string.IsNullOrEmpty(stageName))
        {
            var stage = MakeText(canvasGo.transform, "StageName", stageName, 18,
                new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold);
            var rect = stage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -88f);
            rect.sizeDelta = new Vector2(600f, 24f);
        }
        return hud;
    }

    void BuildBars(Transform parent, string cyanName, string magentaName,
        Texture cyanPortrait, Texture magentaPortrait)
    {
        float cyanInset = BuildPortrait(parent, true, cyanName, cyanPortrait, HoloCyan);
        float magentaInset = BuildPortrait(parent, false, magentaName, magentaPortrait, HoloMagenta);
        _cyanFill = BuildBar(parent, true, cyanName, HoloCyan, cyanInset, out _cyanGhost, out _cyanPips);
        _magentaFill = BuildBar(parent, false, magentaName, HoloMagenta, magentaInset, out _magentaGhost, out _magentaPips);
        _cyanMove = BuildMoveCaption(parent, true, HoloCyan, cyanInset);
        _magentaMove = BuildMoveCaption(parent, false, HoloMagenta, magentaInset);
    }

    /// <summary>
    /// The fighter's face in the corner — a live RenderTexture of the actual
    /// robot — so whose bar is whose needs no reading. Returns how far the
    /// bar row must shift inward to make room.
    /// </summary>
    float BuildPortrait(Transform parent, bool left, string name, Texture portrait, Color color)
    {
        if (portrait == null)
            return 0f;
        float sign = left ? 1f : -1f;
        var anchor = new Vector2(left ? 0f : 1f, 1f);

        var frame = MakeImage(parent, $"Portrait_{name}", BarBack);
        var frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = anchor;
        frameRect.pivot = anchor;
        frameRect.anchoredPosition = new Vector2(sign * 40f, -34f);
        frameRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);

        var raw = new GameObject("Face").AddComponent<RawImage>();
        raw.transform.SetParent(frame.transform, false);
        raw.texture = portrait;
        var rawRect = raw.rectTransform;
        rawRect.anchorMin = Vector2.zero;
        rawRect.anchorMax = Vector2.one;
        rawRect.offsetMin = new Vector2(3f, 3f);
        rawRect.offsetMax = new Vector2(-3f, -3f);

        // A team-colour sill under the face ties it to its bar.
        var sill = MakeImage(frame.transform, "Sill", new Color(color.r, color.g, color.b, 0.9f));
        sill.rectTransform.anchorMin = Vector2.zero;
        sill.rectTransform.anchorMax = new Vector2(1f, 0f);
        sill.rectTransform.offsetMin = new Vector2(3f, 0f);
        sill.rectTransform.offsetMax = new Vector2(-3f, 3f);

        return PortraitSize + 12f;
    }

    /// <summary>
    /// The move ticker under each fighter's name: every attack announces
    /// itself by name as it starts, so a spectator can READ the fight —
    /// half the fun of watching is knowing the crescent kick was one.
    /// </summary>
    Text BuildMoveCaption(Transform parent, bool left, Color color, float inset)
    {
        float sign = left ? 1f : -1f;
        var anchor = new Vector2(left ? 0f : 1f, 1f);
        var caption = MakeText(parent, left ? "Move_P1" : "Move_P2", "", 27,
            Color.Lerp(color, Color.white, 0.35f), FontStyle.BoldAndItalic);
        var rect = caption.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = new Vector2(sign * (44f + inset), -102f);
        rect.sizeDelta = new Vector2(480, 32);
        caption.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        caption.gameObject.SetActive(false);
        return caption;
    }

    /// <summary>Flash a move name under one side's health bar.</summary>
    public void ShowMove(bool cyanSide, string moveName)
    {
        var caption = cyanSide ? _cyanMove : _magentaMove;
        if (caption == null)
            return;
        caption.text = moveName;
        caption.gameObject.SetActive(true);
        caption.transform.localScale = Vector3.one * 1.30f;   // pops, then settles
        var c = caption.color; c.a = 1f; caption.color = c;
        if (cyanSide) _cyanMoveUntil = Time.time + 1.0f;
        else _magentaMoveUntil = Time.time + 1.0f;
    }

    RectTransform BuildBar(Transform parent, bool left, string name, Color color,
        float inset, out RectTransform ghost, out Image[] pips)
    {
        float sign = left ? 1f : -1f;
        var anchor = new Vector2(left ? 0f : 1f, 1f);

        var back = MakeImage(parent, $"Bar_{name}", BarBack);
        var backRect = back.rectTransform;
        backRect.anchorMin = backRect.anchorMax = anchor;
        backRect.pivot = anchor;
        backRect.anchoredPosition = new Vector2(sign * (40f + inset), -34f);
        backRect.sizeDelta = new Vector2(BarWidth, BarHeight);

        // Ghost under fill: it lingers at the old health and eases down, so
        // a combo's total bite stays readable for a beat.
        ghost = MakeFill(back.transform, "Ghost", Ghost, left);
        var fill = MakeFill(back.transform, "Fill", color, left);

        var label = MakeText(parent, $"Name_{name}", name.ToUpperInvariant(), 22, color, FontStyle.Bold);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = anchor;
        labelRect.pivot = anchor;
        labelRect.anchoredPosition = new Vector2(sign * (44f + inset), -70f);
        labelRect.sizeDelta = new Vector2(400, 28);
        label.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

        pips = new Image[2];
        for (int i = 0; i < pips.Length; i++)
        {
            var pip = MakeImage(parent, $"Pip_{name}_{i}", new Color(1f, 1f, 1f, 0.15f));
            var pipRect = pip.rectTransform;
            pipRect.anchorMin = pipRect.anchorMax = anchor;
            pipRect.pivot = anchor;
            pipRect.anchoredPosition = new Vector2(sign * (44f + inset + 410f + i * 34f), -70f);
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

    RectTransform _cyanCharge, _magentaCharge;
    Image _cyanChargeImage, _magentaChargeImage;
    float _cyanChargeValue, _magentaChargeValue;

    void BuildChargeMeters(Transform parent)
    {
        _cyanCharge = BuildCharge(parent, true, out _cyanChargeImage);
        _magentaCharge = BuildCharge(parent, false, out _magentaChargeImage);
    }

    RectTransform BuildCharge(Transform parent, bool left, out Image fillImage)
    {
        float sign = left ? 1f : -1f;
        var anchor = new Vector2(left ? 0f : 1f, 0f);

        var back = MakeImage(parent, left ? "Charge_P1" : "Charge_P2", BarBack);
        var backRect = back.rectTransform;
        backRect.anchorMin = backRect.anchorMax = anchor;
        backRect.pivot = anchor;
        backRect.anchoredPosition = new Vector2(sign * 40f, 26f);
        backRect.sizeDelta = new Vector2(320f, 16f);

        var label = MakeText(parent, left ? "ChargeLabel_P1" : "ChargeLabel_P2",
            "BLAST", 16, new Color(1f, 1f, 1f, 0.5f), FontStyle.Bold);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = anchor;
        labelRect.pivot = anchor;
        labelRect.anchoredPosition = new Vector2(sign * 44f, 46f);
        labelRect.sizeDelta = new Vector2(200, 20);
        label.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

        var fill = MakeFill(back.transform, "Fill", left ? HoloCyan : HoloMagenta, left);
        fillImage = fill.GetComponent<Image>();
        fill.localScale = new Vector3(0f, 1f, 1f);
        return fill;
    }

    public void SetCharge(float cyan, float magenta)
    {
        _cyanChargeValue = cyan;
        _magentaChargeValue = magenta;
    }

    GameObject _helpPanel;

    void BuildHelpButton(Transform parent)
    {
        var image = MakeImage(parent, "HelpButton", BarBack);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        // Below the portrait frame, which owns the corner itself.
        rect.anchoredPosition = new Vector2(-40f, -130f);
        rect.sizeDelta = new Vector2(46f, 46f);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(ToggleHelp);

        var mark = MakeText(image.transform, "Mark", "?", 28, HoloCyan, FontStyle.Bold);
        var markRect = mark.rectTransform;
        markRect.anchorMin = Vector2.zero;
        markRect.anchorMax = Vector2.one;
        markRect.offsetMin = markRect.offsetMax = Vector2.zero;
    }

    void ToggleHelp()
    {
        if (_helpPanel == null)
            BuildHelpPanel();
        else
            _helpPanel.SetActive(!_helpPanel.activeSelf);
    }

    /// <summary>
    /// The HOW TO PLAY card. Deliberately no pause behind it — the hit-stop
    /// restorer resets any timeScale below 1, so a paused overlay would
    /// silently unpause itself; reading between rounds works fine.
    /// </summary>
    void BuildHelpPanel()
    {
        _helpPanel = new GameObject("HelpPanel");
        _helpPanel.transform.SetParent(_canvas.transform, false);
        var stretch = _helpPanel.AddComponent<RectTransform>();
        stretch.anchorMin = Vector2.zero;
        stretch.anchorMax = Vector2.one;
        stretch.offsetMin = stretch.offsetMax = Vector2.zero;

        var dim = MakeImage(_helpPanel.transform, "Dim", new Color(0.01f, 0.03f, 0.06f, 0.85f));
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;
        // The dim itself closes the card, so a tap anywhere gets back to
        // the fight — no hunting for the button on a tablet.
        var dimButton = dim.gameObject.AddComponent<Button>();
        dimButton.targetGraphic = dim;
        dimButton.onClick.AddListener(ToggleHelp);

        var title = MakeText(_helpPanel.transform, "Title", "HOW  TO  PLAY", 54, HoloCyan, FontStyle.Bold);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 250f);
        titleRect.sizeDelta = new Vector2(900f, 70f);

        var body = MakeText(_helpPanel.transform, "Body",
            "MOVE  —  W / S close in and back off   ·   A / D circle around\n" +
            "        (you always face your opponent — the whole arena is yours)\n" +
            "JUMP  —  SPACE   (or JUMP)\n" +
            "PUNCH  —  J   ·   a different kung fu punch every press\n" +
            "KICK  —  K   ·   a different kick every press — in the air: FLYING KICK\n" +
            "BLOCK  —  hold C (or SHIFT)   ·   a guard takes no damage\n" +
            "PHOTON BLAST  —  L when the meter below your bar is full\n" +
            "\n" +
            "Landing hits fills your BLAST meter. Getting hit fills it a little too.\n" +
            "Every move calls its name under the fighter's health bar.\n" +
            "The arena fights too: geysers launch you, mines and fire hurt EVERYONE,\n" +
            "and the green repair kit heals whoever grabs it first.\n" +
            "Win the round: empty their health, or lead when time runs out.\n" +
            "\n" +
            "=  —  show the buttons     F3  —  hitboxes     ESC  —  menu",
            26, new Color(1f, 1f, 1f, 0.92f), FontStyle.Normal);
        body.alignment = TextAnchor.UpperLeft;
        var bodyRect = body.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRect.anchoredPosition = new Vector2(0f, -40f);
        bodyRect.sizeDelta = new Vector2(980f, 480f);

        MakeButton(_helpPanel.transform, "GOT  IT", -290, ToggleHelp);
    }

    // ---------------------------------------------------------------- API

    public void SetHealth(float cyan, float magenta)
    {
        _cyanShown = cyan;
        _magentaShown = magenta;
    }

    int _lastTimerShown = -1;

    public void SetTimer(float seconds)
    {
        int shown = Mathf.Max(0, Mathf.CeilToInt(seconds));
        // The last ten seconds pop on every tick — the clock must be
        // impossible to miss once it matters.
        if (shown != _lastTimerShown && shown <= 10)
            _timer.transform.localScale = Vector3.one * 1.4f;
        _lastTimerShown = shown;
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

        _timer.transform.localScale = Vector3.Lerp(
            _timer.transform.localScale, Vector3.one, 1f - Mathf.Exp(-8f * dt));

        if (_announcement.gameObject.activeSelf)
        {
            _announcement.transform.localScale = Vector3.Lerp(
                _announcement.transform.localScale, Vector3.one, 1f - Mathf.Exp(-10f * dt));
            if (Time.time >= _announceUntil)
                _announcement.gameObject.SetActive(false);
        }

        UpdateCharge(_cyanCharge, _cyanChargeImage, _cyanChargeValue, HoloCyan);
        UpdateCharge(_magentaCharge, _magentaChargeImage, _magentaChargeValue, HoloMagenta);

        UpdateMoveCaption(_cyanMove, _cyanMoveUntil, dt);
        UpdateMoveCaption(_magentaMove, _magentaMoveUntil, dt);
    }

    static void UpdateMoveCaption(Text caption, float until, float dt)
    {
        if (caption == null || !caption.gameObject.activeSelf)
            return;
        caption.transform.localScale = Vector3.Lerp(
            caption.transform.localScale, Vector3.one, 1f - Mathf.Exp(-12f * dt));
        float left = until - Time.time;
        if (left <= 0f)
        {
            caption.gameObject.SetActive(false);
            return;
        }
        // The last third of its life fades out; a fresh move resets alpha.
        var c = caption.color;
        c.a = Mathf.Clamp01(left / 0.35f);
        caption.color = c;
    }

    static void UpdateCharge(RectTransform fill, Image image, float value, Color baseColor)
    {
        if (fill == null)
            return;
        fill.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
        // A full meter breathes white so READY reads from across the room.
        image.color = value >= 1f
            ? Color.Lerp(baseColor, Color.white, 0.4f + 0.4f * Mathf.PingPong(Time.time * 2.5f, 1f))
            : baseColor;
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

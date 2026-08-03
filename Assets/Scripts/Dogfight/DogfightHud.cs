using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What a pilot needs while the stick is busy: both shields, the score, the
/// speed, a reticle that admits when the assist has the target, and an arrow
/// to the enemy when the sky has swallowed them. The AI WAR broadcast swaps
/// the reticle for a caption naming whose canopy you are riding.
///
/// Bars are SCALED RECTS, never <c>Image.fillAmount</c> — every Image in this
/// project's runtime UI is a sprite-less rect, and fillAmount on one is
/// silently discarded (the bar just stays full, no error). TankRaidHud's rule,
/// TankRaidHud's shapes.
/// </summary>
public class DogfightHud : MonoBehaviour
{
    const int PipsToWin = 5;

    static readonly Color Panel = new Color(0.04f, 0.09f, 0.14f, 0.72f);
    static readonly Color Warn = new Color(1f, 0.75f, 0.2f);
    static readonly Color Danger = new Color(1f, 0.35f, 0.3f);

    Color _cyan;
    Color _magenta;

    RectTransform _cyanFill;
    Image _cyanImage;
    RectTransform _magentaFill;
    Image _magentaImage;
    Image[] _cyanPips;
    Image[] _magentaPips;
    Text _speed;
    RawImage _reticle;
    Image _reticleDot;
    RectTransform _arrow;
    Text _caption;
    Text _banner;
    float _bannerUntil;
    GameObject _over;
    Text _overTitle;
    Text _overBody;

    public static DogfightHud Build(Transform parent)
    {
        var go = new GameObject("DogfightHud");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<DogfightHud>();
        hud.Compose();
        return hud;
    }

    void Compose()
    {
        _cyan = MatchAnnouncer.TeamColor(0);
        _magenta = MatchAnnouncer.TeamColor(1);

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // TankRaidHud's slot: above the touch layers, below the menus.
        canvas.sortingOrder = 17;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        BuildShieldBar(true, out _cyanFill, out _cyanImage);
        BuildShieldBar(false, out _magentaFill, out _magentaImage);
        BuildScorePips();
        BuildReticle();
        BuildArrow();

        _speed = Label("Speed", "26 m/s", 22, new Color(1f, 1f, 1f, 0.7f), FontStyle.Bold,
            new Vector2(0f, 0f), new Vector2(120f, 46f), new Vector2(200f, 30f));

        _caption = Label("Caption", "", 20, new Color(1f, 1f, 1f, 0.62f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(900f, 28f));

        _banner = Label("Banner", "", 46, _cyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(1200f, 70f));
        _banner.gameObject.SetActive(false);

        BuildOverPanel();
    }

    /// <summary>One team's shield, top corner of its own side. The away bar is
    /// how a pilot on someone's tail watches the work land.</summary>
    void BuildShieldBar(bool cyanSide, out RectTransform fill, out Image fillImage)
    {
        Color team = cyanSide ? _cyan : _magenta;
        Vector2 anchor = cyanSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        float x = cyanSide ? 230f : -230f;

        Box($"ShieldFrame_{(cyanSide ? "C" : "M")}", Panel, anchor,
            new Vector2(x, -52f), new Vector2(420f, 34f)).transform.SetAsFirstSibling();
        var track = Box($"ShieldTrack_{(cyanSide ? "C" : "M")}", new Color(0f, 0f, 0f, 0.45f),
            anchor, new Vector2(x, -52f), new Vector2(404f, 20f));

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(track.transform, false);
        fillImage = fillGo.AddComponent<Image>();
        fillImage.color = team;
        fill = fillImage.rectTransform;
        // Pivoted at the OUTER screen edge, so both bars drain toward the
        // middle — the same read whichever seat you take.
        float pivotX = cyanSide ? 0f : 1f;
        fill.anchorMin = new Vector2(pivotX, 0f);
        fill.anchorMax = new Vector2(pivotX, 1f);
        fill.pivot = new Vector2(pivotX, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2(404f, 0f);

        Label($"ShieldLabel_{(cyanSide ? "C" : "M")}",
            MatchAnnouncer.TeamName(cyanSide ? 0 : 1), 15, new Color(1f, 1f, 1f, 0.55f),
            FontStyle.Bold, anchor, new Vector2(cyanSide ? 64f : -396f, -26f),
            new Vector2(120f, 20f));
    }

    /// <summary>First to five, counted in pips under the top centre — the
    /// TankRaid lives vocabulary, one row per team.</summary>
    void BuildScorePips()
    {
        Box("ScoreFrame", Panel, new Vector2(0.5f, 1f), new Vector2(0f, -52f),
            new Vector2(2f * PipsToWin * 24f + 60f, 42f));
        _cyanPips = new Image[PipsToWin];
        _magentaPips = new Image[PipsToWin];
        for (int i = 0; i < PipsToWin; i++)
        {
            // Cyan grows leftward from the middle, magenta rightward — score
            // marches toward your own corner of the screen.
            _cyanPips[i] = Box($"CyanPip{i}", _cyan, new Vector2(0.5f, 1f),
                new Vector2(-22f - i * 24f, -52f), new Vector2(16f, 16f));
            _magentaPips[i] = Box($"MagentaPip{i}", _magenta, new Vector2(0.5f, 1f),
                new Vector2(22f + i * 24f, -52f), new Vector2(16f, 16f));
        }
    }

    void BuildReticle()
    {
        var ringGo = new GameObject("Reticle");
        ringGo.transform.SetParent(transform, false);
        _reticle = ringGo.AddComponent<RawImage>();
        _reticle.texture = VfxUtil.RingTexture;
        _reticle.color = new Color(1f, 1f, 1f, 0.55f);
        _reticle.raycastTarget = false;
        var rect = _reticle.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(46f, 46f);

        _reticleDot = Box("ReticleDot", new Color(1f, 1f, 1f, 0.8f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
    }

    /// <summary>The way home to an off-screen enemy: a chevron orbiting the
    /// reticle, pointed along the screen direction to fly.</summary>
    void BuildArrow()
    {
        var group = new GameObject("TargetArrow");
        group.transform.SetParent(transform, false);
        _arrow = group.AddComponent<RectTransform>();
        _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
        _arrow.sizeDelta = Vector2.zero;

        var shaft = new GameObject("Shaft").AddComponent<Image>();
        shaft.transform.SetParent(group.transform, false);
        shaft.color = Warn;
        shaft.raycastTarget = false;
        shaft.rectTransform.sizeDelta = new Vector2(26f, 8f);
        shaft.rectTransform.anchoredPosition = new Vector2(-8f, 0f);

        var head = new GameObject("Head").AddComponent<Image>();
        head.transform.SetParent(group.transform, false);
        head.color = Warn;
        head.raycastTarget = false;
        head.rectTransform.sizeDelta = new Vector2(13f, 13f);
        head.rectTransform.anchoredPosition = new Vector2(8f, 0f);
        head.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);

        _arrow.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ updates

    public void SetShields(float cyanNormalized, float magentaNormalized)
    {
        SetBar(_cyanFill, _cyanImage, _cyan, cyanNormalized);
        SetBar(_magentaFill, _magentaImage, _magenta, magentaNormalized);
    }

    void SetBar(RectTransform fill, Image image, Color team, float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        fill.sizeDelta = new Vector2(404f * normalized, 0f);
        image.color = normalized > 0.34f ? team : normalized > 0.16f ? Warn : Danger;
    }

    public void SetScore(int cyan, int magenta)
    {
        for (int i = 0; i < PipsToWin; i++)
        {
            _cyanPips[i].color = i < cyan ? _cyan : new Color(1f, 1f, 1f, 0.14f);
            _magentaPips[i].color = i < magenta ? _magenta : new Color(1f, 1f, 1f, 0.14f);
        }
    }

    public void SetSpeed(float metresPerSecond, bool boosting)
    {
        _speed.text = $"{Mathf.RoundToInt(metresPerSecond)} m/s";
        _speed.color = boosting ? Warn : new Color(1f, 1f, 1f, 0.7f);
    }

    /// <summary>Player mode shows the reticle; the broadcast hides it and
    /// captions the seat instead.</summary>
    public void SetReticleVisible(bool visible)
    {
        _reticle.gameObject.SetActive(visible);
        _reticleDot.gameObject.SetActive(visible);
    }

    /// <summary>Warm when the assist is holding a target — the "guns live"
    /// tell that replaces a lock-on tone.</summary>
    public void SetReticleHot(bool hot)
    {
        _reticle.color = hot ? Warn : new Color(1f, 1f, 1f, 0.55f);
        _reticleDot.color = hot ? Warn : new Color(1f, 1f, 1f, 0.8f);
    }

    /// <summary>Point the chevron along a screen-space direction, or hide it
    /// while the enemy is honestly in view.</summary>
    public void SetTargetArrow(Vector2 direction, bool visible)
    {
        _arrow.gameObject.SetActive(visible);
        if (!visible || direction.sqrMagnitude < 1e-5f)
            return;
        direction.Normalize();
        _arrow.anchoredPosition = direction * 175f;
        _arrow.localEulerAngles =
            new Vector3(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    public void SetCaption(string caption) => _caption.text = caption ?? "";

    public void Flash(string message, Color color, float seconds = 1.6f)
    {
        _banner.text = message;
        _banner.color = color;
        _banner.gameObject.SetActive(true);
        _bannerUntil = Time.time + seconds;
    }

    void Update()
    {
        if (_banner.gameObject.activeSelf && Time.time >= _bannerUntil)
            _banner.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------- end panel

    void BuildOverPanel()
    {
        _over = Box("OverPanel", new Color(0.02f, 0.05f, 0.09f, 0.9f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(760f, 340f)).gameObject;
        var tile = _over.GetComponent<Image>();
        tile.sprite = MainMenu.RoundedTile();
        tile.type = Image.Type.Sliced;

        _overTitle = Label(_over.transform, "OverTitle", "", 54, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(700f, 70f));
        _overBody = Label(_over.transform, "OverBody", "", 30, Color.white, FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(700f, 110f));

        _over.SetActive(false);
    }

    /// <summary>
    /// The end of the sortie. Buttons deliberately absent — the mode is
    /// entered from the menu and left with ESC or the MENU button, and a
    /// panel with its own PLAY AGAIN is a second, competing way out.
    /// </summary>
    public void ShowOver(string title, string body, Color accent)
    {
        _overTitle.text = title;
        _overTitle.color = accent;
        _overBody.text = body;
        _over.SetActive(true);
        SetReticleVisible(false);
        SetTargetArrow(Vector2.zero, false);
    }

    // ---------------------------------------------------------------- little bits

    Image Box(string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return image;
    }

    Text Label(string name, string content, int size, Color color, FontStyle style,
        Vector2 anchor, Vector2 position, Vector2 sizeDelta) =>
        Label(transform, name, content, size, color, style, anchor, position, sizeDelta);

    Text Label(Transform parent, string name, string content, int size, Color color,
        FontStyle style, Vector2 anchor, Vector2 position, Vector2 sizeDelta)
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
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }
}

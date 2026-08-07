using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the player needs to know while both thumbs are busy: how much shield is
/// left, how many continues, how far they have come, and what is in the gun.
///
/// Bars are SCALED RECTS, never <c>Image.fillAmount</c>. Every Image in this
/// project's runtime UI is a plain coloured rect with no sprite assigned, and
/// fillAmount is silently discarded on a sprite-less Image — the bar simply
/// never moves, with no error to explain why. A child rect pivoted at its left
/// edge and scaled on x is the shape that actually empties.
///
/// The weapon strip sits at the BOTTOM CENTRE rather than in a corner: it is the
/// one readout that changes under the player mid-fight, and the centre is the
/// only place a thumb is guaranteed not to be covering.
/// </summary>
public class TankRaidHud : MonoBehaviour
{
    const int Lives = 3;

    static readonly Color Cyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color Warn = new Color(1f, 0.75f, 0.2f);
    static readonly Color Danger = new Color(1f, 0.35f, 0.3f);
    static readonly Color Recruit = new Color(0.35f, 0.7f, 1f);
    static readonly Color Panel = new Color(0.04f, 0.09f, 0.14f, 0.72f);

    RectTransform _shieldFill;
    Image _shieldImage;
    Text _shieldText;
    Image[] _lifePips;
    Text _distance;
    Text _wrecks;
    Text _escort;
    Text _boons;
    Text _weapon;
    RectTransform _weaponClock;
    Image _weaponClockImage;
    GameObject[] _weaponStrip;
    Text _banner;
    float _bannerUntil;
    GameObject _over;
    Text _overBody;

    public static TankRaidHud Build(Transform parent)
    {
        var go = new GameObject("TankRaidHud");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<TankRaidHud>();
        hud.Compose();
        return hud;
    }

    void Compose()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the sticks (14) and TouchControls (15) so a readout is never
        // hidden behind a thumb ring, below the main menu (20).
        canvas.sortingOrder = 17;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        BuildShield();
        BuildScore();
        BuildWeaponStrip();

        _banner = Label("Banner", "", 46, Cyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(1200f, 70f));
        _banner.gameObject.SetActive(false);

        BuildOverPanel();
    }

    // ------------------------------------------------------------------ shield

    void BuildShield()
    {
        var frame = Box("ShieldFrame", Panel, new Vector2(0f, 1f),
            new Vector2(230f, -52f), new Vector2(420f, 34f));

        var track = Box("ShieldTrack", new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, 1f),
            new Vector2(230f, -52f), new Vector2(404f, 20f));

        // Pivoted hard left so scaling x empties it from the right — see the
        // class note on why this is not a filled Image.
        var fill = new GameObject("ShieldFill");
        fill.transform.SetParent(track.transform, false);
        _shieldImage = fill.AddComponent<Image>();
        _shieldImage.color = Cyan;
        _shieldFill = _shieldImage.rectTransform;
        _shieldFill.anchorMin = new Vector2(0f, 0f);
        _shieldFill.anchorMax = new Vector2(0f, 1f);
        _shieldFill.pivot = new Vector2(0f, 0.5f);
        _shieldFill.anchoredPosition = Vector2.zero;
        _shieldFill.sizeDelta = new Vector2(404f, 0f);

        Label("ShieldLabel", "SHIELD", 15, new Color(1f, 1f, 1f, 0.5f), FontStyle.Normal,
            new Vector2(0f, 1f), new Vector2(64f, -26f), new Vector2(120f, 20f));
        _shieldText = Label("ShieldValue", "100", 17, Color.white, FontStyle.Bold,
            new Vector2(0f, 1f), new Vector2(400f, -26f), new Vector2(120f, 20f));

        _lifePips = new Image[Lives];
        for (int i = 0; i < Lives; i++)
            _lifePips[i] = Box($"Life{i}", Cyan, new Vector2(0f, 1f),
                new Vector2(38f + i * 24f, -80f), new Vector2(16f, 16f));

        // The escort, beside the lives: both answer "how much of me is left".
        _escort = Label("Escort", "", 16, Recruit, FontStyle.Bold,
            new Vector2(0f, 1f), new Vector2(210f, -80f), new Vector2(240f, 22f));
        _escort.alignment = TextAnchor.MiddleLeft;

        // The trophy shelf, under everything. Empty — and therefore invisible —
        // until the first outpost comes down.
        _boons = Label("Boons", "", 17, Warn, FontStyle.Bold,
            new Vector2(0f, 1f), new Vector2(360f, -108f), new Vector2(700f, 24f));
        _boons.alignment = TextAnchor.MiddleLeft;

        // The frame is behind everything it frames; uGUI draws in hierarchy
        // order, so it has to go first.
        frame.transform.SetAsFirstSibling();
    }

    void BuildScore()
    {
        Box("ScoreFrame", Panel, new Vector2(1f, 1f), new Vector2(-160f, -52f),
            new Vector2(280f, 76f));
        _distance = Label("Distance", "0 m", 34, Warn, FontStyle.Bold,
            new Vector2(1f, 1f), new Vector2(-160f, -44f), new Vector2(260f, 42f));
        _wrecks = Label("Wrecks", "0 WRECKS", 16, new Color(1f, 1f, 1f, 0.62f), FontStyle.Normal,
            new Vector2(1f, 1f), new Vector2(-160f, -74f), new Vector2(260f, 24f));
    }

    void BuildWeaponStrip()
    {
        var frame = Box("WeaponFrame", Panel, new Vector2(0.5f, 0f),
            new Vector2(0f, 92f), new Vector2(430f, 52f));
        _weapon = Label("WeaponName", "CANNON", 24, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(410f, 30f));

        var track = Box("WeaponClockTrack", new Color(0f, 0f, 0f, 0.4f), new Vector2(0.5f, 0f),
            new Vector2(0f, 76f), new Vector2(400f, 7f));

        // Siblings rather than one parented group: each is anchored in CANVAS
        // space, and reparenting a uGUI rect keeps its world placement, not its
        // anchoring. Kept as a list so the strip can be hidden as one thing.
        _weaponStrip = new[] { frame.gameObject, _weapon.gameObject, track.gameObject };
        var fill = new GameObject("WeaponClockFill");
        fill.transform.SetParent(track.transform, false);
        _weaponClockImage = fill.AddComponent<Image>();
        _weaponClockImage.color = Warn;
        _weaponClock = _weaponClockImage.rectTransform;
        _weaponClock.anchorMin = new Vector2(0f, 0f);
        _weaponClock.anchorMax = new Vector2(0f, 1f);
        _weaponClock.pivot = new Vector2(0f, 0.5f);
        _weaponClock.anchoredPosition = Vector2.zero;
        _weaponClock.sizeDelta = new Vector2(400f, 0f);
    }

    // ------------------------------------------------------------------ updates

    /// <summary>Shield bar and its colour. Amber at a third, red at a sixth —
    /// the same four-step drain vocabulary the arena's shield bars use.</summary>
    public void SetShield(float normalized, float current)
    {
        normalized = Mathf.Clamp01(normalized);
        _shieldFill.sizeDelta = new Vector2(404f * normalized, 0f);
        _shieldImage.color = normalized > 0.34f ? Cyan : normalized > 0.16f ? Warn : Danger;
        _shieldText.text = Mathf.CeilToInt(Mathf.Max(0f, current)).ToString();
    }

    public void SetLives(int left)
    {
        for (int i = 0; i < _lifePips.Length; i++)
            _lifePips[i].color = i < left ? Cyan : new Color(1f, 1f, 1f, 0.14f);
    }

    public void SetProgress(int metres, int wrecks)
    {
        _distance.text = $"{metres} m";
        _wrecks.text = wrecks == 1 ? "1 WRECK" : $"{wrecks} WRECKS";
    }

    /// <summary>Recruits driving with you, out of how many you may keep. Blank
    /// while the escort is empty — a readout that says "0" every run until the
    /// first beacon is a readout that teaches nothing.</summary>
    public void SetEscort(int allies, int cap)
    {
        _escort.text = allies > 0 ? $"ESCORT {allies}/{cap}" : "";
    }

    /// <summary>What has been captured, as one line. See TankBoons.Summary.</summary>
    public void SetBoons(string summary)
    {
        _boons.text = summary;
    }

    /// <summary>
    /// The gun and how long it has left. <paramref name="secondsLeft"/> below
    /// zero means the permanent cannon, whose clock bar is hidden rather than
    /// shown full — a full bar that never moves reads as a bug.
    /// </summary>
    public void SetWeapon(string weaponName, float secondsLeft, float ofSeconds)
    {
        _weapon.text = weaponName == null ? "" : weaponName.ToUpperInvariant();
        bool timed = secondsLeft > 0f && ofSeconds > 0f;
        _weaponClockImage.enabled = timed;
        if (!timed)
            return;
        float left = Mathf.Clamp01(secondsLeft / ofSeconds);
        _weaponClock.sizeDelta = new Vector2(400f * left, 0f);
        _weaponClockImage.color = left > 0.25f ? Warn : Danger;
        _weapon.color = left > 0.25f ? Warn : Danger;
    }

    /// <summary>A line across the middle for a couple of seconds: a pod picked
    /// up, a life lost, a milestone passed.</summary>
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

    // -------------------------------------------------------------- the end panel

    void BuildOverPanel()
    {
        _over = Box("OverPanel", new Color(0.02f, 0.05f, 0.09f, 0.9f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(760f, 340f)).gameObject;
        var tile = _over.GetComponent<Image>();
        tile.sprite = MainMenu.RoundedTile();
        tile.type = Image.Type.Sliced;

        // Children of the panel from the outset, not reparented afterwards: a
        // uGUI rect moved between parents keeps its WORLD placement, which means
        // its anchored position no longer says where it is.
        Label(_over.transform, "OverTitle", "RUN OVER", 54, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(700f, 70f));
        _overBody = Label(_over.transform, "OverBody", "", 30, Color.white, FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(700f, 100f));

        _over.SetActive(false);
    }

    /// <summary>
    /// The end of a run. Buttons are deliberately absent: this mode is entered
    /// from the menu and left with ESC or the MENU button, and a panel with its
    /// own PLAY AGAIN would be a second, competing way out of a mode that
    /// already has one.
    /// </summary>
    public void ShowOver(int metres, int wrecks, int outposts, int best)
    {
        string haul = $"{metres} metres   ·   {wrecks} wrecked";
        if (outposts > 0)
            haul += outposts == 1 ? "   ·   1 outpost taken" : $"   ·   {outposts} outposts taken";
        _overBody.text = haul + "\n" + (metres >= best ? "A NEW RECORD" : $"best {best} m");
        _over.SetActive(true);
        foreach (var part in _weaponStrip)
            part.SetActive(false);
    }

    // -------------------------------------------------------------- little bits

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

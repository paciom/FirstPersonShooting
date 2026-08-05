using System.Collections.Generic;
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

    /// <summary>One pilot's row: their bar, their name, and the camera tag
    /// that says "you are watching this one".</summary>
    class PawnBar
    {
        public JetPawn pawn;
        public RectTransform fill;
        public Image fillImage;
        public Image frame;
        public Text label;
        public GameObject cameraTag;
    }

    readonly List<PawnBar> _pawnBars = new List<PawnBar>();
    Image[] _cyanPips;
    Image[] _magentaPips;
    Text _scoreText;
    Text _scoreHint;
    bool _numericScore;
    Text _speed;
    Text _form;
    RawImage _reticle;
    Image _reticleDot;
    RectTransform _arrow;
    Text _caption;
    Text _banner;
    float _bannerUntil;
    GameObject _over;
    Text _overTitle;
    Text _overBody;
    GameObject _helpPanel;
    GameObject _helpButton;
    GameObject[] _ordnanceParts;
    RectTransform _missileFill;
    Image _missileFillImage;
    Text _missileLabel;
    Image[] _flarePips;
    RawImage _lockDiamond;
    float _nextIncomingWarn;

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

        BuildScorePips();
        BuildReticle();
        BuildArrow();
        BuildOrdnanceRow();
        BuildLockDiamond();

        _speed = Label("Speed", "26 m/s", 22, new Color(1f, 1f, 1f, 0.7f), FontStyle.Bold,
            new Vector2(0f, 0f), new Vector2(120f, 46f), new Vector2(200f, 30f));
        _form = Label("Form", "JET", 18, new Color(1f, 1f, 1f, 0.75f), FontStyle.Bold,
            new Vector2(0f, 0f), new Vector2(120f, 76f), new Vector2(200f, 26f));

        _caption = Label("Caption", "", 20, new Color(1f, 1f, 1f, 0.62f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(900f, 28f));

        _banner = Label("Banner", "", 46, _cyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(1200f, 70f));
        _banner.gameObject.SetActive(false);

        BuildOverPanel();
    }

    /// <summary>
    /// One row per PILOT, stacked down each team's corner, draining toward
    /// the middle of the screen. Built once the cast is final; the camera
    /// tag — a little lens glyph at the inner end of the row — is lit on
    /// exactly one of them by <see cref="UpdatePawnBars"/>, so "which jet am
    /// I watching" is never a question. The player's own row says YOU.
    /// </summary>
    public void BuildPawnBars(IReadOnlyList<JetPawn> cyans, IReadOnlyList<JetPawn> magentas,
        JetPawn hero)
    {
        // Each row is FOUR siblings under the canvas (frame, track, label,
        // tag) — swept piece by piece; reaching for a shared parent here
        // would find the canvas itself.
        foreach (var bar in _pawnBars)
        {
            if (bar.frame != null)
                Destroy(bar.frame.gameObject);
            if (bar.fill != null && bar.fill.parent != null)
                Destroy(bar.fill.parent.gameObject);
            if (bar.label != null)
                Destroy(bar.label.gameObject);
            if (bar.cameraTag != null)
                Destroy(bar.cameraTag);
        }
        _pawnBars.Clear();

        for (int i = 0; i < cyans.Count; i++)
            BuildPawnBar(cyans[i], true, i, cyans[i] == hero);
        for (int i = 0; i < magentas.Count; i++)
            BuildPawnBar(magentas[i], false, i, false);
    }

    const float PawnBarWidth = 232f;

    void BuildPawnBar(JetPawn pawn, bool cyanSide, int row, bool isHero)
    {
        Color team = cyanSide ? _cyan : _magenta;
        Vector2 anchor = cyanSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        float x = cyanSide ? 150f : -150f;
        float y = -40f - row * 26f;

        var bar = new PawnBar { pawn = pawn };
        bar.frame = Box($"PawnBar_{(cyanSide ? "C" : "M")}{row}", Panel, anchor,
            new Vector2(x, y), new Vector2(PawnBarWidth + 12f, 20f));
        var track = Box("Track", new Color(0f, 0f, 0f, 0.45f), anchor,
            new Vector2(x, y), new Vector2(PawnBarWidth, 12f));

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(track.transform, false);
        bar.fillImage = fillGo.AddComponent<Image>();
        bar.fillImage.color = team;
        bar.fill = bar.fillImage.rectTransform;
        // Pivoted at the OUTER screen edge, so every bar drains toward the
        // middle — the same read whichever side it hangs from.
        float pivotX = cyanSide ? 0f : 1f;
        bar.fill.anchorMin = new Vector2(pivotX, 0f);
        bar.fill.anchorMax = new Vector2(pivotX, 1f);
        bar.fill.pivot = new Vector2(pivotX, 0.5f);
        bar.fill.anchoredPosition = Vector2.zero;
        bar.fill.sizeDelta = new Vector2(PawnBarWidth, 0f);

        // The name rides the OUTER end, the camera tag the INNER — the tag
        // points into the sky the camera is actually showing.
        string name = isHero ? "YOU"
            : $"{MatchAnnouncer.TeamName(cyanSide ? 0 : 1)} {row + 1}";
        bar.label = Label($"PawnLabel_{(cyanSide ? "C" : "M")}{row}", name, 12,
            new Color(1f, 1f, 1f, 0.6f), isHero ? FontStyle.Bold : FontStyle.Normal,
            anchor, new Vector2(cyanSide ? 24f : -24f, y), new Vector2(90f, 18f));
        bar.label.alignment = cyanSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        bar.label.rectTransform.pivot = new Vector2(cyanSide ? 0f : 1f, 0.5f);

        bar.cameraTag = BuildCameraTag(anchor,
            new Vector2(cyanSide ? x + PawnBarWidth * 0.5f + 26f : x - PawnBarWidth * 0.5f - 26f, y));
        _pawnBars.Add(bar);
    }

    /// <summary>A camera the size of a pea: body, lens ring, and the little
    /// red light that says LIVE. Primitive-built like every other glyph here.</summary>
    GameObject BuildCameraTag(Vector2 anchor, Vector2 at)
    {
        var body = Box("CameraTag", new Color(1f, 1f, 1f, 0.85f), anchor, at,
            new Vector2(16f, 11f));

        var lens = new GameObject("Lens");
        lens.transform.SetParent(body.transform, false);
        var ring = lens.AddComponent<RawImage>();
        ring.texture = VfxUtil.RingTexture;
        ring.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        ring.raycastTarget = false;
        ring.rectTransform.sizeDelta = new Vector2(9f, 9f);

        var light = Box("Rec", new Color(1f, 0.25f, 0.2f, 1f), anchor,
            at + new Vector2(6f, 7f), new Vector2(4f, 4f));
        light.transform.SetParent(body.transform, true);

        body.gameObject.SetActive(false);
        return body.gameObject;
    }

    /// <summary>Every pilot's truth, every frame — and the camera tag on the
    /// one being watched. A downed row dims to empty until its respawn.</summary>
    public void UpdatePawnBars(JetPawn subject)
    {
        foreach (var bar in _pawnBars)
        {
            if (bar.pawn == null)
                continue;
            float health = bar.pawn.Shield != null && !bar.pawn.IsDown
                ? bar.pawn.Shield.Normalized
                : 0f;
            bar.fill.sizeDelta = new Vector2(PawnBarWidth * Mathf.Clamp01(health), 0f);
            Color team = bar.pawn.Team == 0 ? _cyan : _magenta;
            bar.fillImage.color = health > 0.34f ? team : health > 0.16f ? Warn : Danger;

            bool downed = bar.pawn.IsDown;
            bar.frame.color = downed
                ? new Color(Panel.r, Panel.g, Panel.b, 0.3f)
                : Panel;
            var labelColor = bar.label.color;
            labelColor.a = downed ? 0.25f : 0.6f;
            bar.label.color = labelColor;

            if (bar.cameraTag != null)
                bar.cameraTag.SetActive(bar.pawn == subject);
        }
    }

    /// <summary>First to five, counted in pips under the top centre — the
    /// TankRaid lives vocabulary, one row per team. Bigger targets (team
    /// sorties run to five per pilot) trade the pips for numerals: twenty
    /// squares a side is an abacus, not a scoreboard.</summary>
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

        _scoreText = Label("ScoreText", "0  —  0", 30, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(280f, 40f));
        _scoreHint = Label("ScoreHint", "", 14, new Color(1f, 1f, 1f, 0.45f),
            FontStyle.Normal, new Vector2(0.5f, 1f), new Vector2(0f, -84f),
            new Vector2(280f, 20f));
        _scoreText.gameObject.SetActive(false);
        _scoreHint.gameObject.SetActive(false);
    }

    /// <summary>How many wrecks end the sortie — decides pips or numerals.</summary>
    public void SetScoreTarget(int target)
    {
        _numericScore = target > PipsToWin;
        for (int i = 0; i < PipsToWin; i++)
        {
            _cyanPips[i].gameObject.SetActive(!_numericScore);
            _magentaPips[i].gameObject.SetActive(!_numericScore);
        }
        _scoreText.gameObject.SetActive(_numericScore);
        _scoreHint.gameObject.SetActive(_numericScore);
        _scoreHint.text = $"FIRST  TO  {target}";
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

    public void SetScore(int cyan, int magenta)
    {
        if (_numericScore)
        {
            _scoreText.text = $"{cyan}  —  {magenta}";
            return;
        }
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

    /// <summary>What the watched pawn currently is — JET, ROBOT, TANK, or
    /// FOLDING between — in its team's colour.</summary>
    public void SetForm(string label, Color team)
    {
        _form.text = label;
        _form.color = Color.Lerp(team, Color.white, 0.35f);
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

    // ---------------------------------------------------------------- ordnance

    /// <summary>The pilot's bottom strip: the missile tube on the left of
    /// centre, the flare pocket on the right. Scaled-rect bar, as ever.</summary>
    void BuildOrdnanceRow()
    {
        var frame = Box("OrdnanceFrame", Panel, new Vector2(0.5f, 0f),
            new Vector2(0f, 96f), new Vector2(430f, 46f));

        _missileLabel = Label("MissileLabel", "MISSILE", 16, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0f), new Vector2(-140f, 106f), new Vector2(120f, 22f));
        var track = Box("MissileTrack", new Color(0f, 0f, 0f, 0.4f), new Vector2(0.5f, 0f),
            new Vector2(-55f, 106f), new Vector2(120f, 10f));
        var fill = new GameObject("MissileFill");
        fill.transform.SetParent(track.transform, false);
        _missileFillImage = fill.AddComponent<Image>();
        _missileFillImage.color = Warn;
        _missileFill = _missileFillImage.rectTransform;
        _missileFill.anchorMin = new Vector2(0f, 0f);
        _missileFill.anchorMax = new Vector2(0f, 1f);
        _missileFill.pivot = new Vector2(0f, 0.5f);
        _missileFill.anchoredPosition = Vector2.zero;
        _missileFill.sizeDelta = new Vector2(120f, 0f);

        var flareLabel = Label("FlareLabel", "FLARES", 16, new Color(1f, 0.78f, 0.35f),
            FontStyle.Bold, new Vector2(0.5f, 0f), new Vector2(60f, 106f), new Vector2(110f, 22f));
        _flarePips = new Image[JetPawn.FlareChargesMax];
        for (int i = 0; i < _flarePips.Length; i++)
        {
            _flarePips[i] = Box($"FlarePip{i}", new Color(1f, 0.78f, 0.35f),
                new Vector2(0.5f, 0f), new Vector2(130f + i * 26f, 106f), new Vector2(14f, 14f));
            _flarePips[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        }

        // Siblings toggled as a list, never re-parented into a group — a uGUI
        // rect moved between parents keeps its world placement while its
        // anchoring starts lying. TankRaidHud's weapon-strip arrangement.
        _ordnanceParts = new[]
        {
            frame.gameObject, _missileLabel.gameObject, track.gameObject,
            flareLabel.gameObject, _flarePips[0].gameObject,
            _flarePips[1].gameObject, _flarePips[2].gameObject,
        };
    }

    public void SetPilotRowVisible(bool visible)
    {
        if (_ordnanceParts == null)
            return;
        foreach (var part in _ordnanceParts)
            if (part != null)
                part.SetActive(visible);
    }

    public void SetOrdnance(float missileFraction, bool missileReady, int flares)
    {
        _missileFill.sizeDelta = new Vector2(120f * Mathf.Clamp01(missileFraction), 0f);
        _missileFillImage.color = missileReady ? Warn : new Color(1f, 1f, 1f, 0.35f);
        _missileLabel.color = missileReady ? Warn : new Color(1f, 1f, 1f, 0.4f);
        for (int i = 0; i < _flarePips.Length; i++)
            _flarePips[i].color = i < flares
                ? new Color(1f, 0.78f, 0.35f)
                : new Color(1f, 1f, 1f, 0.14f);
    }

    /// <summary>The seeker's diamond, parked on the candidate by viewport
    /// anchor so it survives any window shape. 0 hidden · 1 locking (dim,
    /// breathing) · 2 locked (warm, steady).</summary>
    void BuildLockDiamond()
    {
        var go = new GameObject("LockDiamond");
        go.transform.SetParent(transform, false);
        _lockDiamond = go.AddComponent<RawImage>();
        _lockDiamond.texture = VfxUtil.RingTexture;
        _lockDiamond.raycastTarget = false;
        _lockDiamond.rectTransform.sizeDelta = new Vector2(38f, 38f);
        _lockDiamond.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        go.SetActive(false);
    }

    public void SetLockDiamond(Vector3 viewport, int state)
    {
        bool show = state > 0 && viewport.z > 0f;
        _lockDiamond.gameObject.SetActive(show);
        if (!show)
            return;
        var rect = _lockDiamond.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(viewport.x, viewport.y);
        rect.anchoredPosition = Vector2.zero;
        if (state >= 2)
        {
            _lockDiamond.color = Warn;
            rect.sizeDelta = new Vector2(34f, 34f);
        }
        else
        {
            _lockDiamond.color = new Color(1f, 1f, 1f, 0.45f);
            float breathe = 40f + 8f * Mathf.Sin(Time.time * 9f);
            rect.sizeDelta = new Vector2(breathe, breathe);
        }
    }

    /// <summary>Somebody's missile has this cockpit's name on it. Throttled
    /// here so the caller can shout every frame and the screen still breathes.</summary>
    public void WarnIncoming()
    {
        if (Time.time < _nextIncomingWarn)
            return;
        _nextIncomingWarn = Time.time + 1.3f;
        Flash("INCOMING  —  F  FLARES", Danger, 0.7f);
    }

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

    // ------------------------------------------------------------------- help

    /// <summary>
    /// The "?" in the corner and the card it opens: every key, grouped the
    /// way the game thinks — per form, then the verbs that work everywhere.
    /// Built per mode, because the couch watching an AI WAR has three keys
    /// and a pilot has a cockpit's worth. Toggled by the button (desktop
    /// mouse) or the ? key; hidden while the touch sticks are up, where a
    /// keyboard card answers a question nobody asked.
    /// </summary>
    public void BuildHelp(bool playerControls)
    {
        var button = Box("HelpButton", Panel, new Vector2(0.5f, 1f),
            new Vector2(240f, -52f), new Vector2(38f, 38f));
        button.raycastTarget = true;
        Label(button.transform, "Mark", "?", 24, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        var click = button.gameObject.AddComponent<Button>();
        click.transition = Selectable.Transition.None;
        click.onClick.AddListener(ToggleHelp);
        _helpButton = button.gameObject;

        _helpPanel = Box("HelpPanel", new Color(0.02f, 0.05f, 0.09f, 0.94f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(860f, 560f)).gameObject;
        var tile = _helpPanel.GetComponent<Image>();
        tile.sprite = MainMenu.RoundedTile();
        tile.type = Image.Type.Sliced;
        // Last sibling: the card draws over every readout on this canvas.
        _helpPanel.transform.SetAsLastSibling();

        Label(_helpPanel.transform, "Title", "HOW  TO  PLAY", 40, Warn, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 236f), new Vector2(700f, 54f));

        string body = playerControls
            ? "JET   —   Mouse steers (the jet chases your cursor)   ·   W boost   ·   S brake\n" +
              "ROBOT / TANK   —   Mouse aims   ·   WASD moves   ·   SHIFT runs   ·   SPACE jumps\n" +
              "\n" +
              "Hold Click   —   guns   (SPACE too, in the air)\n" +
              "Right Click   —   missile (hold the ring on a target until it locks)\n" +
              "F   —   flares, when INCOMING flashes\n" +
              "T   —   transform:  jet  →  robot  →  tank  →  jet\n" +
              "C   —   chase / cockpit camera\n" +
              "\n" +
              "=   —   thumb sticks on any screen      ESC   —   menu"
            : "C   —   chase / cockpit camera\n" +
              "SPACE   —   jump the broadcast to the next jet\n" +
              "\n" +
              "The camera tag on a pilot's bar marks who you are watching.\n" +
              "\n" +
              "=   —   thumb sticks on any screen      ESC   —   menu";
        var text = Label(_helpPanel.transform, "Body", body, 24, Color.white,
            FontStyle.Normal, new Vector2(0.5f, 0.5f), new Vector2(0f, -50f),
            new Vector2(780f, 420f));
        text.alignment = TextAnchor.MiddleLeft;

        _helpPanel.SetActive(false);
    }

    public void ToggleHelp()
    {
        if (_helpPanel != null)
            _helpPanel.SetActive(!_helpPanel.activeSelf);
    }

    /// <summary>Touch players get buttons, not key cards — the "?" steps
    /// aside while the sticks are up (and takes its open card with it).</summary>
    public void SetHelpButtonVisible(bool visible)
    {
        if (_helpButton != null && _helpButton.activeSelf != visible)
            _helpButton.SetActive(visible);
        if (!visible && _helpPanel != null && _helpPanel.activeSelf)
            _helpPanel.SetActive(false);
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

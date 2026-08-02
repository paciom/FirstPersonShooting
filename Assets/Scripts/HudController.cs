using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a holographic-cyan HUD entirely at runtime (no scene wiring needed
/// during greybox): crosshair, shield bar, and de-rez score counter.
/// Attach to the player rig next to its EnergyShield.
/// </summary>
public class HudController : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f, 0.9f);
    static readonly Color HealthyGreen = new Color(0.35f, 0.95f, 0.5f, 0.95f);
    static readonly Color WarnAmber = new Color(1f, 0.72f, 0.15f, 0.95f);
    static readonly Color CriticalRed = new Color(1f, 0.28f, 0.2f, 0.95f);
    static readonly Color GhostAmber = new Color(1f, 0.7f, 0.25f, 0.85f);
    static readonly Color OverGold = new Color(1f, 0.88f, 0.35f, 0.95f);

    const float BarX = 60f;
    const float BarY = 52f;
    const float BarWidth = 420f;
    const float BarHeight = 34f;

    /// <summary>Notches in the bar, so it can be read as "3 blocks left" without the number.</summary>
    const int Segments = 10;

    /// <summary>Below this the bar goes red and breathes.</summary>
    const float CriticalFraction = 0.25f;

    /// <summary>How long the amber ghost holds at the old value before draining.</summary>
    const float GhostHold = 0.35f;
    const float GhostDrainPerSecond = 0.55f;

    EnergyShield _shield;
    RectTransform _shieldFill;
    RectTransform _shieldGhost;
    Image _shieldFillImage;
    Text _shieldText;
    float _ghostShown = 1f;
    float _lastNormalized = 1f;
    float _ghostHoldUntil;
    Text _scoreText;
    Text _weaponText;
    GameObject _canvasGo;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
    }

    void Start()
    {
        BuildCanvas();
        ScoreKeeper.OnScoreChanged += HandleScore;
        HandleScore(ScoreKeeper.Score);
    }

    void OnDestroy()
    {
        ScoreKeeper.OnScoreChanged -= HandleScore;
    }

    void Update()
    {
        // The combat HUD belongs to the first-person fighting modes (it bleeds
        // through the translucent menu backdrop otherwise).
        if (_canvasGo != null && GameModeController.Instance != null)
        {
            bool show = GameModeController.IsFirstPersonMatch(GameModeController.Instance.Mode);
            if (_canvasGo.activeSelf != show)
                _canvasGo.SetActive(show);
        }

        UpdateShieldBar();

        if (_weaponText != null && PlayerBrain.Local != null)
        {
            // Airdropped guns are on a clock — show it, since it's the whole
            // reason to go and grab the next pod.
            float left = PlayerBrain.Local.TreasureSecondsLeft;
            string timer = left > 0f && !float.IsInfinity(left) ? $"   {left:0}s" : "";
            _weaponText.text =
                $"[{PlayerBrain.Local.WeaponSlotLabel}] {PlayerBrain.Local.CurrentWeaponName}{timer}";
            // Label glows in the weapon's signature color — instant read of what's equipped.
            Color c = PlayerBrain.Local.CurrentWeaponColor;
            _weaponText.color = new Color(c.r, c.g, c.b, 0.95f);
        }
    }

    void HandleScore(int score)
    {
        if (_scoreText != null)
            _scoreText.text = $"DE-REZ  {score}";
    }

    /// <summary>
    /// The bar reads three ways at once: how far the fill has receded says how
    /// much is left, the amber ghost trailing it says what the last hit cost,
    /// and the number says it exactly. Colour alone used to be the entire
    /// readout — it told a player they were hurt, never how close to de-rezzing.
    /// </summary>
    void UpdateShieldBar()
    {
        if (_shield == null || _shieldFill == null)
            return;

        float n = Mathf.Clamp01(_shield.Normalized);

        // Healing pulls the ghost straight up with the fill; damage leaves it
        // behind for a beat and then drains it, so the size of the bite stays
        // on screen after the fill has already snapped down.
        if (n < _lastNormalized)
            _ghostHoldUntil = Time.time + GhostHold;
        if (n >= _ghostShown)
            _ghostShown = n;
        else if (Time.time >= _ghostHoldUntil)
            _ghostShown = Mathf.MoveTowards(_ghostShown, n, GhostDrainPerSecond * Time.deltaTime);
        _lastNormalized = n;

        SetBarFill(_shieldFill, n);
        SetBarFill(_shieldGhost, _ghostShown);

        // Airdropped overshield gets its own colour rather than a place on the
        // ramp — it is the one state where a full bar is worth MORE than full.
        Color tone = _shield.HasOvershield ? OverGold : ShieldTone(n);

        // Down to the last quarter the bar breathes. A colour change on its own
        // is easy to miss with a firefight happening over the top of it.
        if (n <= CriticalFraction && !_shield.IsDown)
            tone = Color.Lerp(tone, Color.white, 0.2f + 0.2f * Mathf.Sin(Time.time * 9f));
        _shieldFillImage.color = tone;

        // Ceil, so a sliver of shield never reads as the 0 that means de-rezzed.
        if (_shieldText != null)
            _shieldText.text = $"{Mathf.CeilToInt(_shield.Current)} / {Mathf.RoundToInt(_shield.maxShield)}";
    }

    /// <summary>
    /// Full-to-empty colour ramp, cyan → green → amber → red.
    ///
    /// It detours through green and amber instead of running cyan straight to
    /// red because those two are near-complementary: a direct lerp spends the
    /// middle of the bar in a desaturated olive, which reads as a rendering
    /// fault rather than a warning. Every stop here is a colour a player can
    /// name, which is the whole job of the ramp.
    /// </summary>
    static Color ShieldTone(float n)
    {
        if (n >= 0.70f)
            return Color.Lerp(HealthyGreen, HoloCyan, Mathf.InverseLerp(0.70f, 1f, n));
        if (n >= 0.40f)
            return Color.Lerp(WarnAmber, HealthyGreen, Mathf.InverseLerp(0.40f, 0.70f, n));
        if (n >= CriticalFraction)
            return Color.Lerp(CriticalRed, WarnAmber, Mathf.InverseLerp(CriticalFraction, 0.40f, n));
        return CriticalRed;
    }

    /// <summary>
    /// Drain a bar by SCALE, from its left-edge pivot.
    ///
    /// Not Image.fillAmount: these Images are minted at runtime with no sprite,
    /// and Image.OnPopulateMesh short-circuits to a plain full-rect quad when
    /// the sprite is null — every fillAmount written to one is silently
    /// discarded. That is exactly how this bar spent its life permanently full.
    /// </summary>
    static void SetBarFill(RectTransform fill, float fraction)
    {
        if (fill == null)
            return;
        Vector3 scale = fill.localScale;
        scale.x = Mathf.Clamp01(fraction);
        fill.localScale = scale;
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("HUD");
        _canvasGo = canvasGo;
        // Child of the player so the HUD disappears with them (e.g. AI v AI mode).
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Crosshair: four small bars around center.
        BuildCrosshairBar(canvas.transform, new Vector2(0, 14), new Vector2(2, 10));
        BuildCrosshairBar(canvas.transform, new Vector2(0, -14), new Vector2(2, 10));
        BuildCrosshairBar(canvas.transform, new Vector2(14, 0), new Vector2(10, 2));
        BuildCrosshairBar(canvas.transform, new Vector2(-14, 0), new Vector2(10, 2));

        BuildShieldBar(canvas.transform);

        // Score (top-center).
        _scoreText = MakeText(canvas.transform, "Score", "", 34, HoloCyan);
        var scoreRect = _scoreText.rectTransform;
        scoreRect.anchorMin = scoreRect.anchorMax = new Vector2(0.5f, 1f);
        scoreRect.anchoredPosition = new Vector2(0, -50);
        scoreRect.sizeDelta = new Vector2(400, 60);

        // Weapon name (bottom-right).
        _weaponText = MakeText(canvas.transform, "Weapon", "", 26, HoloCyan);
        _weaponText.alignment = TextAnchor.MiddleRight;
        var weaponRect = _weaponText.rectTransform;
        weaponRect.anchorMin = weaponRect.anchorMax = new Vector2(1f, 0f);
        weaponRect.pivot = new Vector2(1f, 0f);
        weaponRect.anchoredPosition = new Vector2(-50, 46);
        weaponRect.sizeDelta = new Vector2(420, 40);
    }

    /// <summary>
    /// Bottom-left shield readout, back to front: plate, amber ghost, coloured
    /// fill, segment notches, then the exact number over the top.
    /// </summary>
    void BuildShieldBar(Transform parent)
    {
        var label = MakeText(parent, "ShieldLabel", "SHIELD", 18,
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.7f));
        label.alignment = TextAnchor.LowerLeft;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = Vector2.zero;
        labelRect.pivot = Vector2.zero;
        labelRect.anchoredPosition = new Vector2(BarX, BarY + BarHeight + 5f);
        labelRect.sizeDelta = new Vector2(220, 22);

        var back = MakeImage(parent, "ShieldBarBg", new Color(0f, 0f, 0f, 0.55f));
        var backRect = back.rectTransform;
        backRect.anchorMin = backRect.anchorMax = Vector2.zero;
        backRect.pivot = Vector2.zero;
        backRect.anchoredPosition = new Vector2(BarX, BarY);
        backRect.sizeDelta = new Vector2(BarWidth, BarHeight);

        // Ghost first so the live fill draws over it — what shows is the strip
        // between them, which IS the damage just taken.
        _shieldGhost = MakeBarFill(back.transform, "ShieldGhost", GhostAmber);
        _shieldFill = MakeBarFill(back.transform, "ShieldFill", HoloCyan);
        _shieldFillImage = _shieldFill.GetComponent<Image>();

        // Notches over the fill: "about three blocks left" is a faster read
        // mid-fight than any number, and a young player can count them.
        for (int i = 1; i < Segments; i++)
        {
            var tick = MakeImage(back.transform, "Tick", new Color(0f, 0f, 0f, 0.45f));
            var tickRect = tick.rectTransform;
            tickRect.anchorMin = new Vector2(0f, 0f);
            tickRect.anchorMax = new Vector2(0f, 1f);
            tickRect.pivot = new Vector2(0.5f, 0.5f);
            tickRect.sizeDelta = new Vector2(2f, -8f);
            tickRect.anchoredPosition = new Vector2(BarWidth * i / Segments, 0f);
        }

        _shieldText = MakeText(back.transform, "ShieldNumber", "", 21, Color.white);
        _shieldText.alignment = TextAnchor.MiddleRight;
        var numberRect = _shieldText.rectTransform;
        numberRect.anchorMin = Vector2.zero;
        numberRect.anchorMax = Vector2.one;
        numberRect.offsetMin = new Vector2(8f, 0f);
        numberRect.offsetMax = new Vector2(-10f, 0f);
        // The number sits on top of the fill, so it needs its own contrast —
        // white-on-cyan is unreadable at a glance without it.
        var outline = _shieldText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    /// <summary>
    /// A bar layer that drains by scaling from its left edge. Pivot goes on
    /// before the offsets, matching BrawlHud's bars.
    /// </summary>
    RectTransform MakeBarFill(Transform back, string name, Color color)
    {
        var fill = MakeImage(back, name, color);
        var rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        return rect;
    }

    static Text MakeText(Transform parent, string name, string content, int size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    void BuildCrosshairBar(Transform parent, Vector2 offset, Vector2 size)
    {
        var img = MakeImage(parent, "Crosshair", HoloCyan);
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        img.rectTransform.anchoredPosition = offset;
        img.rectTransform.sizeDelta = size;
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        // Pure readout — the touch controls now sit over this corner, and a
        // raycastable HUD there would start eating taps meant for the stick.
        img.raycastTarget = false;
        return img;
    }
}

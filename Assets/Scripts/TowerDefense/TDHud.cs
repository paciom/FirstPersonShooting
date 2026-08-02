using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The defender's readouts and controls: credits (amber, as everywhere),
/// CORE energy (cyan — the actual score), the wave counter, the tower
/// build bar down the right edge, and the NEXT WAVE button that trades
/// patience for credits.
///
/// Lives on the TowerDefense object; the canvas is a child, so the whole
/// HUD dies with the session.
/// </summary>
public class TDHud : MonoBehaviour
{
    static readonly Color CreditAmber = new Color(1f, 0.72f, 0.25f);
    static readonly Color CoreCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color WarnRed = new Color(1f, 0.35f, 0.3f);
    static readonly Color ButtonFace = new Color(0.06f, 0.14f, 0.22f, 0.92f);

    Text _credits;
    Text _core;
    Text _wave;
    Text _robots;
    Text _waveButtonLabel;
    Button _waveButton;

    readonly System.Collections.Generic.List<(TDTowerDefinition def, Button button, Text label)>
        _towerButtons = new System.Collections.Generic.List<(TDTowerDefinition, Button, Text)>();
    readonly System.Collections.Generic.List<(TDDefenderDefinition def, Button button, Text label)>
        _robotButtons = new System.Collections.Generic.List<(TDDefenderDefinition, Button, Text)>();
    float _nextRefresh;

    void Awake()
    {
        var canvasGo = new GameObject("TDHud");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // Real buttons, so this canvas raycasts — which also makes the
        // placer's IsPointerOverGameObject guard true over it.
        canvasGo.AddComponent<GraphicRaycaster>();

        _credits = MakeReadout(canvasGo.transform, "Credits", 30, CreditAmber,
            new Vector2(-28f, -20f), new Vector2(360f, 40f));
        _core = MakeReadout(canvasGo.transform, "Core", 24, CoreCyan,
            new Vector2(-28f, -58f), new Vector2(360f, 32f));
        _wave = MakeReadout(canvasGo.transform, "Wave", 20, new Color(1f, 1f, 1f, 0.8f),
            new Vector2(-28f, -92f), new Vector2(360f, 28f));
        _robots = MakeReadout(canvasGo.transform, "Robots", 20, new Color(1f, 1f, 1f, 0.8f),
            new Vector2(-28f, -122f), new Vector2(360f, 28f));

        BuildBar(canvasGo.transform);
        BuildWaveButton(canvasGo.transform);
    }

    Text MakeReadout(Transform parent, string name, int size, Color color,
        Vector2 anchored, Vector2 sizeDelta)
    {
        var textGo = new GameObject(name);
        textGo.transform.SetParent(parent, false);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.UpperRight;
        text.color = color;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchored;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    /// <summary>
    /// The build column, right edge, vertically centred — the Commander
    /// hand position, and Commander's two-section layout too: structures
    /// on top, a gap, the robot hires below, and the rally flag under them.
    /// </summary>
    void BuildBar(Transform parent)
    {
        var towers = TDTowerCatalog.All;
        var robots = TDDefenderCatalog.All;
        const float rowHeight = 64f;
        const float sectionGap = 24f;
        int rows = towers.Length + robots.Length + 1;   // +1: the rally flag
        float top = ((rows - 1) * rowHeight + 2f * sectionGap) * 0.5f;

        for (int i = 0; i < towers.Length; i++)
        {
            var def = towers[i];
            var (button, label) = BarButton(parent, $"Tower_{def.key}", def.accent,
                top - i * rowHeight,
                () => TDController.Instance?.Placer?.Arm(def));
            _towerButtons.Add((def, button, label));
        }

        float robotTop = top - towers.Length * rowHeight - sectionGap;
        for (int i = 0; i < robots.Length; i++)
        {
            var def = robots[i];
            var (button, label) = BarButton(parent, $"Hire_{def.key}", CoreCyan,
                robotTop - i * rowHeight,
                () => TDGarrison.Instance?.TryHire(def));
            _robotButtons.Add((def, button, label));
        }

        float rallyY = robotTop - robots.Length * rowHeight - sectionGap;
        var (rallyButton, rallyLabel) = BarButton(parent, "RallyFlag", Color.white, rallyY,
            () => TDController.Instance?.Placer?.ArmRally());
        rallyButton.interactable = true;
        rallyLabel.text = "RALLY FLAG\nmove the defense line";
        rallyLabel.color = Color.white;
    }

    /// <summary>
    /// NEXT WAVE, bottom right — beside the build bar's feet, well clear of
    /// the mode hint strip along the bottom centre.
    /// </summary>
    void BuildWaveButton(Transform parent)
    {
        var buttonGo = new GameObject("NextWave");
        buttonGo.transform.SetParent(parent, false);
        var image = buttonGo.AddComponent<Image>();
        image.color = ButtonFace;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-14f, 90f);
        rect.sizeDelta = new Vector2(250f, 64f);

        _waveButton = buttonGo.AddComponent<Button>();
        _waveButton.targetGraphic = image;
        _waveButton.onClick.AddListener(() => TDWaves.Instance?.CallNextWave());

        var edgeGo = new GameObject("Edge");
        edgeGo.transform.SetParent(buttonGo.transform, false);
        var edge = edgeGo.AddComponent<Image>();
        edge.color = new Color(1f, 0.3f, 0.9f);
        edge.raycastTarget = false;
        var edgeRect = edge.rectTransform;
        edgeRect.anchorMin = new Vector2(0f, 0f);
        edgeRect.anchorMax = new Vector2(0f, 1f);
        edgeRect.pivot = new Vector2(0f, 0.5f);
        edgeRect.anchoredPosition = Vector2.zero;
        edgeRect.sizeDelta = new Vector2(5f, 0f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, false);
        _waveButtonLabel = labelGo.AddComponent<Text>();
        _waveButtonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _waveButtonLabel.fontSize = 17;
        _waveButtonLabel.fontStyle = FontStyle.Bold;
        _waveButtonLabel.alignment = TextAnchor.MiddleCenter;
        _waveButtonLabel.raycastTarget = false;
        var labelRect = _waveButtonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);
    }

    (Button, Text) BarButton(Transform parent, string name, Color accent, float y,
        UnityEngine.Events.UnityAction onClick)
    {
        var buttonGo = new GameObject(name);
        buttonGo.transform.SetParent(parent, false);
        var image = buttonGo.AddComponent<Image>();
        image.color = ButtonFace;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-14f, y);
        rect.sizeDelta = new Vector2(250f, 56f);

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var edgeGo = new GameObject("Edge");
        edgeGo.transform.SetParent(buttonGo.transform, false);
        var edge = edgeGo.AddComponent<Image>();
        edge.color = accent;
        edge.raycastTarget = false;
        var edgeRect = edge.rectTransform;
        edgeRect.anchorMin = new Vector2(0f, 0f);
        edgeRect.anchorMax = new Vector2(0f, 1f);
        edgeRect.pivot = new Vector2(0f, 0.5f);
        edgeRect.anchoredPosition = Vector2.zero;
        edgeRect.sizeDelta = new Vector2(5f, 0f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 17;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);

        return (button, label);
    }

    void OnEnable()
    {
        TDEconomy.OnCreditsChanged += HandleCreditsChanged;
        HandleCreditsChanged(TDEconomy.Credits);
    }

    void OnDisable()
    {
        TDEconomy.OnCreditsChanged -= HandleCreditsChanged;
    }

    void HandleCreditsChanged(int total)
    {
        _credits.text = $"CREDITS  {total:N0}";
    }

    void Update()
    {
        if (Time.time < _nextRefresh)
            return;
        _nextRefresh = Time.time + 0.25f;

        var match = TDMatch.Instance;
        if (match != null)
        {
            _core.text = $"CORE  {match.CoreEnergy} / {TDMatch.StartingCoreEnergy}";
            _core.color = match.CoreEnergy <= 3 ? WarnRed : CoreCyan;
        }

        var waves = TDWaves.Instance;
        if (waves != null)
        {
            _wave.text = waves.Wave == 0
                ? $"WAVE  — / {TDWaves.TotalWaves}"
                : $"WAVE  {waves.Wave} / {TDWaves.TotalWaves}";
            RefreshWaveButton(waves, match);
        }

        int corps = TDGarrison.DefenderCount;
        _robots.text = $"ROBOTS  {corps} / {TDGarrison.MaxDefenders}";

        foreach (var (def, button, label) in _towerButtons)
        {
            bool affordable = TDEconomy.Credits >= def.cost;
            button.interactable = affordable;
            label.text = Describe(def);
            label.color = affordable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }

        bool roomInCorps = corps < TDGarrison.MaxDefenders;
        foreach (var (def, button, label) in _robotButtons)
        {
            bool affordable = TDEconomy.Credits >= def.cost;
            button.interactable = affordable && roomInCorps;
            label.text = roomInCorps
                ? $"{def.displayName}\n{def.cost} cr   ·   {def.role}"
                : $"{def.displayName}\ncorps full — {TDGarrison.MaxDefenders} robots";
            label.color = affordable && roomInCorps
                ? Color.white
                : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    void RefreshWaveButton(TDWaves waves, TDMatch match)
    {
        bool over = match != null && match.IsOver;
        bool canCall = !over && waves.IsBuildPhase && waves.Wave < TDWaves.TotalWaves;
        _waveButton.interactable = canCall;

        if (over)
        {
            _waveButtonLabel.text = "—";
        }
        else if (canCall)
        {
            int seconds = Mathf.CeilToInt(waves.BuildSecondsLeft);
            _waveButtonLabel.text = $"NEXT WAVE  ·  {seconds}s\ncall now  +{seconds * 2} cr";
        }
        else
        {
            int incoming = waves.AliveCount + waves.RemainingToSpawn;
            _waveButtonLabel.text = $"WAVE {waves.Wave} ATTACKING\n{incoming} raiders left";
        }
        _waveButtonLabel.fontSize = 17;
    }

    static string Describe(TDTowerDefinition def)
    {
        switch (def.kind)
        {
            case TDTowerKind.Stasis:
                return $"{def.displayName}\n{def.cost} cr   ·   slows raiders";
            case TDTowerKind.Refinery:
                return $"{def.displayName}\n{def.cost} cr   ·   +{def.incomePerTick} cr / {def.tickSeconds:0}s";
            case TDTowerKind.Rail:
                return $"{def.displayName}\n{def.cost} cr   ·   sniper";
            case TDTowerKind.Mortar:
                return $"{def.displayName}\n{def.cost} cr   ·   splash";
            default:
                return $"{def.displayName}\n{def.cost} cr   ·   rapid fire";
        }
    }
}

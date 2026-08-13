using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The commander's readouts and controls: credits (amber, like the crystal
/// it stands for), the power meter, and the build bar — a Red Alert-style
/// column of structure buttons down the right edge, gated live by prereqs
/// and price.
///
/// Lives on the Commander object; the canvas is a child, so the whole HUD
/// dies with the session.
/// </summary>
public class CommanderHud : MonoBehaviour
{
    static readonly Color CreditAmber = new Color(1f, 0.72f, 0.25f);
    static readonly Color PowerGreen = new Color(0.55f, 1f, 0.4f);
    static readonly Color WarnRed = new Color(1f, 0.35f, 0.3f);
    static readonly Color ButtonFace = new Color(0.06f, 0.14f, 0.22f, 0.92f);

    Text _credits;
    Text _power;
    int _shownTeam;   // team 0 — the human seat; AI v AI revisits this in Phase 5

    readonly System.Collections.Generic.List<(BuildingDefinition def, Button button, Text label)>
        _buildButtons = new System.Collections.Generic.List<(BuildingDefinition, Button, Text)>();
    readonly System.Collections.Generic.List<(UnitDefinition def, Button button, Text label)>
        _unitButtons = new System.Collections.Generic.List<(UnitDefinition, Button, Text)>();
    float _nextButtonRefresh;

    void Awake()
    {
        var canvasGo = new GameObject("CommanderHud");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // The build bar is real uGUI buttons, so this canvas raycasts — which
        // also makes CommanderSelection's IsPointerOverGameObject guard true
        // over it, keeping build clicks out of the battlefield.
        canvasGo.AddComponent<GraphicRaycaster>();

        _credits = MakeReadout(canvasGo.transform, "Credits", 30, CreditAmber,
            new Vector2(-28f, -20f), new Vector2(360f, 40f));
        _power = MakeReadout(canvasGo.transform, "Power", 20, PowerGreen,
            new Vector2(-28f, -58f), new Vector2(360f, 28f));

        BuildBar(canvasGo.transform);
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
        // Readouts never eat clicks — only the build buttons raycast.
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchored;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    /// <summary>
    /// The construction column, right edge, vertically centred — where a
    /// Red Alert hand expects it. Structures on top (Command Center skipped:
    /// each side owns exactly one, pre-placed), a gap, then the factory's
    /// unit orders below.
    /// </summary>
    void BuildBar(Transform parent)
    {
        var buildingDefs = new System.Collections.Generic.List<BuildingDefinition>();
        foreach (var def in BuildingCatalog.All)
            if (!def.isHeadquarters)
                buildingDefs.Add(def);
        var unitDefs = UnitCatalog.All;

        const float rowHeight = 64f;
        const float sectionGap = 26f;
        int rows = buildingDefs.Count + unitDefs.Length;
        float top = ((rows - 1) * rowHeight + sectionGap) * 0.5f;

        for (int i = 0; i < buildingDefs.Count; i++)
        {
            var def = buildingDefs[i];
            var (button, label) = BarButton(parent, $"Build_{def.key}", def.accent,
                top - i * rowHeight,
                () => CommanderController.Instance?.Placer?.Arm(def));
            _buildButtons.Add((def, button, label));
            string info = $"{def.cost} cr   ·   " +
                (def.power >= 0 ? $"+{def.power}" : def.power.ToString()) + " power" +
                (def.prerequisite != null
                    ? $"\nneeds {BuildingCatalog.Get(def.prerequisite)?.displayName}" : "") +
                $"\n\n{def.description}";
            WireInfo(button, def.displayName, info);
        }

        float unitTop = top - buildingDefs.Count * rowHeight - sectionGap;
        for (int i = 0; i < unitDefs.Length; i++)
        {
            var def = unitDefs[i];
            var (button, label) = BarButton(parent, $"Train_{def.key}", CreditAmber,
                unitTop - i * rowHeight,
                () => TryTrain(def));
            _unitButtons.Add((def, button, label));
            string info = $"{def.cost} cr   ·   builds in ~{def.BuildSeconds:0} s" +
                (def.prerequisite != null
                    ? $"\nneeds {BuildingCatalog.Get(def.prerequisite)?.displayName}" : "") +
                $"\n\n{def.description}";
            WireInfo(button, def.displayName, info);
        }

        BuildInfoPanel(parent);
        BuildAdvice(parent, top);
    }

    // ------------------------------------------------------------- info panel

    Image _infoPanel;
    Text _infoTitle;
    Text _infoBody;

    /// <summary>
    /// Hovering (or pressing, which is what a finger does) any bar button
    /// opens the dossier: name, price, prerequisites, and what the thing IS —
    /// a commander should know what they are buying before they buy it.
    /// </summary>
    void WireInfo(Button button, string title, string body)
    {
        var trigger = button.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
        { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowInfo(title, body));
        var press = new UnityEngine.EventSystems.EventTrigger.Entry
        { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
        press.callback.AddListener(_ => ShowInfo(title, body));
        var exit = new UnityEngine.EventSystems.EventTrigger.Entry
        { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideInfo());
        trigger.triggers.Add(enter);
        trigger.triggers.Add(press);
        trigger.triggers.Add(exit);
    }

    void BuildInfoPanel(Transform parent)
    {
        var panelGo = new GameObject("InfoPanel");
        panelGo.transform.SetParent(parent, false);
        _infoPanel = panelGo.AddComponent<Image>();
        _infoPanel.color = new Color(0.02f, 0.06f, 0.10f, 0.94f);
        _infoPanel.raycastTarget = false;
        var rect = _infoPanel.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        // Just left of the bar column.
        rect.anchoredPosition = new Vector2(-14f - 250f - 10f, 0f);
        rect.sizeDelta = new Vector2(320f, 190f);

        _infoTitle = MakeReadout(panelGo.transform, "Title", 20, Color.white,
            new Vector2(0f, -10f), new Vector2(0f, 28f));
        _infoTitle.alignment = TextAnchor.UpperLeft;
        _infoBody = MakeReadout(panelGo.transform, "Body", 16,
            new Color(1f, 1f, 1f, 0.8f), new Vector2(0f, -42f), new Vector2(0f, 140f));
        _infoBody.alignment = TextAnchor.UpperLeft;
        foreach (var text in new[] { _infoTitle, _infoBody })
        {
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.offsetMin = new Vector2(14f, textRect.offsetMin.y);
            textRect.offsetMax = new Vector2(-12f, textRect.offsetMax.y);
        }

        panelGo.SetActive(false);
    }

    void ShowInfo(string title, string body)
    {
        if (_infoPanel == null)
            return;
        _infoTitle.text = title;
        _infoBody.text = body;
        _infoPanel.gameObject.SetActive(true);
    }

    void HideInfo()
    {
        if (_infoPanel != null)
            _infoPanel.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------- advisor

    Text _adviceBanner;
    Image _nowChip;
    Text _nowChipText;
    CommanderAdvisor.Advice _advice;

    /// <summary>
    /// The BUILD NOW voice: a banner above the bar saying what and why, and
    /// a pulsing chip pointing at the recommended button. Same rule chain
    /// the AI plays by, so following it is literally keeping pace.
    /// </summary>
    void BuildAdvice(Transform parent, float barTop)
    {
        _adviceBanner = MakeReadout(parent, "Advice", 17, CreditAmber,
            new Vector2(-14f, 0f), new Vector2(560f, 46f));
        var bannerRect = _adviceBanner.rectTransform;
        bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(1f, 0.5f);
        bannerRect.pivot = new Vector2(1f, 0f);
        bannerRect.anchoredPosition = new Vector2(-14f, barTop + 40f);
        _adviceBanner.alignment = TextAnchor.LowerRight;

        var chipGo = new GameObject("NowChip");
        chipGo.transform.SetParent(parent, false);
        _nowChip = chipGo.AddComponent<Image>();
        _nowChip.color = CreditAmber;
        _nowChip.raycastTarget = false;
        var chipRect = _nowChip.rectTransform;
        chipRect.anchorMin = chipRect.anchorMax = new Vector2(1f, 0.5f);
        chipRect.pivot = new Vector2(1f, 0.5f);
        chipRect.sizeDelta = new Vector2(64f, 24f);

        var chipTextGo = new GameObject("Label");
        chipTextGo.transform.SetParent(chipGo.transform, false);
        _nowChipText = chipTextGo.AddComponent<Text>();
        _nowChipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _nowChipText.fontSize = 13;
        _nowChipText.fontStyle = FontStyle.Bold;
        _nowChipText.alignment = TextAnchor.MiddleCenter;
        _nowChipText.color = new Color(0.08f, 0.05f, 0f);
        _nowChipText.text = "NOW ▶";
        _nowChipText.raycastTarget = false;
        var chipTextRect = _nowChipText.rectTransform;
        chipTextRect.anchorMin = Vector2.zero;
        chipTextRect.anchorMax = Vector2.one;
        chipTextRect.offsetMin = chipTextRect.offsetMax = Vector2.zero;

        chipGo.SetActive(false);
    }

    void RefreshAdvice()
    {
        _advice = CommanderAdvisor.Recommend(_shownTeam);

        var name = _advice.isUnit
            ? UnitCatalog.Get(_advice.key)?.displayName
            : BuildingCatalog.Get(_advice.key)?.displayName;
        _adviceBanner.text = name == null ? "" : $"BUILD NOW:  {name}\n{_advice.reason}";

        // Park the chip beside the recommended button.
        Button target = null;
        if (_advice.isUnit)
        {
            foreach (var (def, button, _) in _unitButtons)
                if (def.key == _advice.key) { target = button; break; }
        }
        else
        {
            foreach (var (def, button, _) in _buildButtons)
                if (def.key == _advice.key) { target = button; break; }
        }
        if (target == null)
        {
            _nowChip.gameObject.SetActive(false);
            return;
        }
        var buttonRect = (RectTransform)target.transform;
        _nowChip.rectTransform.anchoredPosition =
            new Vector2(-14f - 250f - 6f, buttonRect.anchoredPosition.y);
        _nowChip.gameObject.SetActive(true);
    }

    /// <summary>
    /// Pay first, then queue on the least-loaded factory. Refund on the one
    /// race that can lose the order — every factory filling up between the
    /// button refresh and the click.
    /// </summary>
    void TryTrain(UnitDefinition def)
    {
        if (!CommanderEconomy.Spend(_shownTeam, def.cost))
            return;
        var queue = ProductionQueue.LeastBusy(_shownTeam);
        if (queue == null || !queue.Enqueue(def.key))
            CommanderEconomy.Grant(_shownTeam, def.cost);
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

        // Accent edge on the left — the building's own colour, amber for units.
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
        CommanderEconomy.OnCreditsChanged += HandleCreditsChanged;
        Refresh();
    }

    void OnDisable()
    {
        CommanderEconomy.OnCreditsChanged -= HandleCreditsChanged;
    }

    void HandleCreditsChanged(int teamId, int total)
    {
        if (teamId == _shownTeam)
            Refresh();
    }

    void Update()
    {
        // The NOW chip breathes every frame — a still chip is furniture, a
        // pulsing one is a finger tapping the board.
        if (_nowChip != null && _nowChip.gameObject.activeSelf)
        {
            var pulse = CreditAmber;
            pulse.a = 0.55f + 0.45f * Mathf.PingPong(Time.time * 1.6f, 1f);
            _nowChip.color = pulse;
        }

        // Slow tick: power and prereqs change when buildings rise or fall,
        // which no event announces yet — and at six buttons, polling is
        // cheaper than plumbing one through.
        if (Time.time < _nextButtonRefresh)
            return;
        _nextButtonRefresh = Time.time + 0.4f;
        RefreshPower();
        RefreshButtons();
        RefreshUnitButtons();
        RefreshAdvice();
    }

    void Refresh()
    {
        _credits.text = $"CREDITS  {CommanderEconomy.Credits(_shownTeam):N0}";
    }

    void RefreshPower()
    {
        int supply = CommanderPower.Supply(_shownTeam);
        int draw = CommanderPower.Draw(_shownTeam);
        bool brownOut = draw > supply;
        _power.text = $"POWER  {supply} / {draw}";
        _power.color = brownOut ? WarnRed : PowerGreen;
    }

    void RefreshButtons()
    {
        foreach (var (def, button, label) in _buildButtons)
        {
            bool unlocked = def.prerequisite == null || HasBuilding(def.prerequisite);
            bool affordable = CommanderEconomy.Credits(_shownTeam) >= def.cost;
            button.interactable = unlocked && affordable;

            string power = def.power >= 0 ? $"+{def.power}" : def.power.ToString();
            label.text = unlocked
                ? $"{def.displayName}\n{def.cost} cr   ·   {power} pw"
                : $"{def.displayName}\nneeds {BuildingCatalog.Get(def.prerequisite)?.displayName}";
            label.color = unlocked && affordable
                ? Color.white
                : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    void RefreshUnitButtons()
    {
        bool hasFactory = HasBuilding(BuildingCatalog.Factory);
        bool hasRoom = ProductionQueue.LeastBusy(_shownTeam) != null;

        foreach (var (def, button, label) in _unitButtons)
        {
            bool unlocked = hasFactory
                && (def.prerequisite == null || HasBuilding(def.prerequisite));
            bool affordable = CommanderEconomy.Credits(_shownTeam) >= def.cost;
            button.interactable = unlocked && affordable && hasRoom;

            if (!hasFactory)
            {
                label.text = $"{def.displayName}\nneeds ROBOT FACTORY";
            }
            else if (!unlocked)
            {
                label.text = $"{def.displayName}\nneeds {BuildingCatalog.Get(def.prerequisite)?.displayName}";
            }
            else
            {
                int queued = ProductionQueue.TotalQueued(_shownTeam, def.key);
                label.text = queued > 0
                    ? $"{def.displayName}\n{def.cost} cr   ·   {queued} in build"
                    : $"{def.displayName}\n{def.cost} cr";
            }
            label.color = unlocked && affordable && hasRoom
                ? Color.white
                : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    bool HasBuilding(string key)
    {
        foreach (var building in Building.All)
            if (building != null && building.TeamId == _shownTeam && building.IsAlive
                && building.Definition != null && building.Definition.key == key)
                return true;
        return false;
    }
}

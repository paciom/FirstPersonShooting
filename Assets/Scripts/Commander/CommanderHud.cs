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
    /// Red Alert hand expects it. One button per catalog entry; the Command
    /// Center is skipped (each side owns exactly one, pre-placed).
    /// </summary>
    void BuildBar(Transform parent)
    {
        var defs = new System.Collections.Generic.List<BuildingDefinition>();
        foreach (var def in BuildingCatalog.All)
            if (!def.isHeadquarters)
                defs.Add(def);

        float rowHeight = 64f;
        float top = (defs.Count - 1) * rowHeight * 0.5f;
        for (int i = 0; i < defs.Count; i++)
        {
            var def = defs[i];

            var buttonGo = new GameObject($"Build_{def.key}");
            buttonGo.transform.SetParent(parent, false);
            var image = buttonGo.AddComponent<Image>();
            image.color = ButtonFace;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-14f, top - i * rowHeight);
            rect.sizeDelta = new Vector2(250f, 56f);

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            var captured = def;
            button.onClick.AddListener(() =>
                CommanderController.Instance?.Placer?.Arm(captured));

            // Accent edge on the left, the building's own colour.
            var edgeGo = new GameObject("Edge");
            edgeGo.transform.SetParent(buttonGo.transform, false);
            var edge = edgeGo.AddComponent<Image>();
            edge.color = def.accent;
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

            _buildButtons.Add((def, button, label));
        }
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
        // Slow tick: power and prereqs change when buildings rise or fall,
        // which no event announces yet — and at six buttons, polling is
        // cheaper than plumbing one through.
        if (Time.time < _nextButtonRefresh)
            return;
        _nextButtonRefresh = Time.time + 0.4f;
        RefreshPower();
        RefreshButtons();
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

    bool HasBuilding(string key)
    {
        foreach (var building in Building.All)
            if (building != null && building.TeamId == _shownTeam && building.IsAlive
                && building.Definition != null && building.Definition.key == key)
                return true;
        return false;
    }
}

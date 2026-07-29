using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The broadcast graphics: one intel panel per commander, so an audience can
/// read the whole war at a glance — treasury and power, army and structures,
/// the AI's own account of its posture, and two live production rows that
/// SHOW the work rather than describe it: the robot on the assembly line and
/// the structure being saved for, each as a spinning 3D portrait
/// (CommanderPortraits) over a real progress bar.
///
/// Spectator (AI WAR) sessions only; the player mode has its own HUD and a
/// human who already knows what they are doing. Everything is polled on a
/// half-second tick — these are scoreboard numbers, not gameplay.
/// </summary>
public class CommanderWarPanels : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    const float PanelWidth = 330f;
    const float PanelHeight = 540f;

    static readonly Color PanelFace = new Color(0.02f, 0.05f, 0.08f, 0.82f);

    class ProductionRow
    {
        public GameObject root;
        public RawImage portrait;
        public Text title;
        public Text detail;
        public RectTransform barFill;
        public Image barFillImage;
    }

    readonly Text[] _stats = new Text[2];
    readonly Text[] _operation = new Text[2];
    readonly Text[] _feed = new Text[2];
    readonly RectTransform[] _panels = new RectTransform[2];
    readonly ProductionRow[] _factoryRows = new ProductionRow[2];
    readonly ProductionRow[] _projectRows = new ProductionRow[2];

    CommanderPortraits _portraits;
    float _nextRefresh;

    void Awake()
    {
        _portraits = gameObject.AddComponent<CommanderPortraits>();

        var canvasGo = new GameObject("WarPanels");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 14;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // No GraphicRaycaster: broadcast graphics never eat a click.

        for (int team = 0; team < 2; team++)
            BuildPanel(canvasGo.transform, team);
    }

    /// <summary>Cyan reads from the left edge, magenta from the right.</summary>
    void BuildPanel(Transform parent, int team)
    {
        Color tint = MatchAnnouncer.TeamColor(team);
        float side = team == 0 ? 1f : -1f;

        var panelGo = new GameObject($"Panel_{team}");
        panelGo.transform.SetParent(parent, false);
        var face = panelGo.AddComponent<Image>();
        face.color = PanelFace;
        face.raycastTarget = false;
        var rect = face.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(team == 0 ? 0f : 1f, 1f);
        rect.pivot = new Vector2(team == 0 ? 0f : 1f, 1f);
        rect.anchoredPosition = new Vector2(14f * side, -16f);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        _panels[team] = rect;

        // Team-colour header bar with the commander's name.
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(panelGo.transform, false);
        var header = headerGo.AddComponent<Image>();
        header.color = new Color(tint.r, tint.g, tint.b, 0.25f);
        header.raycastTarget = false;
        var headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, 40f);

        MakeText(panelGo.transform, "Title", team == 0 ? "CYAN  COMMAND" : "MAGENTA  COMMAND",
            22, tint, FontStyle.Bold, new Vector2(0f, -6f), 30f);

        _stats[team] = MakeText(panelGo.transform, "Stats", "", 18, Color.white,
            FontStyle.Normal, new Vector2(0f, -48f), 140f);

        _factoryRows[team] = BuildRow(panelGo.transform, "FactoryRow", -192f, tint);
        _operation[team] = MakeText(panelGo.transform, "Operation", "", 18, tint,
            FontStyle.Bold, new Vector2(0f, -294f), 26f);
        _projectRows[team] = BuildRow(panelGo.transform, "ProjectRow", -324f, tint);

        _feed[team] = MakeText(panelGo.transform, "Feed", "", 15,
            new Color(1f, 1f, 1f, 0.65f), FontStyle.Normal, new Vector2(0f, -428f), 104f);
    }

    /// <summary>
    /// A production row: 88px live portrait on the left; name, progress bar
    /// and a detail line on the right. Hidden whenever there is nothing to
    /// show — an empty frame reads as a broken one.
    /// </summary>
    ProductionRow BuildRow(Transform parent, string name, float y, Color tint)
    {
        var row = new ProductionRow();
        row.root = new GameObject(name);
        row.root.transform.SetParent(parent, false);
        var rowRect = row.root.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, y);
        rowRect.sizeDelta = new Vector2(0f, 96f);

        var frameGo = new GameObject("PortraitFrame");
        frameGo.transform.SetParent(row.root.transform, false);
        var frame = frameGo.AddComponent<Image>();
        frame.color = new Color(tint.r, tint.g, tint.b, 0.18f);
        frame.raycastTarget = false;
        var frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0.5f);
        frameRect.pivot = new Vector2(0f, 0.5f);
        frameRect.anchoredPosition = new Vector2(12f, 0f);
        frameRect.sizeDelta = new Vector2(92f, 92f);

        var portraitGo = new GameObject("Portrait");
        portraitGo.transform.SetParent(frameGo.transform, false);
        row.portrait = portraitGo.AddComponent<RawImage>();
        row.portrait.raycastTarget = false;
        var portraitRect = row.portrait.rectTransform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(2f, 2f);
        portraitRect.offsetMax = new Vector2(-2f, -2f);

        row.title = MakeText(row.root.transform, "Name", "", 17, Color.white,
            FontStyle.Bold, new Vector2(0f, -12f), 24f);
        row.title.rectTransform.offsetMin = new Vector2(116f, row.title.rectTransform.offsetMin.y);

        // The bar: dark back, team-colour fill driven by anchorMax.x.
        var backGo = new GameObject("BarBack");
        backGo.transform.SetParent(row.root.transform, false);
        var back = backGo.AddComponent<Image>();
        back.color = new Color(1f, 1f, 1f, 0.10f);
        back.raycastTarget = false;
        var backRect = back.rectTransform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(1f, 1f);
        backRect.pivot = new Vector2(0.5f, 1f);
        backRect.anchoredPosition = new Vector2(0f, -44f);
        backRect.offsetMin = new Vector2(116f, backRect.offsetMin.y);
        backRect.offsetMax = new Vector2(-12f, backRect.offsetMax.y);
        backRect.sizeDelta = new Vector2(backRect.sizeDelta.x, 12f);

        var fillGo = new GameObject("BarFill");
        fillGo.transform.SetParent(backGo.transform, false);
        row.barFillImage = fillGo.AddComponent<Image>();
        row.barFillImage.color = tint;
        row.barFillImage.raycastTarget = false;
        row.barFill = row.barFillImage.rectTransform;
        row.barFill.anchorMin = Vector2.zero;
        row.barFill.anchorMax = new Vector2(0f, 1f);
        row.barFill.offsetMin = new Vector2(1f, 1f);
        row.barFill.offsetMax = new Vector2(-1f, -1f);

        row.detail = MakeText(row.root.transform, "Detail", "", 15,
            new Color(1f, 1f, 1f, 0.65f), FontStyle.Normal, new Vector2(0f, -62f), 22f);
        row.detail.rectTransform.offsetMin = new Vector2(116f, row.detail.rectTransform.offsetMin.y);

        row.root.SetActive(false);
        return row;
    }

    Text MakeText(Transform parent, string name, string content, int size, Color color,
        FontStyle style, Vector2 anchored, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchored;
        rect.offsetMin = new Vector2(14f, rect.offsetMin.y);
        rect.offsetMax = new Vector2(-10f, rect.offsetMax.y);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        return text;
    }

    void Update()
    {
        if (Time.time < _nextRefresh)
            return;
        _nextRefresh = Time.time + RefreshSeconds;

        // The touch MENU button lives in the top-left corner (a higher-order
        // canvas): duck the cyan panel under it rather than wearing it as a
        // hat. Desktop keeps the top slot.
        if (_panels[0] != null)
            _panels[0].anchoredPosition = new Vector2(14f, TouchControls.Active ? -200f : -16f);

        for (int team = 0; team < 2; team++)
            RefreshPanel(team);
    }

    void RefreshPanel(int team)
    {
        // --- the ledger ---
        int fighters = 0, collectors = 0;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId != team || !unit.IsAlive)
                continue;
            if (unit is CommanderCollector) collectors++;
            else fighters++;
        }

        var structures = new System.Text.StringBuilder();
        int structureCount = 0;
        foreach (var def in BuildingCatalog.All)
        {
            int count = 0;
            foreach (var building in Building.All)
                if (building != null && building.TeamId == team && building.IsAlive
                    && building.Definition == def)
                    count++;
            structureCount += count;
            if (count == 0)
                continue;
            if (structures.Length > 0)
                structures.Append("   ");
            // Keys make honest abbreviations; display names don't ("ROBOT
            // FACTORY" would shorten to ROB).
            string shortName = def.isHeadquarters ? "HQ" : def.key.Substring(0, 3).ToUpper();
            structures.Append(count > 1 ? $"{shortName}x{count}" : shortName);
        }

        _stats[team].text =
            $"CREDITS   {CommanderEconomy.Credits(team):N0}\n" +
            $"POWER     {CommanderPower.Supply(team)} / {CommanderPower.Draw(team)}\n" +
            $"ARMY      {fighters} robots   ·   {collectors} collectors\n" +
            $"BASE      {structureCount} structures\n" +
            $"{structures}";

        // --- the assembly line: head of the busiest factory queue ---
        string headKey = null;
        float headProgress = 0f;
        int queued = 0;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != team || !building.IsAlive)
                continue;
            var queue = building.GetComponent<ProductionQueue>();
            if (queue == null || queue.QueueLength == 0)
                continue;
            queued += queue.QueueLength;
            if (queue.HeadProgress >= headProgress)
            {
                headProgress = queue.HeadProgress;
                headKey = queue.HeadKey;
            }
        }

        var unitDef = headKey != null ? UnitCatalog.Get(headKey) : null;
        if (unitDef != null)
            SetRow(_factoryRows[team], _portraits.UnitPortrait(unitDef.key, team),
                unitDef.displayName, headProgress,
                queued > 1 ? $"{queued} in the queue" : "on the line");
        else
            _factoryRows[team].root.SetActive(false);

        // --- the commander's own account of itself ---
        CommanderAI ai = null;
        foreach (var candidate in FindObjectsByType<CommanderAI>(FindObjectsSortMode.None))
            if (candidate.teamId == team)
                ai = candidate;
        _operation[team].text = ai != null ? ai.CurrentOperation : "";

        // --- the savings project: the structure being worked toward ---
        var projectDef = ai != null && ai.ProjectKey != null
            ? BuildingCatalog.Get(ai.ProjectKey) : null;
        if (projectDef != null)
            SetRow(_projectRows[team], _portraits.BuildingPortrait(projectDef.key, team),
                projectDef.displayName, ai.ProjectProgress,
                $"{CommanderEconomy.Credits(team):N0} / {projectDef.cost:N0} cr");
        else
            _projectRows[team].root.SetActive(false);

        // --- the feed, newest at the bottom, like a ticker ---
        var log = CommanderOps.Of(team);
        var feed = new System.Text.StringBuilder();
        for (int i = Mathf.Max(0, log.Count - 6); i < log.Count; i++)
            feed.AppendLine(log[i].message);
        _feed[team].text = feed.ToString();
    }

    static void SetRow(ProductionRow row, Texture portrait, string title, float progress,
        string detail)
    {
        if (!row.root.activeSelf)
            row.root.SetActive(true);
        row.portrait.texture = portrait;
        row.portrait.enabled = portrait != null;
        row.title.text = title;
        row.detail.text = detail;
        row.barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }
}

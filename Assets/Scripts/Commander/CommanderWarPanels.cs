using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The broadcast graphics: one intel panel per commander, so an audience can
/// read the whole war at a glance — treasury and power, army and structures,
/// what each AI says it is doing (its own CurrentOperation/CurrentProject),
/// what it is building right now, and a short feed of its latest acts from
/// CommanderOps.
///
/// Spectator (AI WAR) sessions only; the player mode has its own HUD and a
/// human who already knows what they are doing. Everything is polled on a
/// half-second tick — these are scoreboard numbers, not gameplay.
/// </summary>
public class CommanderWarPanels : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    const float PanelWidth = 330f;

    static readonly Color PanelFace = new Color(0.02f, 0.05f, 0.08f, 0.82f);

    readonly Text[] _stats = new Text[2];
    readonly Text[] _operation = new Text[2];
    readonly Text[] _feed = new Text[2];
    readonly RectTransform[] _panels = new RectTransform[2];
    float _nextRefresh;

    void Awake()
    {
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
        rect.sizeDelta = new Vector2(PanelWidth, 420f);
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
            FontStyle.Normal, new Vector2(0f, -48f), 150f);
        _operation[team] = MakeText(panelGo.transform, "Operation", "", 18, tint,
            FontStyle.Bold, new Vector2(0f, -200f), 78f);
        _feed[team] = MakeText(panelGo.transform, "Feed", "", 15,
            new Color(1f, 1f, 1f, 0.65f), FontStyle.Normal, new Vector2(0f, -282f), 130f);
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

        // --- the commander's own account of itself ---
        CommanderAI ai = null;
        foreach (var candidate in FindObjectsByType<CommanderAI>(FindObjectsSortMode.None))
            if (candidate.teamId == team)
                ai = candidate;

        string production = ProductionLine(team);
        _operation[team].text = ai == null
            ? production
            : string.IsNullOrEmpty(ai.CurrentProject)
                ? $"{ai.CurrentOperation}\n{production}"
                : $"{ai.CurrentOperation}\n{ai.CurrentProject}\n{production}";

        // --- the feed, newest at the bottom, like a ticker ---
        var log = CommanderOps.Of(team);
        var feed = new System.Text.StringBuilder();
        for (int i = Mathf.Max(0, log.Count - 6); i < log.Count; i++)
            feed.AppendLine(log[i].message);
        _feed[team].text = feed.ToString();
    }

    static string ProductionLine(int team)
    {
        int queued = 0;
        float bestProgress = -1f;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != team || !building.IsAlive)
                continue;
            var queue = building.GetComponent<ProductionQueue>();
            if (queue == null || queue.QueueLength == 0)
                continue;
            queued += queue.QueueLength;
            bestProgress = Mathf.Max(bestProgress, queue.HeadProgress);
        }
        return queued == 0
            ? ""
            : $"FACTORY   {Mathf.RoundToInt(bestProgress * 100f)}%   ·   {queued} queued";
    }
}

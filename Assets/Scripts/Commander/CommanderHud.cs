using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The commander's readouts. Phase 2: a credits counter, amber like the
/// crystal it stands for. The build bar and unit buttons grow onto this
/// canvas in later phases.
///
/// Lives on the Commander object; the canvas is a child, so the whole HUD
/// dies with the session.
/// </summary>
public class CommanderHud : MonoBehaviour
{
    static readonly Color CreditAmber = new Color(1f, 0.72f, 0.25f);

    Text _credits;
    int _shownTeam;   // team 0 — the human seat; AI v AI revisits this in Phase 5

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

        var textGo = new GameObject("Credits");
        textGo.transform.SetParent(canvasGo.transform, false);
        _credits = textGo.AddComponent<Text>();
        _credits.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _credits.fontSize = 30;
        _credits.fontStyle = FontStyle.Bold;
        _credits.alignment = TextAnchor.UpperRight;
        _credits.color = CreditAmber;
        // No raycastTarget: the HUD must never eat a click the battlefield
        // (or the touch MENU button) was meant to get.
        _credits.raycastTarget = false;
        var rect = _credits.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-28f, -20f);
        rect.sizeDelta = new Vector2(360f, 40f);
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

    void Refresh()
    {
        _credits.text = $"CREDITS  {CommanderEconomy.Credits(_shownTeam):N0}";
    }
}

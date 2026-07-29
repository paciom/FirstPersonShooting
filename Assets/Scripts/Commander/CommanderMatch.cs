using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The referee: watches for a fallen Command Center (or a team scoured to
/// zero structures), calls the war, and stages the ending — the loser's
/// remaining base and army de-rez in a rolling cascade, which is the closing
/// shot the whole no-death fiction was built to deliver. Then back to the
/// menu.
/// </summary>
public class CommanderMatch : MonoBehaviour
{
    const float CascadeStep = 0.28f;
    const float MenuReturnDelay = 8f;

    bool _over;

    void OnEnable()
    {
        Building.OnBuildingLost -= HandleBuildingLost;
        Building.OnBuildingLost += HandleBuildingLost;
    }

    void OnDisable()
    {
        Building.OnBuildingLost -= HandleBuildingLost;
    }

    void HandleBuildingLost(Building lost)
    {
        if (_over || lost == null)
            return;

        int team = lost.TeamId;
        bool hqDown = lost.Definition != null && lost.Definition.isHeadquarters;
        if (!hqDown && CountAlive(team) > 0)
            return;

        _over = true;
        StartCoroutine(EndMatch(loser: team));
    }

    static int CountAlive(int teamId)
    {
        int count = 0;
        foreach (var building in Building.All)
            if (building != null && building.TeamId == teamId && building.IsAlive)
                count++;
        return count;
    }

    IEnumerator EndMatch(int loser)
    {
        int winner = 1 - loser;

        // Commanders stand down — no rebuilding through the credits sequence.
        foreach (var ai in FindObjectsByType<CommanderAI>(FindObjectsSortMode.None))
            ai.enabled = false;
        if (CommanderController.Instance != null && CommanderController.Instance.Placer != null)
            CommanderController.Instance.Placer.Cancel();

        ShowBanner(winner);

        // The cascade: everything the loser still has folds into light, one
        // beat apart, buildings then robots — a wave of surrender rolling
        // across their base.
        var doomed = new System.Collections.Generic.List<EnergyShield>();
        foreach (var building in Building.All)
            if (building != null && building.TeamId == loser && building.IsAlive)
                doomed.Add(building.GetComponent<EnergyShield>());
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == loser && unit.IsAlive)
                doomed.Add(unit.GetComponent<EnergyShield>());

        foreach (var shield in doomed)
        {
            if (shield != null && !shield.IsDown)
                shield.TakeHit(999999f, shield.transform.position + Vector3.up);
            yield return new WaitForSeconds(CascadeStep);
        }

        yield return new WaitForSeconds(MenuReturnDelay - CascadeStep * doomed.Count > 2f
            ? MenuReturnDelay - CascadeStep * doomed.Count : 2f);

        GameModeController.Instance?.EnterMenu();
    }

    void ShowBanner(int winner)
    {
        Color tint = MatchAnnouncer.TeamColor(winner);
        string name = winner == 0 ? "CYAN" : "MAGENTA";

        var canvasGo = new GameObject("MatchBanner");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textGo = new GameObject("Verdict");
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 92;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = tint;
        text.text = $"{name}  WINS  THE  WAR";
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);
        rect.sizeDelta = new Vector2(1600f, 120f);

        var subGo = new GameObject("Sub");
        subGo.transform.SetParent(canvasGo.transform, false);
        var sub = subGo.AddComponent<Text>();
        sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sub.fontSize = 26;
        sub.alignment = TextAnchor.MiddleCenter;
        sub.color = new Color(1f, 1f, 1f, 0.6f);
        sub.text = "the defeated de-rez and return home   ·   back to the menu in a moment";
        sub.raycastTarget = false;
        var subRect = sub.rectTransform;
        subRect.anchorMin = subRect.anchorMax = new Vector2(0.5f, 0.5f);
        subRect.anchoredPosition = new Vector2(0f, 40f);
        subRect.sizeDelta = new Vector2(1200f, 40f);
    }
}

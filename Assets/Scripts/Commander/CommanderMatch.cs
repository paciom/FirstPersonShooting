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

    /// <summary>Enemy structures the player's side has brought down; the
    /// leaderboard's measure of a Commander match beyond the win itself.</summary>
    int _enemyBuildingsDown;

    void HandleBuildingLost(Building lost)
    {
        if (_over || lost == null)
            return;

        int team = lost.TeamId;
        if (team != 0)
            _enemyBuildingsDown++;
        bool hqDown = lost.Definition != null && lost.Definition.isHeadquarters;
        if (!hqDown && CountAlive(team) > 0)
            return;

        _over = true;
        StartCoroutine(EndMatch(loser: team));
    }

    /// <summary>
    /// A Commander match has no running score, so the leaderboard number is
    /// built from the end state: the win, the enemy buildings brought down,
    /// and what the player still has standing. Maps are rolled from a seed,
    /// so this goes to the mode's overall board only.
    /// </summary>
    void PostScore(bool playerWon)
    {
        int units = 0;
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == 0 && unit.IsAlive)
                units++;
        int score = (playerWon ? 1000 : 0) + _enemyBuildingsDown * 100
            + CountAlive(0) * 50 + units * 10;
        LeaderboardClient.Submit(GameMode.Commander, LeaderboardClient.AllArenas, score);
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
        bool playerLost = winner != 0 && CommanderController.Instance != null
            && CommanderController.Instance.PlayerCommands;
        GameAudio.PlayFlat(playerLost ? GameAudio.Id.Defeat : GameAudio.Id.Victory);
        if (CommanderController.Instance != null && CommanderController.Instance.PlayerCommands)
            PostScore(winner == 0);

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

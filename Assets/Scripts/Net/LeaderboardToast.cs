using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The little "NEW BEST" ribbon that drops in when a posted score moved the
/// player up a board. Lives on the LeaderboardClient object and listens to
/// <see cref="LeaderboardClient.Posted"/>, so every mode gets it with no
/// wiring of its own; it draws on its own overlay canvas above whatever
/// HUD the mode has.
///
/// A guest's local best gets the ribbon too, with a nudge to sign in --
/// that moment, right after a record, is when the account pitch lands.
/// </summary>
public class LeaderboardToast : MonoBehaviour
{
    const float ShowSeconds = 4.5f;

    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    static readonly Color Ink = new Color(0.02f, 0.06f, 0.11f, 0.92f);

    GameObject _canvasGo;
    Text _headline, _detail;
    Coroutine _hide;

    void OnEnable()
    {
        LeaderboardClient.Posted += OnPosted;
    }

    void OnDisable()
    {
        LeaderboardClient.Posted -= OnPosted;
    }

    void OnPosted(string modeKey, string arena, int score, LeaderboardClient.ScoreReply reply)
    {
        string unit = LeaderboardClient.ModeUnit(modeKey);
        string where = arena == LeaderboardClient.AllArenas
            ? LeaderboardClient.ModeTitle(modeKey)
            : arena.ToUpperInvariant();
        bool cumulative = LeaderboardClient.IsCumulative(modeKey);

        if (reply != null && reply.ok && reply.improved)
        {
            string headline = cumulative
                ? $"WIN  #{reply.best:N0}  ON  {where}"
                : $"NEW  BEST  ON  {where}";
            string detail = reply.rank > 0
                ? $"#{reply.rank}  on  the  board   ·   {reply.best:N0}  {unit}"
                : $"{reply.best:N0}  {unit}   ·   keep  climbing";
            Show(headline, detail);
            return;
        }
        if (reply != null)
            return;             // the server saw it and it was not a record

        // Offline or a guest: the local best moved, or it did not.
        var account = AccountClient.Instance;
        bool signedIn = account != null && account.SignedIn;
        int local = LeaderboardClient.LocalBest(modeKey, arena);
        if (local != score && !cumulative)
            return;
        Show(cumulative ? $"WIN  #{local:N0}  ON  {where}" : $"NEW  BEST  ON  {where}",
            signedIn ? $"{score:N0}  {unit}   ·   saved,  posting  when  you  are  back  online"
                     : $"{score:N0}  {unit}   ·   sign  in  to  get  on  the  board");
    }

    void Show(string headline, string detail)
    {
        if (_canvasGo == null)
            Build();
        _headline.text = headline;
        _detail.text = detail;
        _canvasGo.SetActive(true);
        if (_hide != null)
            StopCoroutine(_hide);
        _hide = StartCoroutine(HideAfter());
    }

    IEnumerator HideAfter()
    {
        yield return new WaitForSecondsRealtime(ShowSeconds);
        if (_canvasGo != null)
            _canvasGo.SetActive(false);
        _hide = null;
    }

    void Build()
    {
        _canvasGo = new GameObject("LeaderboardToast");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var box = new GameObject("Box").AddComponent<Image>();
        box.transform.SetParent(_canvasGo.transform, false);
        box.color = Ink;
        box.sprite = MainMenu.RoundedTile();
        box.type = Image.Type.Sliced;
        box.raycastTarget = false;
        var rect = box.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -120f);
        rect.sizeDelta = new Vector2(760f, 92f);

        var rail = new GameObject("Rail").AddComponent<Image>();
        rail.transform.SetParent(box.transform, false);
        rail.color = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
        rail.raycastTarget = false;
        var railRect = rail.rectTransform;
        railRect.anchorMin = new Vector2(0f, 0f);
        railRect.anchorMax = new Vector2(1f, 0f);
        railRect.pivot = new Vector2(0.5f, 0f);
        railRect.offsetMin = new Vector2(10f, 0f);
        railRect.offsetMax = new Vector2(-10f, 3f);

        _headline = MakeText(box.transform, "Headline", 26, Gold, FontStyle.Bold, new Vector2(0f, 16f));
        _detail = MakeText(box.transform, "Detail", 18, new Color(1f, 1f, 1f, 0.75f), FontStyle.Normal,
            new Vector2(0f, -18f));
    }

    static Text MakeText(Transform parent, string name, int size, Color color, FontStyle style, Vector2 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(740f, 34f);
        return text;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Broadcast overlay for the drop game: a queue of short coloured call-outs
/// ("SUPPLY DROP INBOUND", "CYAN GRABBED THE COMET SLING") plus a running team
/// gold readout. Deliberately independent of HudController — AI-v-AI has no
/// player rig, and the whole point of these matches is that they're watchable
/// with nobody playing. Self-bootstraps on play, like WeaponDebugConsole.
/// </summary>
public class MatchAnnouncer : MonoBehaviour
{
    public static MatchAnnouncer Instance { get; private set; }

    static readonly Color CyanTeam = new Color(0.2f, 0.9f, 1f);
    static readonly Color MagentaTeam = new Color(1f, 0.25f, 0.9f);

    const float MessageSeconds = 2.2f;

    /// <summary>
    /// Backlog cap. With crates landing every few seconds the overlay would
    /// otherwise fall minutes behind the match and narrate history — dropping
    /// the stalest pending message keeps it describing what's on screen now.
    /// </summary>
    const int MaxQueued = 3;

    struct Message
    {
        public string headline;
        public string detail;
        public Color color;
    }

    readonly Queue<Message> _queue = new Queue<Message>();
    GameObject _canvasGo;
    GameObject _toastGo;
    Text _headline;
    Text _detail;
    Text _gold;
    float _hideAt;

    // ------------------------------------------------------------- static API

    public static Color TeamColor(int teamId) => teamId == 1 ? MagentaTeam : CyanTeam;

    public static string TeamName(int teamId) => teamId == 1 ? "MAGENTA" : "CYAN";

    /// <summary>Readable name for a character — "YOU" for the player, else the object name.</summary>
    public static string CharacterName(Transform root)
    {
        if (root == null)
            return "SOMEONE";
        if (root.GetComponent<PlayerBrain>() != null)
            return "YOU";
        return root.name.Replace('_', ' ').ToUpperInvariant();
    }

    public static void Say(string headline, string detail, Color color)
    {
        if (Instance != null)
            Instance.Enqueue(headline, detail, color);
    }

    // ------------------------------------------------------------- lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("MatchAnnouncer").AddComponent<MatchAnnouncer>();
    }

    void Awake()
    {
        Instance = this;
        BuildUi();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Enqueue(string headline, string detail, Color color)
    {
        _queue.Enqueue(new Message { headline = headline, detail = detail, color = color });
        while (_queue.Count > MaxQueued)
            _queue.Dequeue();
    }

    void Update()
    {
        // The overlay belongs to a running match, not the menus.
        bool inMatch = GameModeController.Instance != null
            && (GameModeController.Instance.Mode == GameMode.PlayerVsAI
                || GameModeController.Instance.Mode == GameMode.AIvAI);
        if (_canvasGo.activeSelf != inMatch)
        {
            _canvasGo.SetActive(inMatch);
            if (!inMatch)
            {
                _queue.Clear();
                _toastGo.SetActive(false);
            }
        }
        if (!inMatch)
            return;

        if (Time.unscaledTime >= _hideAt)
        {
            if (_queue.Count > 0)
            {
                var message = _queue.Dequeue();
                _toastGo.SetActive(true);
                _headline.text = message.headline;
                _headline.color = message.color;
                _detail.text = message.detail;
                _hideAt = Time.unscaledTime + MessageSeconds;
            }
            else if (_toastGo.activeSelf)
            {
                _toastGo.SetActive(false);
            }
        }

        // No angle brackets here — uGUI Text parses rich-text tags.
        _gold.text = $"CYAN GOLD {TeamBank.Gold(0)}   ·   MAGENTA GOLD {TeamBank.Gold(1)}" +
                     $"   ·   NEW ROBOT AT {TeamBank.RobotCost}";
    }

    void BuildUi()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _canvasGo = new GameObject("AnnouncerCanvas");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;   // above the HUD, below the debug console
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Toast: top-centre, below the de-rez score line.
        _toastGo = new GameObject("Toast");
        _toastGo.transform.SetParent(_canvasGo.transform, false);
        var toastRect = _toastGo.AddComponent<RectTransform>();
        toastRect.anchorMin = toastRect.anchorMax = new Vector2(0.5f, 1f);
        toastRect.pivot = new Vector2(0.5f, 1f);
        toastRect.anchoredPosition = new Vector2(0, -102);
        toastRect.sizeDelta = new Vector2(1100, 84);

        _headline = MakeText(_toastGo.transform, "Headline", font, 34, FontStyle.Bold,
            TextAnchor.UpperCenter, CyanTeam, new Vector2(0, 0), new Vector2(0, 40));
        _detail = MakeText(_toastGo.transform, "Detail", font, 20, FontStyle.Normal,
            TextAnchor.UpperCenter, new Color(0.88f, 0.94f, 1f, 0.85f), new Vector2(0, -38), new Vector2(0, 34));

        _toastGo.SetActive(false);

        // Team gold, bottom-centre (bottom-left/right belong to the player HUD).
        var goldGo = new GameObject("Gold");
        goldGo.transform.SetParent(_canvasGo.transform, false);
        _gold = goldGo.AddComponent<Text>();
        _gold.font = font;
        _gold.fontSize = 22;
        _gold.fontStyle = FontStyle.Bold;
        _gold.alignment = TextAnchor.LowerCenter;
        _gold.color = new Color(1f, 0.82f, 0.25f, 0.9f);
        var goldRect = _gold.rectTransform;
        goldRect.anchorMin = goldRect.anchorMax = new Vector2(0.5f, 0f);
        goldRect.pivot = new Vector2(0.5f, 0f);
        goldRect.anchoredPosition = new Vector2(0, 78);
        goldRect.sizeDelta = new Vector2(900, 30);
    }

    static Text MakeText(Transform parent, string name, Font font, int size, FontStyle style,
        TextAnchor anchor, Color color, Vector2 position, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }
}

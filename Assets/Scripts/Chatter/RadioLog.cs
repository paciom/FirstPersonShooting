using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The scrolling comms feed in the bottom-left: who said what, in team colour,
/// fading out as it ages.
///
/// Separate from MatchAnnouncer on purpose. That overlay narrates *match events*
/// in big centred headlines and each one demands to be read. This is background
/// texture -- the sound of a battle happening around you -- so it lives out of
/// the way, stays small, and is designed to be glanced at rather than read. The
/// two would fight if they shared a surface.
///
/// Self-bootstraps like MatchAnnouncer: AI-v-AI matches run with no player rig,
/// and the chatter is most of what makes those watchable.
/// </summary>
public class RadioLog : MonoBehaviour
{
    public static RadioLog Instance { get; private set; }

    const int Rows = 6;
    const float LifeSeconds = 9f;
    const float FadeSeconds = 2f;

    struct Row
    {
        public Text text;
        public Color color;
        public float bornAt;
        public bool used;
    }

    readonly Row[] _rows = new Row[Rows];
    GameObject _canvasGo;

    // ------------------------------------------------------------- static API

    /// <summary>Appends a line. Silently no-ops before the log exists.</summary>
    public static void Push(string speakerName, string line, int teamId)
    {
        if (Instance != null)
            Instance.Append(speakerName, line, teamId);
    }

    // ------------------------------------------------------------- lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("RadioLog").AddComponent<RadioLog>();
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

    void Append(string speakerName, string line, int teamId)
    {
        // Scroll up by one: row 0 is the oldest visible line, the last is newest.
        for (int i = 0; i < Rows - 1; i++)
        {
            _rows[i].text.text = _rows[i + 1].text.text;
            _rows[i].color = _rows[i + 1].color;
            _rows[i].bornAt = _rows[i + 1].bornAt;
            _rows[i].used = _rows[i + 1].used;
        }

        int last = Rows - 1;
        _rows[last].text.text = string.IsNullOrEmpty(speakerName)
            ? line
            : $"{speakerName.ToUpperInvariant()}  {line}";
        _rows[last].color = MatchAnnouncer.TeamColor(teamId);
        _rows[last].bornAt = Time.unscaledTime;
        _rows[last].used = true;
    }

    void Update()
    {
        // Menus have no battle to overhear.
        bool inMatch = GameModeController.Instance != null
            && (GameModeController.Instance.Mode == GameMode.PlayerVsAI
                || GameModeController.Instance.Mode == GameMode.AIvAI);
        if (_canvasGo.activeSelf != inMatch)
        {
            _canvasGo.SetActive(inMatch);
            if (!inMatch)
                Clear();
        }
        if (!inMatch)
            return;

        for (int i = 0; i < Rows; i++)
        {
            if (!_rows[i].used)
                continue;
            float age = Time.unscaledTime - _rows[i].bornAt;
            if (age >= LifeSeconds)
            {
                _rows[i].text.text = string.Empty;
                _rows[i].used = false;
                continue;
            }
            // Older lines also sit dimmer than the newest, so the eye lands on
            // the line that just arrived without it having to move or flash.
            float fade = Mathf.InverseLerp(LifeSeconds, LifeSeconds - FadeSeconds, age);
            float depth = Mathf.Lerp(0.45f, 1f, i / (float)(Rows - 1));
            var color = _rows[i].color;
            color.a = Mathf.Clamp01(fade) * depth;
            _rows[i].text.color = color;
        }
    }

    void Clear()
    {
        for (int i = 0; i < Rows; i++)
        {
            if (_rows[i].text != null)
                _rows[i].text.text = string.Empty;
            _rows[i].used = false;
        }
    }

    void BuildUi()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _canvasGo = new GameObject("RadioLogCanvas");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 38;   // under MatchAnnouncer's 40
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        const float rowHeight = 26f;
        for (int i = 0; i < Rows; i++)
        {
            var go = new GameObject($"Row{i}");
            go.transform.SetParent(_canvasGo.transform, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 19;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = string.Empty;

            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            // Row 0 highest, newest at the bottom: the feed grows downward toward
            // the player's resting gaze rather than pushing away from it.
            rect.anchoredPosition = new Vector2(34f, 150f + (Rows - 1 - i) * rowHeight);
            rect.sizeDelta = new Vector2(820f, rowHeight);

            _rows[i].text = text;
            _rows[i].color = MatchAnnouncer.TeamColor(0);
        }
    }
}

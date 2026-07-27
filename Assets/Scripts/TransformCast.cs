using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays the player's transformation clip in a corner panel whenever they fold,
/// in Player-v-AI only. First person means you never see your own robot change
/// shape — this is the replay that shows what just happened to you.
///
/// Unfolding back into a robot plays a PRE-REVERSED file rather than running
/// the clip backwards: VideoPlayer.playbackSpeed rejects negative values, and
/// on WebGL the underlying &lt;video&gt; element ignores a negative playbackRate
/// outright. Tools/make_reverse_clips.py bakes &lt;robot&gt;-transform-back.mp4
/// next to each source clip; a robot without one falls back to playing forward.
///
/// Clips are streamed by URL out of StreamingAssets on every platform, not just
/// WebGL as the robot inspector does. The reversed file has no VideoClip asset
/// in the roster to reference, and one code path that works everywhere beats
/// two that differ per platform.
/// </summary>
public class TransformCast : MonoBehaviour
{
    const float PanelSize = 320f;
    const float FadeInSeconds = 0.18f;
    const float FadeOutSeconds = 0.4f;

    /// <summary>Stop waiting for a clip that is never going to arrive.</summary>
    const float MaxSeconds = 9f;

    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);

    public static TransformCast Instance { get; private set; }

    CanvasGroup _group;
    RawImage _view;
    Text _caption;
    RenderTexture _texture;
    VideoPlayer _video;
    TransformMode _player;

    bool _showing;
    float _elapsed;
    bool _triedFallback;
    string _forwardUrl;

    /// <summary>Create the panel if it isn't there yet. Safe to call repeatedly.</summary>
    public static TransformCast Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("TransformCast");
            go.AddComponent<TransformCast>();
        }
        return Instance;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        BuildUi();
        BuildPlayer();
        Subscribe();
    }

    void OnDestroy()
    {
        if (_player != null)
            _player.OnFoldStarted -= HandleFold;
        if (_video != null)
        {
            _video.loopPointReached -= HandleFinished;
            _video.errorReceived -= HandleError;
        }
        if (_texture != null)
            _texture.Release();
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// The player rig outlives every match, so one subscription holds — but it
    /// may not exist yet on the frame this component is created.
    /// </summary>
    void Subscribe()
    {
        if (_player != null || PlayerBrain.Local == null)
            return;
        _player = PlayerBrain.Local.GetComponent<TransformMode>();
        if (_player != null)
            _player.OnFoldStarted += HandleFold;
    }

    void Update()
    {
        Subscribe();

        if (_group == null || !_group.gameObject.activeSelf)
            return;

        float dt = Time.unscaledDeltaTime;

        if (_showing)
        {
            _elapsed += dt;
            // Nothing to replay to someone who has left the match, and a clip
            // that never loaded must not leave the panel up forever.
            bool inMatch = GameModeController.Instance == null
                || GameModeController.Instance.Mode == GameMode.PlayerVsAI;
            if (!inMatch || _elapsed > MaxSeconds)
                BeginHide();
        }

        _group.alpha = Mathf.MoveTowards(_group.alpha, _showing ? 1f : 0f,
            dt / (_showing ? FadeInSeconds : FadeOutSeconds));

        if (!_showing && _group.alpha <= 0.001f)
            Finish();
    }

    void HandleFold(bool toVehicle)
    {
        if (GameModeController.Instance != null
            && GameModeController.Instance.Mode != GameMode.PlayerVsAI)
            return;

        string robot = ResolveRobotName();
        if (string.IsNullOrEmpty(robot))
            return;

        string root = $"{Application.streamingAssetsPath}/{robot}-transform";
        _forwardUrl = $"{root}.mp4";
        _triedFallback = toVehicle;   // folding out is already the forward clip

        _caption.text = toVehicle ? "TRANSFORMING" : "BACK TO ROBOT";
        Play(toVehicle ? _forwardUrl : $"{root}-back.mp4");

        // Transforming again mid-clip restarts this one rather than stacking a
        // second panel; whatever alpha it had carries over.
        _showing = true;
        _elapsed = 0f;
        _group.gameObject.SetActive(true);
    }

    void Play(string url)
    {
        _video.Stop();
        _video.source = VideoSource.Url;
        _video.url = url;
        _video.Play();
    }

    /// <summary>
    /// Which robot the player is wearing. The scene is built with roster entry
    /// 0 and ApplyRobotSelection reskins bots only, so the player always has the
    /// default robot — the model instance itself is renamed "Model" on the way
    /// in, so its name can't be asked.
    /// </summary>
    static string ResolveRobotName()
    {
        var roster = FindFirstObjectByType<RobotRoster>();
        if (roster == null || !roster.HasRobots)
            return null;
        string name = roster.Get(0).displayName;
        return string.IsNullOrEmpty(name) ? null : name.ToLowerInvariant();
    }

    void HandleFinished(VideoPlayer source)
    {
        BeginHide();
    }

    /// <summary>
    /// A robot whose reversed clip was never generated falls back to the
    /// forward one — a transformation played the wrong way beats a black box in
    /// the corner, and it is the same footage.
    /// </summary>
    void HandleError(VideoPlayer source, string message)
    {
        if (!_triedFallback && !string.IsNullOrEmpty(_forwardUrl))
        {
            _triedFallback = true;
            Debug.LogWarning($"[TransformCast] {message} — falling back to the forward clip.");
            Play(_forwardUrl);
            return;
        }
        Debug.LogWarning($"[TransformCast] {message}");
        BeginHide();
    }

    /// <summary>Start the fade out, holding the last frame while it runs.</summary>
    void BeginHide()
    {
        _showing = false;
        if (_video != null)
            _video.Pause();
    }

    void Finish()
    {
        if (_video != null)
            _video.Stop();       // decoding a clip nobody can see is pure cost
        if (_group != null)
        {
            _group.alpha = 0f;
            _group.gameObject.SetActive(false);
        }
    }

    void BuildPlayer()
    {
        _texture = new RenderTexture(512, 512, 0);
        _view.texture = _texture;

        var videoGo = new GameObject("Clip");
        videoGo.transform.SetParent(transform, false);
        _video = videoGo.AddComponent<VideoPlayer>();
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.targetTexture = _texture;
        // The clips carry an audio track; this is a silent picture-in-picture,
        // and the fold already has its own sound in the arena.
        _video.audioOutputMode = VideoAudioOutputMode.None;
        _video.waitForFirstFrame = true;
        _video.loopPointReached += HandleFinished;
        _video.errorReceived += HandleError;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("TransformCastCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the HUD (0) and the mode overlay (10), under the on-screen
        // controls (15) and the menu (20).
        canvas.sortingOrder = 12;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Top-right corner, which the touch MENU button vacated for it.
        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        _group = panel.AddComponent<CanvasGroup>();
        var frame = panel.AddComponent<Image>();
        frame.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.85f);
        frame.raycastTarget = false;
        var frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(1f, 1f);
        frameRect.pivot = new Vector2(1f, 1f);
        frameRect.anchoredPosition = new Vector2(-40f, -40f);
        frameRect.sizeDelta = new Vector2(PanelSize + 8f, PanelSize + 54f);

        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(panel.transform, false);
        _view = viewGo.AddComponent<RawImage>();
        _view.raycastTarget = false;
        var viewRect = _view.rectTransform;
        viewRect.anchorMin = viewRect.anchorMax = new Vector2(0.5f, 1f);
        viewRect.pivot = new Vector2(0.5f, 1f);
        viewRect.anchoredPosition = new Vector2(0f, -4f);
        viewRect.sizeDelta = new Vector2(PanelSize, PanelSize);

        var captionGo = new GameObject("Caption");
        captionGo.transform.SetParent(panel.transform, false);
        _caption = captionGo.AddComponent<Text>();
        _caption.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _caption.text = "TRANSFORMING";
        _caption.fontSize = 24;
        _caption.fontStyle = FontStyle.Bold;
        _caption.alignment = TextAnchor.MiddleCenter;
        _caption.color = new Color(0.02f, 0.06f, 0.10f);
        _caption.raycastTarget = false;
        var captionRect = _caption.rectTransform;
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(1f, 0f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.offsetMin = new Vector2(0f, 6f);
        captionRect.offsetMax = new Vector2(0f, 42f);

        _group.alpha = 0f;
        panel.SetActive(false);
    }
}

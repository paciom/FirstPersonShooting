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

    Text _missing;
    bool _showing;
    float _elapsed;
    float _deadline = MaxSeconds;
    bool _triedFallback;
    string _forwardUrl;
    VideoClip _forwardClip;

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
            if (!inMatch || _elapsed > _deadline)
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

        // Transforming again mid-clip restarts this one rather than stacking a
        // second panel; whatever alpha it had carries over.
        _showing = true;
        _elapsed = 0f;
        _deadline = MaxSeconds;
        _caption.text = toVehicle ? "TRANSFORMING" : "BACK TO ROBOT";
        _group.gameObject.SetActive(true);

        // The panel goes up either way. A fold that plays no clip used to leave
        // the corner empty with nothing said anywhere, which is impossible to
        // tell apart from the whole feature being broken.
        string clip = ResolveClipName();
        if (string.IsNullOrEmpty(clip))
        {
            ShowMissing("NO  ROBOT  CLIP");
            return;
        }

        _missing.enabled = false;
        _view.enabled = true;
        string root = $"{Application.streamingAssetsPath}/{clip}";
        _forwardUrl = $"{root}.mp4";
        _forwardClip = GameModeController.Instance != null
            ? GameModeController.Instance.PlayerRobot.transformVideo : null;
        _triedFallback = toVehicle;   // folding out is already the forward clip

        if (toVehicle)
            Play(_forwardClip, _forwardUrl);
        else
            Play(null, $"{root}-back.mp4");   // no asset exists for the reversed file
    }

    /// <summary>
    /// Prefers the imported clip asset and falls back to streaming the file.
    ///
    /// WebGL is the exception in the other direction: it strips VideoClip
    /// assets to stubs that render black with the reference still non-null, so
    /// there the URL is the only thing that works. Same rule the robot
    /// inspector follows.
    /// </summary>
    void Play(VideoClip clip, string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        clip = null;
#endif
        _video.Stop();
        if (clip != null)
        {
            Debug.Log($"[TransformCast] clip {clip.name}");
            _video.source = VideoSource.VideoClip;
            _video.clip = clip;
        }
        else
        {
            Debug.Log($"[TransformCast] url {url}");
            _video.source = VideoSource.Url;
            _video.url = url;
        }
        _video.Play();
    }

    /// <summary>
    /// Base name of the player's transformation clip, without extension —
    /// "ranger-transform", which the reversed file suffixes with "-back".
    ///
    /// The clip ASSET's own name is the authority, exactly as the robot
    /// inspector's WebGL path uses it: the roster's display name only happens
    /// to match the file today, and a roster serialized before the video field
    /// existed would send us looking for a file that was never named that.
    ///
    /// The robot comes from the mode controller rather than being read off the
    /// player, whose model instance is renamed "Model" on the way in and so
    /// can't be asked what it is.
    /// </summary>
    static string ResolveClipName()
    {
        if (GameModeController.Instance == null)
            return null;

        // The robot the player actually wears, which is their own pick for the
        // cyan team — not necessarily roster entry 0.
        var entry = GameModeController.Instance.PlayerRobot;
        if (entry.modelPrefab == null)
        {
            Debug.LogWarning("[TransformCast] No robot roster in the scene — rerun Build Greybox Arena.");
            return null;
        }

        if (entry.transformVideo != null)
            return entry.transformVideo.name;

        // Roster from before the video field: fall back to the naming
        // convention the clips have always followed.
        if (!string.IsNullOrEmpty(entry.displayName))
            return $"{entry.displayName.ToLowerInvariant()}-transform";

        Debug.LogWarning("[TransformCast] Roster entry 0 has neither a clip nor a name.");
        return null;
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
        if (!_triedFallback && (_forwardClip != null || !string.IsNullOrEmpty(_forwardUrl)))
        {
            _triedFallback = true;
            Debug.LogWarning($"[TransformCast] {message} — falling back to the forward clip.");
            Play(_forwardClip, _forwardUrl);
            return;
        }
        Debug.LogWarning($"[TransformCast] {message}");
        ShowMissing("CLIP  WOULD  NOT  PLAY");
    }

    /// <summary>
    /// Keep the panel up, briefly, saying why it is empty. Silence here reads
    /// as a broken feature; a caption reads as a missing file.
    /// </summary>
    void ShowMissing(string reason)
    {
        _missing.text = reason;
        _missing.enabled = true;
        _view.enabled = false;
        _deadline = Mathf.Min(_deadline, _elapsed + 2.5f);
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

        var missingGo = new GameObject("Missing");
        missingGo.transform.SetParent(panel.transform, false);
        _missing = missingGo.AddComponent<Text>();
        _missing.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _missing.text = "";
        _missing.fontSize = 20;
        _missing.alignment = TextAnchor.MiddleCenter;
        _missing.color = new Color(0.02f, 0.06f, 0.10f, 0.75f);
        _missing.raycastTarget = false;
        _missing.enabled = false;
        var missingRect = _missing.rectTransform;
        missingRect.anchorMin = missingRect.anchorMax = new Vector2(0.5f, 1f);
        missingRect.pivot = new Vector2(0.5f, 1f);
        missingRect.anchoredPosition = new Vector2(0f, -PanelSize * 0.5f + 20f);
        missingRect.sizeDelta = new Vector2(PanelSize - 20f, 40f);

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

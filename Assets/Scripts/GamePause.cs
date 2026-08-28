using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The universal pause: a PAUSE button (and the P key) in every playable
/// mode — Brawl, Dogfight, Tower Defense, Commander, the wars, even AI v AI
/// spectating — freezing the game with Time.timeScale = 0 and showing a
/// PAUSED screen with RESUME and MUSIC ON/OFF. On the main menu the same
/// canvas shows just the MUSIC button, so the music can be silenced before
/// a game ever starts. Music itself runs on unscaled time and keeps playing
/// through a pause — that is what the toggle is for.
///
/// Self-bootstraps like the music player; nothing per-mode. Two outside
/// parties know about it: BrawlController's hit-stop (which restores
/// timeScale on a realtime clock and must not while paused), and the FPS
/// click-to-relock in GameModeController (clicking RESUME must not grab
/// the cursor).
/// </summary>
public class GamePause : MonoBehaviour
{
    public static bool Paused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Application.isBatchMode)
            return;
        var go = new GameObject("GamePause");
        DontDestroyOnLoad(go);
        go.AddComponent<GamePause>();
    }

    GameObject _pauseButton;
    GameObject _menuMusicButton;
    GameObject _overlay;
    Text _menuMusicLabel;
    Text _overlayMusicLabel;
    GameMode _pausedMode;
    bool _relockOnResume;

    void Awake()
    {
        // Same insurance every runtime UI in this project carries.
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        var canvasGo = new GameObject("PauseCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the mode HUDs, below the music toast (60).
        canvas.sortingOrder = 55;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        _pauseButton = MakeButton(canvasGo.transform, "II   PAUSE",
            new Vector2(1f, 1f), new Vector2(-110f, -34f), new Vector2(180f, 52f),
            Pause, out _);
        _menuMusicButton = MakeButton(canvasGo.transform, "",
            new Vector2(1f, 0f), new Vector2(-130f, 42f), new Vector2(220f, 52f),
            ToggleMusic, out _menuMusicLabel);

        _overlay = new GameObject("PausedScreen");
        _overlay.transform.SetParent(canvasGo.transform, false);
        var dim = _overlay.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0.05f, 0.72f);
        Stretch(dim.rectTransform);

        var title = MakeText(_overlay.transform, "PAUSED", 84);
        title.rectTransform.anchoredPosition = new Vector2(0f, 130f);
        title.rectTransform.sizeDelta = new Vector2(800f, 120f);

        MakeButton(_overlay.transform, "RESUME",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(360f, 72f),
            Resume, out _);
        MakeButton(_overlay.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(360f, 72f),
            ToggleMusic, out _overlayMusicLabel);

        _overlay.SetActive(false);
        RefreshMusicLabels();
    }

    static GameMode Mode =>
        GameModeController.Instance != null
            ? GameModeController.Instance.Mode : GameMode.Menu;

    // The movie must not be pausable mid-take; everything else may pause.
    static bool Pausable => Mode != GameMode.Menu && Mode != GameMode.Story;

    void Update()
    {
        // Leaving the paused mode (Escape still works under timeScale 0)
        // must never leave the whole game frozen at the menu.
        if (Paused && Mode != _pausedMode)
            Resume();

        if (!Paused && Pausable && Input.GetKeyDown(KeyCode.P))
            Pause();
        else if (Paused && Input.GetKeyDown(KeyCode.P))
            Resume();

        if (_pauseButton.activeSelf != (Pausable && !Paused))
            _pauseButton.SetActive(Pausable && !Paused);
        bool menuButton = Mode == GameMode.Menu;
        if (_menuMusicButton.activeSelf != menuButton)
            _menuMusicButton.SetActive(menuButton);
    }

    void Pause()
    {
        if (Paused || !Pausable)
            return;
        Paused = true;
        _pausedMode = Mode;
        Time.timeScale = 0f;
        // A locked FPS cursor cannot click RESUME; free it and remember.
        _relockOnResume = Cursor.lockState == CursorLockMode.Locked;
        if (_relockOnResume)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        RefreshMusicLabels();
        _overlay.SetActive(true);
    }

    void Resume()
    {
        if (!Paused)
            return;
        Paused = false;
        // Always to 1: a pause taken mid-Brawl-hit-stop must not resume
        // into slow motion the hit-stop clock has already moved past.
        Time.timeScale = 1f;
        _overlay.SetActive(false);
        if (_relockOnResume && Mode == _pausedMode)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        _relockOnResume = false;
    }

    void ToggleMusic()
    {
        GameMusic.Muted = !GameMusic.Muted;
        RefreshMusicLabels();
    }

    void RefreshMusicLabels()
    {
        string label = GameMusic.Muted ? "MUSIC:  OFF" : "MUSIC:  ON";
        _menuMusicLabel.text = label;
        _overlayMusicLabel.text = label;
    }

    // --------------------------------------------------------------- widgets

    static GameObject MakeButton(Transform parent, string label, Vector2 anchor,
        Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick,
        out Text text)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.08f, 0.12f, 0.22f, 0.85f);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        // Centre-anchored buttons pivot at the centre for symmetric layout.
        if (anchor == new Vector2(0.5f, 0.5f))
            rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        text = MakeText(go.transform, label, 28);
        Stretch(text.rectTransform);
        return go;
    }

    static Text MakeText(Transform parent, string content, int size)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.85f, 0.95f, 1f, 0.95f);
        text.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public enum GameMode { Menu, PlayerVsAI, AIvAI, ArenaPreview }

/// <summary>
/// Owns the game's mode flow: main menu → Player v AI / AI v AI / Arena Builder,
/// and Escape back to the menu from any mode. Lives on the GameController
/// object; discovers the player and bots at startup.
/// </summary>
public class GameModeController : MonoBehaviour
{
    public static GameModeController Instance { get; private set; }

    public GameMode Mode { get; private set; } = GameMode.Menu;

    GameObject _menuCanvas;
    Text _overlayText;
    GameObject _overlayCanvas;
    GameObject _player;
    PlayerBrain _playerBrain;
    CharacterMotor _playerMotor;
    AIBrain[] _bots;
    ArenaRandomizer _randomizer;
    GameObject _spectatorRig;
    DeRezEffect[] _deRezEffects;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        _playerBrain = FindFirstObjectByType<PlayerBrain>();
        if (_playerBrain != null)
        {
            _player = _playerBrain.gameObject;
            _playerMotor = _player.GetComponent<CharacterMotor>();
        }
        _bots = FindObjectsByType<AIBrain>(FindObjectsSortMode.None);
        _deRezEffects = FindObjectsByType<DeRezEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        _randomizer = GetComponent<ArenaRandomizer>();

        _menuCanvas = MainMenu.Build(this);
        BuildOverlay();
        EnterMenu();
    }

    void Update()
    {
        if (Mode != GameMode.Menu && Input.GetKeyDown(KeyCode.Escape))
        {
            EnterMenu();
            return;
        }

        if (Mode == GameMode.ArenaPreview && Input.GetKeyDown(KeyCode.R) && _randomizer != null)
            _randomizer.Regenerate();

        // Re-lock the cursor with a click after alt-tab/focus loss unlocks it.
        if ((Mode == GameMode.PlayerVsAI || Mode == GameMode.ArenaPreview)
            && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Re-assert menu stillness: a DeRezEffect re-materialize can re-enable
        // brains that were disabled when the menu opened mid-respawn.
        if (Mode == GameMode.Menu)
        {
            if (_playerBrain != null && _playerBrain.enabled)
                _playerBrain.enabled = false;
            SetBotsActive(false);
        }
    }

    /// <summary>
    /// Force-complete any in-flight de-rez cycles before switching modes —
    /// deactivating a mid-cycle character would otherwise strand it invisible.
    /// </summary>
    void RestoreAllDeRez()
    {
        if (_deRezEffects == null)
            return;
        foreach (var effect in _deRezEffects)
            if (effect != null)
                effect.CancelAndRestore();
    }

    public void EnterMenu()
    {
        Mode = GameMode.Menu;
        DestroySpectatorRig();
        RestoreAllDeRez();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = false;
            if (_playerMotor != null)
                _playerMotor.SetMoveInput(Vector2.zero);
        }
        SetBotsActive(false);

        _menuCanvas.SetActive(true);
        _overlayCanvas.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void StartPlayerVsAI()
    {
        Mode = GameMode.PlayerVsAI;
        RestoreAllDeRez();
        ScoreKeeper.Reset();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = true;
        }
        SetBotsActive(true);

        _menuCanvas.SetActive(false);
        ShowOverlay("ESC — Menu");
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void StartAIvAI()
    {
        Mode = GameMode.AIvAI;
        RestoreAllDeRez();
        ScoreKeeper.Reset();

        if (_player != null)
            _player.SetActive(false);
        SetBotsActive(true);

        _spectatorRig = BuildCameraRig("SpectatorCamera", new Vector3(0, 8, -14));
        _spectatorRig.AddComponent<SpectatorCamera>();

        _menuCanvas.SetActive(false);
        ShowOverlay("AI v AI — ESC for Menu");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void StartArenaPreview()
    {
        Mode = GameMode.ArenaPreview;
        RestoreAllDeRez();

        if (_player != null)
            _player.SetActive(false);
        SetBotsActive(false);

        _spectatorRig = BuildCameraRig("FlyCamera", new Vector3(0, 14, -22));
        _spectatorRig.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        _spectatorRig.AddComponent<FlyCam>();

        _menuCanvas.SetActive(false);
        ShowOverlay("R — New Layout   ·   WASD/QE — Fly   ·   ESC — Menu");
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void SetBotsActive(bool active)
    {
        foreach (var bot in _bots)
            if (bot != null)
                bot.SetActive(active);
    }

    GameObject BuildCameraRig(string name, Vector3 position)
    {
        var rig = new GameObject(name);
        // Tagged so Camera.main works while the player camera is inactive
        // (FlashQuad billboarding depends on it). Only one is ever active.
        rig.tag = "MainCamera";
        rig.transform.position = position;
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = 65f;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        return rig;
    }

    void DestroySpectatorRig()
    {
        if (_spectatorRig != null)
        {
            Destroy(_spectatorRig);
            _spectatorRig = null;
        }
    }

    void ShowOverlay(string message)
    {
        _overlayCanvas.SetActive(true);
        _overlayText.text = message;
    }

    void BuildOverlay()
    {
        _overlayCanvas = new GameObject("ModeOverlay");
        _overlayCanvas.transform.SetParent(transform, false);
        var canvas = _overlayCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = _overlayCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textGo = new GameObject("Hint");
        textGo.transform.SetParent(_overlayCanvas.transform, false);
        _overlayText = textGo.AddComponent<Text>();
        _overlayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _overlayText.fontSize = 22;
        _overlayText.alignment = TextAnchor.MiddleCenter;
        _overlayText.color = new Color(0.2f, 0.9f, 1f, 0.75f);
        var rect = _overlayText.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, 28);
        rect.sizeDelta = new Vector2(900, 40);

        _overlayCanvas.SetActive(false);
    }
}

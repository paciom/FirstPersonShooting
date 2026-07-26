using System.Collections.Generic;
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
    GameObject _robotSelect;
    RobotRoster _roster;
    GameMode _pendingMode;
    int _cyanRobot;
    int _magentaRobot;
    // The scene is built with roster entry 0 (the default rigged walker) on
    // every bot, so an initial 0/0 selection needs no reskin.
    int _appliedCyan;
    int _appliedMagenta;
    Text _overlayText;
    GameObject _overlayCanvas;
    GameObject _player;
    PlayerBrain _playerBrain;
    CharacterMotor _playerMotor;
    // A list, not an array: teams grow mid-match when a team banks enough gold
    // to build a reinforcement (see RobotReinforcements).
    readonly List<AIBrain> _bots = new List<AIBrain>();
    ArenaBlockManager _blockManager;
    TreasureSpawner _treasureSpawner;
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
        _bots.AddRange(FindObjectsByType<AIBrain>(FindObjectsSortMode.None));
        _deRezEffects = FindObjectsByType<DeRezEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        _blockManager = GetComponent<ArenaBlockManager>();
        _treasureSpawner = GetComponent<TreasureSpawner>();
        _roster = GetComponent<RobotRoster>();

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

        // Escape backs out of the robot select screen (still Menu mode).
        if (Mode == GameMode.Menu && _robotSelect != null && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelRobotSelect();
            return;
        }

        if (Mode == GameMode.ArenaPreview && Input.GetKeyDown(KeyCode.R) && _blockManager != null)
            _blockManager.Reshuffle();

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
    ///
    /// Vehicle form unwinds here too, and for the same reason: both effects run
    /// as coroutines, and a deactivated GameObject loses those permanently. A
    /// robot left mid-fold comes back with a vehicle's hitbox and one gun.
    /// </summary>
    void RestoreAllDeRez()
    {
        if (_deRezEffects == null)
            return;
        foreach (var effect in _deRezEffects)
        {
            if (effect == null)
                continue;
            effect.CancelAndRestore();

            var vehicle = effect.GetComponent<TransformMode>();
            if (vehicle != null)
                vehicle.ForceRobotForm();
        }
    }

    /// <summary>Track a robot built mid-match so mode switches still control it.</summary>
    public void RegisterBot(AIBrain bot)
    {
        if (bot != null && !_bots.Contains(bot))
            _bots.Add(bot);
    }

    public void UnregisterBot(AIBrain bot) => _bots.Remove(bot);

    /// <summary>
    /// Reset everything a match owns: airdrops off the field, banked gold back
    /// to zero, and bought robots removed so team sizes never leak from one
    /// match into the next.
    /// </summary>
    void ResetMatchState()
    {
        _treasureSpawner?.EndMatch();
        RobotReinforcements.DespawnAll();
        TeamBank.Reset();
    }

    public void EnterMenu()
    {
        Mode = GameMode.Menu;
        DestroySpectatorRig();
        ResetMatchState();
        RestoreAllDeRez();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = false;
            if (_playerMotor != null)
                _playerMotor.SetMoveInput(Vector2.zero);
        }
        SetBotsActive(false);

        CloseRobotSelect();
        _menuCanvas.SetActive(true);
        _overlayCanvas.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Main-menu entry for both AI modes: show the robot select screen first.
    /// Falls straight through to the match when no roster exists yet (scene
    /// built before any robot models were downloaded).
    /// </summary>
    public void OpenRobotSelect(GameMode mode)
    {
        if (_roster == null || !_roster.HasRobots)
        {
            if (mode == GameMode.AIvAI) StartAIvAI(); else StartPlayerVsAI();
            return;
        }

        _pendingMode = mode;
        _menuCanvas.SetActive(false);
        CloseRobotSelect();
        _robotSelect = RobotSelectMenu.Build(this, _roster, mode, _cyanRobot, _magentaRobot);
    }

    public void CancelRobotSelect()
    {
        CloseRobotSelect();
        _menuCanvas.SetActive(true);
    }

    public void LaunchSelectedMatch(int cyanIndex, int magentaIndex)
    {
        _cyanRobot = cyanIndex;
        _magentaRobot = magentaIndex;
        CloseRobotSelect();
        ApplyRobotSelection();
        if (_pendingMode == GameMode.AIvAI) StartAIvAI(); else StartPlayerVsAI();
    }

    void CloseRobotSelect()
    {
        if (_robotSelect != null)
        {
            Destroy(_robotSelect);
            _robotSelect = null;
        }
    }

    /// <summary>
    /// Swaps every bot's model to its team's selected robot. Runs from the
    /// menu, where RestoreAllDeRez has already reset every Body to full scale.
    /// </summary>
    void ApplyRobotSelection()
    {
        if (_roster == null || !_roster.HasRobots)
            return;
        if (_appliedCyan == _cyanRobot && _appliedMagenta == _magentaRobot)
            return;
        _appliedCyan = _cyanRobot;
        _appliedMagenta = _magentaRobot;

        foreach (var bot in _bots)
        {
            if (bot == null)
                continue;
            var shield = bot.GetComponent<EnergyShield>();
            int team = shield != null ? shield.teamId : 1;
            var entry = _roster.Get(team == 0 ? _cyanRobot : _magentaRobot);
            if (entry.modelPrefab == null)
                continue;
            var body = bot.transform.Find("Body");
            if (body == null)
                continue;
            Color tint = team == 0 ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.25f, 0.9f);
            RobotFactory.Reskin(body, entry.modelPrefab, tint);
        }

        // Old silhouette duplicates died with the old models; rebuild on next scope.
        var scope = FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include);
        if (scope != null)
            scope.InvalidateSilhouettes();
    }

    public void StartPlayerVsAI()
    {
        Mode = GameMode.PlayerVsAI;
        ResetMatchState();
        RestoreAllDeRez();
        ScoreKeeper.Reset();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = true;
        }
        SetBotsActive(true);
        _treasureSpawner?.BeginMatch();

        _menuCanvas.SetActive(false);
        ShowOverlay("ESC — Menu");
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void StartAIvAI()
    {
        Mode = GameMode.AIvAI;
        ResetMatchState();
        RestoreAllDeRez();
        ScoreKeeper.Reset();

        if (_player != null)
            _player.SetActive(false);
        SetBotsActive(true);
        _treasureSpawner?.BeginMatch();

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
        ResetMatchState();
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

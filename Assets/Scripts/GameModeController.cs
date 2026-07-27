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
    // -1, not 0: the scene is built with roster entry 0 on every bot, but it is
    // built in the editor, where TeamPaint deliberately does nothing (a repaint
    // is a RenderTexture and cannot be serialized into a scene). So the default
    // 0/0 selection — both teams on the ranger, the most likely case of all —
    // still has to reskin once, or the two teams launch identically painted.
    int _appliedCyan = -1;
    int _appliedMagenta = -1;
    Text _overlayText;
    GameObject _overlayCanvas;
    string _hintDesktop = "";
    string _hintTouch = "";
    bool _hintWasTouch;
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
        // Builds nothing until a touch actually happens (or the platform is
        // mobile), so desktop play is unaffected.
        TouchControls.Ensure();
        // Corner replay of the player's own transformation — first person never
        // sees it otherwise.
        TransformCast.Ensure();
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

        if (Mode == GameMode.ArenaPreview && Input.GetKeyDown(KeyCode.R))
            RequestReshuffle();

        // Re-lock the cursor with a click after alt-tab/focus loss unlocks it.
        // Never while the on-screen controls are up — they need a free cursor.
        if ((Mode == GameMode.PlayerVsAI || Mode == GameMode.ArenaPreview)
            && !TouchControls.Active
            && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Picking up a tablet mid-match swaps the hint to its touch wording.
        if (Mode != GameMode.Menu && _hintWasTouch != TouchControls.Active)
            ApplyOverlay();

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

    /// <summary>New arena layout — the R key in Arena Builder, or its touch button.</summary>
    public void RequestReshuffle()
    {
        if (Mode == GameMode.ArenaPreview && _blockManager != null)
            _blockManager.Reshuffle();
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
        LockCursor(false);
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
            Color tint = MatchAnnouncer.TeamColor(team);
            RobotFactory.Reskin(body, entry.modelPrefab, tint);

            // A different robot transforms into a different vehicle. Rebuilt
            // after the reskin so it measures against the new robot's height.
            //
            // Stages win where a robot has them: turning into the tank at the
            // end of its own transformation beats turning into a separately
            // generated vehicle that never appears in that transformation.
            var skin = body.GetComponent<VehicleSkin>();
            if (skin != null)
            {
                if (entry.HasStages)
                    skin.SetStages(entry.transformStages, tint);
                else
                    skin.SetVehiclePrefab(entry.vehiclePrefab, tint);
            }
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
        ShowOverlay("ESC — Menu", "");
        LockCursor(true);
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
        ShowOverlay("AI v AI — ESC for Menu", "AI v AI — tap MENU to go back");
        LockCursor(false);
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
        ShowOverlay("R — New Layout   ·   WASD/QE — Fly   ·   ESC — Menu",
            "NEW — fresh layout   ·   stick to fly   ·   drag to look");
        LockCursor(true);
    }

    /// <summary>
    /// Capture the mouse for a first-person mode — unless the on-screen
    /// controls are driving, where a captured cursor helps nobody and the
    /// editor's mouse-as-finger testing needs it free.
    /// </summary>
    static void LockCursor(bool locked)
    {
        bool lockIt = locked && !TouchControls.Active;
        Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockIt;
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

    /// <summary>
    /// Mode hint, in two flavours — the keyboard one and the touch one. Which
    /// shows is re-evaluated whenever the input scheme flips mid-match.
    /// </summary>
    void ShowOverlay(string desktopMessage, string touchMessage)
    {
        _hintDesktop = desktopMessage;
        _hintTouch = touchMessage;
        ApplyOverlay();
    }

    void ApplyOverlay()
    {
        _hintWasTouch = TouchControls.Active;
        string message = _hintWasTouch ? _hintTouch : _hintDesktop;
        _overlayText.text = message;
        _overlayCanvas.SetActive(!string.IsNullOrEmpty(message));
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public enum GameMode { Menu, PlayerVsAI, AIvAI, ArenaPreview, Commander, OnlinePvP, Brawl, BrawlWar, BrawlShow, TowerDefense, ChineseQuest, ChineseRun, TankRaid }

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
    GameObject _arenaSelect;
    int _arenaIndex;
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
    // -1, not 0: the player starts with no model at all, so even entry 0 is a change.
    int _appliedPlayerRobot = -1;
    Text _overlayText;
    GameObject _overlayCanvas;
    string _hintDesktop = "";
    string _hintTouch = "";
    bool _hintWasTouch;
    GameObject _player;
    PlayerBrain _playerBrain;
    CharacterMotor _playerMotor;
    // A list, not an array: teams grow mid-match when a team banks enough gold
    // to build a reinforcement (see RobotReinforcements). Not readonly: plain
    // private fields survive a recompile-during-Play reload, readonly ones are
    // silently reset — and an empty bot list here means no mode can ever pause
    // or resume the bots again.
    List<AIBrain> _bots = new List<AIBrain>();
    ArenaBlockManager _blockManager;
    TreasureSpawner _treasureSpawner;
    GameObject _spectatorRig;
    CommanderController _commander;
    TDController _towerDefense;
    BrawlController _brawl;
    BrawlShow _brawlShow;
    GameObject _brawlStageSelect;
    ChineseQuest _chineseQuest;
    ChineseRun _chineseRun;
    TankRaid _tankRaid;
    GameObject _chineseDeckSelect;
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
            // In Commander, Escape unwinds one intent at a time before it
            // exits the mode: first a build ghost, then an armed attack-move.
            // Asked through here rather than read in three Updates, where
            // execution order would decide which one wins.
            if (Mode == GameMode.Commander && _commander != null)
            {
                if (_commander.Placer != null && _commander.Placer.CancelPending())
                    return;
                if (_commander.Selection != null && _commander.Selection.CancelPendingOrder())
                    return;
            }
            // Tower Defense has one intent to unwind: an armed tower ghost.
            if (Mode == GameMode.TowerDefense && _towerDefense != null
                && _towerDefense.Placer != null && _towerDefense.Placer.CancelPending())
                return;
            EnterMenu();
            return;
        }

        // Escape steps BACK one screen rather than all the way out: arena
        // select → robot select → main menu.
        if (Mode == GameMode.Menu && _arenaSelect != null && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelArenaSelect();
            return;
        }

        if (Mode == GameMode.Menu && _brawlStageSelect != null && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelBrawlStageSelect();
            return;
        }

        if (Mode == GameMode.Menu && _chineseDeckSelect != null && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelChineseDeckSelect();
            return;
        }

        if (Mode == GameMode.Menu && _robotSelect != null && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelRobotSelect();
            return;
        }

        if (Mode == GameMode.ArenaPreview && Input.GetKeyDown(KeyCode.R))
            RequestReshuffle();

        // Arena Builder doubles as the arena iteration loop: cycle without
        // going back through the menus.
        if (Mode == GameMode.ArenaPreview && ArenaLibrary.Count > 1)
        {
            if (Input.GetKeyDown(KeyCode.RightBracket))
                CycleArena(1);
            else if (Input.GetKeyDown(KeyCode.LeftBracket))
                CycleArena(-1);
        }

        // Re-lock the cursor with a click after alt-tab/focus loss unlocks it.
        // Never while the on-screen controls are up — they need a free cursor.
        if ((Mode == GameMode.PlayerVsAI || Mode == GameMode.ArenaPreview || Mode == GameMode.OnlinePvP)
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
        // Leaving an online match tells the other player before anything is
        // torn down; a no-op in every other mode (and when the match already
        // ended itself — NetMatch guards on its own state).
        if (Mode == GameMode.OnlinePvP)
            NetMatch.OnLocalLeftMatch();

        Mode = GameMode.Menu;
        DestroySpectatorRig();
        // Torn down before anything touches the characters: Teardown is what
        // brings the hidden FPS cast back and swaps the arena world back in.
        if (_commander != null)
        {
            _commander.Teardown();
            _commander = null;
        }
        // Tower Defense holds the world the same way Commander does, and
        // hands it back through the same one-shot Teardown.
        if (_towerDefense != null)
        {
            _towerDefense.Teardown();
            _towerDefense = null;
        }
        // Same contract as Commander: Teardown swaps the arena world back in
        // before anything touches the characters.
        if (_brawl != null)
        {
            _brawl.Teardown();
            _brawl = null;
        }
        if (_brawlShow != null)
        {
            _brawlShow.Teardown();
            _brawlShow = null;
        }
        // Same contract again: Chinese Quest borrows Brawl's world lifecycle
        // wholesale, so it hands the arena back the same way.
        if (_chineseQuest != null)
        {
            _chineseQuest.Teardown();
            _chineseQuest = null;
        }
        // Same again — and this one also hands back the lane fence it widened
        // to build a road with no end.
        if (_chineseRun != null)
        {
            _chineseRun.Teardown();
            _chineseRun = null;
        }
        // Same contract once more — and this one also sweeps the tanks, which
        // live at the SCENE ROOT rather than under its stage (see TankPawn), so
        // destroying the stage would leave them driving around the arena.
        if (_tankRaid != null)
        {
            _tankRaid.Teardown();
            _tankRaid = null;
        }
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

        // Bring hidden bots back — online matches hide their bodies, and the
        // menu's arena backdrop expects the full cast standing in it.
        SetBotsHidden(false);

        CloseRobotSelect();
        // Also closed here, or backing out of a match mid-arena-select leaves an
        // orphaned canvas floating over the main menu.
        CloseArenaSelect();
        CloseBrawlStageSelect();
        CloseChineseDeckSelect();
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
            if (mode == GameMode.AIvAI) StartAIvAI();
            else if (mode == GameMode.Brawl) StartBrawl();
            else if (mode == GameMode.BrawlWar) StartBrawlWar();
            else if (mode == GameMode.BrawlShow) StartBrawlShow();
            else if (mode == GameMode.TankRaid) StartTankRaid();
            else StartPlayerVsAI();
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

    /// <summary>
    /// Robots are chosen; now pick the arena. Both AI modes funnel through here,
    /// so the arena step lands in both at once.
    /// </summary>
    public void LaunchSelectedMatch(int cyanIndex, int magentaIndex)
    {
        _cyanRobot = cyanIndex;
        _magentaRobot = magentaIndex;
        CloseRobotSelect();
        // The Brawl family skips the FPS-cast reskin — its robots spawn
        // fresh from the roster. Brawl and its exhibition go through the
        // stage picker (the same step FPS modes give arenas); the Show
        // launches straight onto its bench.
        if (_pendingMode == GameMode.Brawl || _pendingMode == GameMode.BrawlWar)
        {
            _menuCanvas.SetActive(false);
            CloseBrawlStageSelect();
            _brawlStageSelect = BrawlStageSelect.Build(this, _pendingMode);
            return;
        }
        if (_pendingMode == GameMode.BrawlShow)
        {
            StartBrawlShow();
            return;
        }
        // Tank Raid takes the cyan pick as the hero's chassis and builds its own
        // tanks from the roster, so it skips the FPS-cast reskin the same way
        // the Brawl family does — and its battlefield is its own set, so there
        // is no arena step in front of it either.
        if (_pendingMode == GameMode.TankRaid)
        {
            StartTankRaid();
            return;
        }
        ApplyRobotSelection();
        OpenArenaSelect();
    }

    void CloseRobotSelect()
    {
        if (_robotSelect != null)
        {
            Destroy(_robotSelect);
            _robotSelect = null;
        }
    }

    // ---------- arena select ----------

    /// <summary>
    /// Show the arena picker. With fewer than two arenas registered there is no
    /// choice to offer, so it falls straight through to the match — the same way
    /// OpenRobotSelect does when no roster exists.
    /// </summary>
    public void OpenArenaSelect()
    {
        if (ArenaLibrary.Count < 2)
        {
            LaunchArena(0);
            return;
        }

        _menuCanvas.SetActive(false);
        CloseArenaSelect();
        _arenaSelect = ArenaSelectMenu.Build(this, _arenaIndex);
    }

    /// <summary>
    /// Build the chosen arena, then start the match.
    ///
    /// Order matters: the arena loads BEFORE Start*, never after. Start*
    /// positions and enables characters, and re-baking the NavMesh underneath
    /// live agents strands them off-mesh.
    /// </summary>
    public void LaunchArena(int arenaIndex)
    {
        _arenaIndex = arenaIndex;
        CloseArenaSelect();
        ArenaRuntime.Load(arenaIndex);
        if (_pendingMode == GameMode.AIvAI) StartAIvAI(); else StartPlayerVsAI();
    }

    /// <summary>Escape from the arena screen goes back a step, to robot select.</summary>
    public void CancelArenaSelect()
    {
        CloseArenaSelect();
        if (_roster != null && _roster.HasRobots)
            _robotSelect = RobotSelectMenu.Build(this, _roster, _pendingMode, _cyanRobot, _magentaRobot);
        else
            _menuCanvas.SetActive(true);
    }

    void CloseArenaSelect()
    {
        if (_arenaSelect != null)
        {
            Destroy(_arenaSelect);
            _arenaSelect = null;
        }
    }

    /// <summary>Arena Builder's `[` / `]` — swap arena without leaving the mode.</summary>
    void CycleArena(int step)
    {
        int count = ArenaLibrary.Count;
        _arenaIndex = ((_arenaIndex + step) % count + count) % count;
        ArenaRuntime.Load(_arenaIndex);
        var arena = ArenaLibrary.Get(_arenaIndex);
        ShowOverlay($"{arena.DisplayName} — [ ] arena   ·   R reshuffle   ·   ESC menu",
                    $"{arena.DisplayName}");
    }

    /// <summary>
    /// Roster entry the player's own robot wears. Their pick for the cyan team
    /// is their own robot too — see <see cref="EnsurePlayerRobot"/>.
    /// </summary>
    public RobotRoster.Entry PlayerRobot =>
        (_roster != null && _roster.HasRobots) ? _roster.Get(_cyanRobot) : default;

    // Online PvP reads these to describe the local setup to the other client
    // and to dress their mirror pawn in the robot they actually picked.
    public int PlayerRobotIndex => _cyanRobot;
    public int CurrentArenaIndex => _arenaIndex;
    public RobotRoster Roster => _roster;

    /// <summary>
    /// Gives the player the same robot rig the bots wear.
    ///
    /// The scene builds the player as a bare capsule — first person never sees
    /// itself, so a placeholder was enough. But a capsule carries no Animator,
    /// so TransformMode.CanTransform reported false and T did nothing at all:
    /// no fold, no vehicle guns, and no transformation replay, since all three
    /// hang off a fold that never started.
    ///
    /// Done at runtime rather than in ArenaBuilder so it needs no scene
    /// rebuild; a rebuilt scene that already has a model just falls through.
    /// </summary>
    void EnsurePlayerRobot()
    {
        if (_roster == null || !_roster.HasRobots || _player == null)
            return;
        var body = _player.transform.Find("Body");
        if (body == null)
            return;
        var entry = _roster.Get(_cyanRobot);
        if (entry.modelPrefab == null)
            return;

        var tint = new Color(0.2f, 0.9f, 1f);
        bool hasModel = body.Find("Model") != null;
        if (hasModel && _appliedPlayerRobot == _cyanRobot)
            return;
        _appliedPlayerRobot = _cyanRobot;

        if (hasModel)
        {
            RobotFactory.Reskin(body, entry.modelPrefab, tint, entry.paintAnchorHue);
        }
        else
        {
            // The capsule was only ever standing in for the model.
            var placeholder = body.GetComponent<MeshRenderer>();
            if (placeholder != null)
                placeholder.enabled = false;
            RobotFactory.InstantiateNormalized(entry.modelPrefab, body, tint, entry.paintAnchorHue);
        }

        var skin = body.GetComponent<VehicleSkin>();
        if (skin == null)
        {
            skin = body.gameObject.AddComponent<VehicleSkin>();
            skin.holder = body;
        }
        // Set before either branch: both configure calls rebuild the models,
        // and each one repaints as it goes.
        skin.paintAnchorHue = entry.paintAnchorHue;
        if (entry.HasStages)
            skin.SetStages(entry.transformStages, tint);
        else
            skin.SetVehiclePrefab(entry.vehiclePrefab, tint);

        var scope = FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include);
        if (scope != null)
            scope.InvalidateSilhouettes();
    }

    /// <summary>
    /// Swaps every bot's model to its team's selected robot. Runs from the
    /// menu, where RestoreAllDeRez has already reset every Body to full scale.
    /// </summary>
    void ApplyRobotSelection()
    {
        EnsurePlayerRobot();

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
            RobotFactory.Reskin(body, entry.modelPrefab, tint, entry.paintAnchorHue);

            // A different robot transforms into a different vehicle. Rebuilt
            // after the reskin so it measures against the new robot's height.
            //
            // Stages win where a robot has them: turning into the tank at the
            // end of its own transformation beats turning into a separately
            // generated vehicle that never appears in that transformation.
            var skin = body.GetComponent<VehicleSkin>();
            if (skin != null)
            {
                skin.paintAnchorHue = entry.paintAnchorHue;
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
        // Also reached when the roster screen is skipped entirely, which is
        // where the player would otherwise still be a capsule.
        EnsurePlayerRobot();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = true;
        }
        SetBotsActive(true);
        _treasureSpawner?.BeginMatch();

        _menuCanvas.SetActive(false);
        ShowOverlay("T — Transform   ·   Z — Sniper Scope   ·   ESC — Menu", "");
        LockCursor(true);
    }

    /// <summary>
    /// The online 1v1: local player vs the other human's mirror pawn (built by
    /// NetMatch after this returns). No bots — their brains are off AND their
    /// bodies hidden, since a brainless robot standing at spawn reads as a
    /// target. No airdrops either: treasure rolls are unseeded randomness that
    /// the two clients could never agree on (v1).
    ///
    /// The guest spawns across the arena on the magenta line so the two
    /// players' world positions agree on both clients: host = PlayerSpawn,
    /// guest = TeamSpawns(1)[0], everywhere.
    /// </summary>
    public void StartOnlinePvP(int arenaIndex, bool isHost)
    {
        _arenaIndex = arenaIndex;
        CloseRobotSelect();
        CloseArenaSelect();
        // Arena before characters, as everywhere: re-baking the NavMesh under
        // live agents strands them, and spawns come from the loaded arena.
        ArenaRuntime.Load(arenaIndex);

        Mode = GameMode.OnlinePvP;
        DestroySpectatorRig();
        ResetMatchState();
        RestoreAllDeRez();
        ScoreKeeper.Reset();
        EnsurePlayerRobot();

        if (_player != null)
        {
            _player.SetActive(true);
            _playerBrain.enabled = true;

            if (!isHost)
            {
                var spot = ArenaContext.Current.TeamSpawns(1)[0];
                var rotation = Quaternion.Euler(0f, 180f, 0f);
                if (_playerMotor != null)
                    _playerMotor.Teleport(spot);
                else
                    _player.transform.position = spot;
                _player.transform.rotation = rotation;
                var deRez = _player.GetComponent<DeRezEffect>();
                if (deRez != null)
                    deRez.SetSpawn(spot, rotation);
            }
        }

        SetBotsActive(false);
        SetBotsHidden(true);

        _menuCanvas.SetActive(false);
        ShowOverlay("ONLINE MATCH   ·   T — Transform   ·   Z — Scope   ·   ESC — Leave",
                    "ONLINE MATCH   ·   tap MENU to leave");
        LockCursor(true);
    }

    /// <summary>
    /// Bots have no place in an online match (yet). Hidden AFTER
    /// RestoreAllDeRez, which this mode's entry already ran — deactivating a
    /// mid-cycle character would strand its coroutines. EnterMenu unhides.
    /// </summary>
    void SetBotsHidden(bool hidden)
    {
        foreach (var bot in _bots)
            if (bot != null && bot.gameObject.activeSelf == hidden)
                bot.gameObject.SetActive(!hidden);
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

        // Start the broadcast from the active arena's own perch, not a fixed
        // point that may sit inside a ziggurat tier or under a catwalk.
        _spectatorRig = BuildCameraRig("SpectatorCamera", ArenaContext.Current.SpectatorPerch);
        _spectatorRig.AddComponent<SpectatorCamera>();

        _menuCanvas.SetActive(false);
        ShowOverlay("AI v AI — ESC for Menu", "AI v AI — tap MENU to go back");
        LockCursor(false);
    }

    /// <summary>
    /// The Commander RTS mode. No robot/arena select in front of it (yet):
    /// the battlefield is its own fixed map, and army composition is decided
    /// in-match by what you build, not on a select screen.
    /// </summary>
    public void StartCommander() => StartCommanderMode(playerCommands: true);

    /// <summary>Commander's AI-v-AI: two machine commanders, a camera that hunts the fight.</summary>
    public void StartCommanderWar() => StartCommanderMode(playerCommands: false);

    void StartCommanderMode(bool playerCommands)
    {
        Mode = GameMode.Commander;
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden: deactivating a mid-cycle de-rez
        // or fold would strand its coroutine — same order every mode uses.
        RestoreAllDeRez();

        // The MAP CODE: whatever the menu box asked for, or a fresh roll.
        // Printed in the hint so a battlefield worth revisiting can be —
        // type its code back into the menu box.
        int seed = MainMenu.RequestedSeed ?? Random.Range(1, 1000000);
        _commander = CommanderController.Begin(this, playerCommands, seed);

        _menuCanvas.SetActive(false);
        if (playerCommands)
            ShowOverlay($"MAP #{seed}   ·   Drag — Select   ·   RMB — Move / Attack   ·   " +
                "A + Click — Attack-move   ·   WASD / Wheel — Camera   ·   H — Home   ·   ESC — Menu",
                $"MAP #{seed}   ·   tap robot — select   ·   tap ground — move   ·   drag — pan");
        else
            ShowOverlay($"AI WAR — MAP #{seed}   ·   the camera follows the fighting — " +
                "drag / WASD / wheel to take it   ·   ESC — Menu",
                $"AI WAR — MAP #{seed}   ·   drag — pan   ·   pinch — zoom   ·   MENU to go back");
        LockCursor(false);
    }

    /// <summary>
    /// The Tower Defense siege: raiders pour through a warp gate and march
    /// a canyon toward the Photon Core; the player builds the towers that
    /// say otherwise. Its battlefield rolls from the same MAP CODE box
    /// Commander reads — one code vocabulary for both strategy modes.
    /// </summary>
    public void StartTowerDefense()
    {
        Mode = GameMode.TowerDefense;
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        int seed = MainMenu.RequestedSeed ?? Random.Range(1, 1000000);
        _towerDefense = TDController.Begin(this, seed);

        _menuCanvas.SetActive(false);
        ShowOverlay($"MAP #{seed}   ·   Towers on the high ground, robots in the canyon   ·   " +
            "RALLY FLAG — move the line   ·   RMB tower — Sell   ·   Drag — Pan   ·   H — Core   ·   ESC — Menu",
            $"MAP #{seed}   ·   tap to build towers and robots   ·   drag — pan   ·   pinch — zoom");
        LockCursor(false);
    }

    /// <summary>A stage card was clicked: remember it and fight there.</summary>
    public void ChooseBrawlStage(int selection)
    {
        BrawlArenas.SetSelected(_pendingMode, selection);
        CloseBrawlStageSelect();
        if (_pendingMode == GameMode.BrawlWar) StartBrawlWar(); else StartBrawl();
    }

    /// <summary>Escape/BACK from the stage screen goes back a step, to robot select.</summary>
    public void CancelBrawlStageSelect()
    {
        CloseBrawlStageSelect();
        if (_roster != null && _roster.HasRobots)
            _robotSelect = RobotSelectMenu.Build(this, _roster, _pendingMode, _cyanRobot, _magentaRobot);
        else
            _menuCanvas.SetActive(true);
    }

    void CloseBrawlStageSelect()
    {
        if (_brawlStageSelect != null)
        {
            Destroy(_brawlStageSelect);
            _brawlStageSelect = null;
        }
    }

    /// <summary>The end panel's CHANGE ROBOTS: out through the menu, back into the same select.</summary>
    public void RestartBrawlSelect()
    {
        var variant = Mode == GameMode.BrawlWar ? GameMode.BrawlWar
            : Mode == GameMode.BrawlShow ? GameMode.BrawlShow : GameMode.Brawl;
        EnterMenu();
        OpenRobotSelect(variant);
    }

    /// <summary>
    /// The MARTIAL ARTS SHOW: the cyan pick alone on the stage, running its
    /// whole repertoire with captions — the judging bench for every clip.
    /// </summary>
    public void StartBrawlShow()
    {
        Mode = GameMode.BrawlShow;
        DestroySpectatorRig();
        ResetMatchState();
        RestoreAllDeRez();

        _brawlShow = BrawlShow.Begin(this, _roster, _cyanRobot);

        _menuCanvas.SetActive(false);
        ShowOverlay("MARTIAL ARTS SHOW   ·   ← → — Move   ·   ESC — Menu",
                    "MARTIAL ARTS SHOW   ·   tap MENU to go back");
        LockCursor(false);
    }

    /// <summary>
    /// The versus mode: the two picked robots duel on the Brawl stage, best
    /// of three. No arena select in front of it — the stage is its own set,
    /// exactly as Commander's battlefield is.
    /// </summary>
    public void StartBrawl() => StartBrawlMode(playerControls: true);

    /// <summary>Brawl's exhibition bout: two CPU corners, the couch watches.</summary>
    public void StartBrawlWar() => StartBrawlMode(playerControls: false);

    void StartBrawlMode(bool playerControls)
    {
        Mode = playerControls ? GameMode.Brawl : GameMode.BrawlWar;
        DestroySpectatorRig();
        ResetMatchState();
        RestoreAllDeRez();

        _brawl = BrawlController.Begin(this, _roster, _cyanRobot, _magentaRobot, playerControls,
            BrawlDifficulty.For(Mode), BrawlArenas.SelectedFor(Mode));

        _menuCanvas.SetActive(false);
        if (playerControls)
            ShowOverlay("WASD — Move   ·   J — Punch   ·   K — Kick   ·   C — Block   ·   " +
                        "L — Blast   ·   ? — Help   ·   ESC — Menu",
                        "BRAWL   ·   tap ? for help   ·   tap MENU to go back");
        else
            ShowOverlay("BRAWL: AI v AI — F3 for Hitboxes — ESC for Menu",
                        "BRAWL: AI v AI — tap MENU to go back");
        LockCursor(false);
    }

    // ---------- Tank Raid ----------

    /// <summary>
    /// TANK RAID: the vertical scroller. The player's chosen robot drives up a
    /// battlefield with no end in its tank form, hull on one stick and turret on
    /// the other, while raiders come down it. Its own set, like every mode that
    /// builds its world rather than borrowing an arena — so no arena select
    /// comes in front of it.
    /// </summary>
    public void StartTankRaid()
    {
        Mode = GameMode.TankRaid;
        CloseRobotSelect();
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        _tankRaid = TankRaid.Begin(this, _roster, _cyanRobot);

        _menuCanvas.SetActive(false);
        ShowOverlay("WASD — Drive   ·   Mouse — Turret   ·   the guns fire themselves   ·   " +
                    "grab the pods   ·   ESC — Menu",
                    "left thumb drives   ·   right thumb aims   ·   tap MENU to go back");
        LockCursor(false);
    }

    // ---------- Chinese Quest ----------

    /// <summary>
    /// CHINESE QUEST's front screen: which words to fight over. There is no
    /// robot select in front of it — the hero and the four answers are cast
    /// from the roster automatically, because five DIFFERENT robots is the
    /// point (four identical ones holding four different words are hard to
    /// tell apart at a glance, and glancing is the whole input).
    /// </summary>
    public void OpenChineseDeckSelect() => OpenChineseDeckSelect(GameMode.ChineseQuest);

    /// <summary>The runner's front door — same deck picker, different launcher.</summary>
    public void OpenChineseRunDeckSelect() => OpenChineseDeckSelect(GameMode.ChineseRun);

    void OpenChineseDeckSelect(GameMode mode)
    {
        _menuCanvas.SetActive(false);
        CloseChineseDeckSelect();
        _chineseDeckSelect = ChineseDeckSelect.Build(this, mode);
    }

    /// <summary>Escape/BACK from the deck screen goes back a step, to the main menu.</summary>
    public void CancelChineseDeckSelect()
    {
        CloseChineseDeckSelect();
        _menuCanvas.SetActive(true);
    }

    void CloseChineseDeckSelect()
    {
        if (_chineseDeckSelect != null)
        {
            Destroy(_chineseDeckSelect);
            _chineseDeckSelect = null;
        }
    }

    /// <summary>
    /// A deck was picked: stand five robots around a character and start
    /// asking. Its stage is its own set, exactly as Commander's battlefield
    /// and Brawl's ring are, so no arena select comes in front of it.
    /// </summary>
    public void StartChineseQuest(int deckIndex)
    {
        Mode = GameMode.ChineseQuest;
        CloseChineseDeckSelect();
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        _chineseQuest = ChineseQuest.Begin(this, _roster, deckIndex);

        _menuCanvas.SetActive(false);
        ShowOverlay("Read the character   ·   click a robot or its card   ·   " +
                    "1 – 4 to answer   ·   ESC — Menu",
                    "Read the character   ·   tap a robot or its card   ·   tap MENU to go back");
        LockCursor(false);
    }

    /// <summary>
    /// CHINESE RUN: the same question asked at a sprint. A road with no end,
    /// four robots standing across it holding the four meanings, and only as
    /// long to answer as it takes to reach them. Its own set, like every
    /// Chinese and Brawl mode — no arena select in front of it.
    /// </summary>
    public void StartChineseRun(int deckIndex)
    {
        Mode = GameMode.ChineseRun;
        CloseChineseDeckSelect();
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        _chineseRun = ChineseRun.Begin(this, _roster, deckIndex);

        _menuCanvas.SetActive(false);
        ShowOverlay("Answer before you reach them   ·   click a robot or its card   ·   " +
                    "1 – 4 to answer   ·   ESC — Menu",
                    "Answer before you reach them   ·   tap a robot or its card   ·   " +
                    "tap MENU to go back");
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
        ShowOverlay($"{ArenaLibrary.Get(_arenaIndex).DisplayName}   ·   [ ] — Arena   ·   " +
                    "R — New Layout   ·   WASD/QE — Fly   ·   ESC — Menu",
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

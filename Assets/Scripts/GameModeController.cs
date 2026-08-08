using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public enum GameMode { Menu, PlayerVsAI, AIvAI, ArenaPreview, Commander, OnlinePvP, Brawl, BrawlWar, BrawlShow, TowerDefense, ChineseQuest, ChineseRun, TankRaid, TankRaidWar, Dogfight, DogfightWar, Story }

/// <summary>
/// Owns the game's mode flow: main menu → Player v AI / AI v AI / Arena Builder,
/// and Escape back to the menu from any mode. Lives on the GameController
/// object; discovers the player and bots at startup.
/// </summary>
public class GameModeController : MonoBehaviour
{
    public static GameModeController Instance { get; private set; }

    public GameMode Mode
    {
        get => _mode;
        private set { MetricsModeSwap(_mode, value); _mode = value; }
    }
    GameMode _mode = GameMode.Menu;
    float _matchT0;

    // One seam sees every mode transition, so every mode — current and future —
    // gets match_start / match_end / menu_view without per-mode wiring
    // (ANALYTICS_PLAN.md). Metrics.Track never throws, so neither can this.
    void MetricsModeSwap(GameMode from, GameMode to)
    {
        if (from != GameMode.Menu)
            Metrics.Track("match_end", ("mode", from.ToString()),
                ("duration_s", (int)(Time.realtimeSinceStartup - _matchT0)));
        if (to == GameMode.Menu)
        {
            Metrics.Track("menu_view");
        }
        else
        {
            _matchT0 = Time.realtimeSinceStartup;
            Metrics.Track("match_start", ("mode", to.ToString()));
        }
    }

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
    bool _hintWasUnlocked;
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
    StoryDirector _story;
    GameObject _brawlStageSelect;
    ChineseQuest _chineseQuest;
    ChineseRun _chineseRun;
    TankRaid _tankRaid;
    Dogfight _dogfight;
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
        // Teams read from the robots' own paint now, so the floor rings the
        // scene was built with come off. Done here rather than only in
        // ArenaBuilder because they are baked into the saved scene — and
        // before TeamRoster clones anyone, so no clone inherits one.
        foreach (var bot in _bots)
            if (bot != null)
            {
                RobotFactory.StripTeamRing(bot.transform);
                WeaponCatalog.TopUp(bot.GetComponent<WeaponLoadout>());
            }
        if (_player != null)
        {
            RobotFactory.StripTeamRing(_player.transform);
            // Weapons added to the catalog since the scene was last built exist
            // in code and nowhere else until this runs — see WeaponCatalog.TopUp.
            // Ahead of TeamRoster's cloning, so reinforcements are born with the
            // same arsenal.
            WeaponCatalog.TopUp(_player.GetComponent<WeaponLoadout>());
            // The player wears a real robot now, and the camera sits inside it.
            // Added here rather than in ArenaBuilder so it needs no scene
            // rebuild, and unconditionally: every mode this player appears in
            // is first person.
            if (_player.GetComponent<FirstPersonBody>() == null)
                _player.AddComponent<FirstPersonBody>();
            // ...and the gun in front of it becomes whichever one is selected,
            // rather than the laser blaster the whole arsenal happens to be
            // bolted to.
            WeaponViewModel.Ensure(_playerBrain, _player.transform.Find("Head"));
        }
        _deRezEffects = FindObjectsByType<DeRezEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        _blockManager = GetComponent<ArenaBlockManager>();
        _treasureSpawner = GetComponent<TreasureSpawner>();
        _roster = GetComponent<RobotRoster>();

        _menuCanvas = MainMenu.Build(this);
        BuildOverlay();
        // Builds nothing until a touch actually happens (or the platform is
        // mobile), so desktop play is unaffected.
        TouchControls.Ensure();
        // Corner panel naming the form of whichever robot is on camera, and
        // playing its stop-motion transformation as it morphs.
        TransformCast.Ensure();
        EnterMenu();
    }

    void Update()
    {
        if (Mode != GameMode.Menu && Input.GetKeyDown(KeyCode.Escape)
            && !UnattendedRender.Active)
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

        // Browsers only grant pointer lock inside a user gesture, and an online
        // match starts from a network message — so both players land in the
        // arena with a free cursor and no idea why looking doesn't work. Say so
        // until their first click takes the mouse.
        bool needsClick = IsFirstPersonMatch(Mode) && !TouchControls.Active
            && Cursor.lockState != CursorLockMode.Locked;
        if (needsClick != _hintWasUnlocked)
        {
            _hintWasUnlocked = needsClick;
            ApplyOverlay();
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
        // Back to basics-plus-treasure. Cleared for every mode entry, so the
        // one mode that wants the whole arsenal turns it on afterwards and no
        // other mode can inherit it.
        WeaponLoadout.SetFullArsenalMatch(false);
        _treasureSpawner?.EndMatch();
        RobotReinforcements.DespawnAll();
        // Puts the scene's own cast back the way it was found — the robots a
        // sized team parked, and the ones it built. Every mode entry runs this
        // before it takes the world, so no mode inherits another's team size.
        TeamRoster.Reset();
        TeamBank.Reset();
    }

    public void EnterMenu()
    {
        // Leaving an online match tells the other player before anything is
        // torn down; a no-op in every other mode (and when the match already
        // ended itself — NetMatch guards on its own state).
        //
        // Mode flips FIRST: NetMatch.EndMatch calls back into EnterMenu for
        // the peer-initiated paths, and its `Mode == OnlinePvP` test is what
        // stops this teardown running twice.
        bool wasOnline = Mode == GameMode.OnlinePvP;
        Mode = GameMode.Menu;
        if (wasOnline)
            NetMatch.OnLocalLeftMatch();
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
        // The story stage is the Brawl one, so it hands the world back the
        // same way.
        if (_story != null)
        {
            _story.Teardown();
            _story = null;
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
        // And once more for the sky: the jets are scene-root shootables the
        // same way the tanks are, swept by the same kind of Teardown.
        if (_dogfight != null)
        {
            _dogfight.Teardown();
            _dogfight = null;
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
        // menu's arena backdrop expects the full cast standing in it. Cover
        // churn resumes with them.
        SetBotsHidden(false);
        if (_blockManager != null)
            _blockManager.enabled = true;

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
            else if (mode == GameMode.TankRaid || mode == GameMode.TankRaidWar)
            {
                if (TankRaidPick.PlayerDrives) StartTankRaid(); else StartTankRaidWar();
            }
            else if (mode == GameMode.Dogfight) StartDogfight();
            else if (mode == GameMode.DogfightWar) StartDogfightWar();
            else StartPlayerVsAI();
            return;
        }

        _pendingMode = mode;
        Metrics.Track("mode_select", ("mode", mode.ToString()));
        _menuCanvas.SetActive(false);
        CloseRobotSelect();
        _robotSelect = RobotSelectMenu.Build(this, _roster, mode, _cyanRobot, _magentaRobot);
    }

    public void CancelRobotSelect()
    {
        Metrics.Track("select_cancel", ("screen", "robot"));
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
        Metrics.Track("robot_select",
            ("cyan", _roster.Get(cyanIndex).displayName),
            ("magenta", _roster.Get(magentaIndex).displayName),
            ("mode", _pendingMode.ToString()));
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
        // Both of its variants come through this one card: the WHO DRIVES chips
        // on the select screen decide which, and remember the answer.
        if (_pendingMode == GameMode.TankRaid)
        {
            if (TankRaidPick.PlayerDrives) StartTankRaid(); else StartTankRaidWar();
            return;
        }
        // Dogfight spawns both jets fresh from the roster picks, and its sky
        // is its own set — same shape as Tank Raid, twice over.
        if (_pendingMode == GameMode.Dogfight)
        {
            StartDogfight();
            return;
        }
        if (_pendingMode == GameMode.DogfightWar)
        {
            StartDogfightWar();
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

    /// <summary>
    /// The modes where the player IS a robot fighting in the arena, first
    /// person. Everything that belongs to fighting — HUD, touch controls,
    /// the transformation replay — asks this rather than keeping its own
    /// list of modes to fall out of date.
    /// </summary>
    public static bool IsFirstPersonMatch(GameMode mode) =>
        mode == GameMode.PlayerVsAI || mode == GameMode.OnlinePvP;

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

        // The reskin built a fresh set of renderers, all of them visible and all
        // of them around the camera. Hidden now rather than on this component's
        // next tick, which would be a visible frame or two of the robot's own
        // chest plate.
        var hide = _player.GetComponent<FirstPersonBody>();
        if (hide != null)
            hide.Refresh();

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
        // The player is one of the cyan robots, so a 4 v 4 fields them and
        // three allies. Before the brains come on and after the arena loaded:
        // slots are read off the live arena's spawn lines.
        TeamRoster.Apply(TeamSize.PerTeam, playerPlays: true);

        // Everyone gets everything here — the player picks from the ARMS rack,
        // the bots roll the whole catalogue. Only this mode: elsewhere a Weapon
        // Pod is worth crossing the arena for, and a mode where you already own
        // every gun has nothing to airdrop.
        WeaponLoadout.SetFullArsenalMatch(true);

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

        // A treasure gun carried in from an offline match would shift the
        // weapon list this client sends slot indices against — the other side
        // would replay the wrong gun entirely.
        if (_player != null)
        {
            var loadout = _player.GetComponent<WeaponLoadout>();
            if (loadout != null)
                loadout.ClearSpecial();
        }

        // Cover churn rolls unseeded randomness on each machine — left running
        // it would walk the two arenas apart within seconds, and players would
        // be shot through cover that isn't there on the other screen.
        if (_blockManager != null)
            _blockManager.enabled = false;

        _menuCanvas.SetActive(false);
        ShowOverlay("ONLINE MATCH   ·   T Transform   ·   Z Sniper   ·   RMB X-Ray   ·   "
                    + "1/2 Weapons   ·   ESC Leave",
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
        // Nobody is holding a cyan slot here, so both sides field the full size.
        TeamRoster.Apply(TeamSize.PerTeam, playerPlays: false);

        if (_player != null)
            _player.SetActive(false);
        SetBotsActive(true);
        _treasureSpawner?.BeginMatch();

        // Start the broadcast from the active arena's own perch, not a fixed
        // point that may sit inside a ziggurat tier or under a catwalk.
        _spectatorRig = BuildCameraRig("SpectatorCamera", ArenaContext.Current.SpectatorPerch);
        _spectatorRig.AddComponent<SpectatorCamera>();

        _menuCanvas.SetActive(false);
        ShowOverlay($"AI v AI   ·   {TeamSize.Matchup(TeamSize.PerTeam)}   ·   ESC for Menu",
                    $"AI v AI   ·   {TeamSize.Matchup(TeamSize.PerTeam)}   ·   tap MENU to go back");
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
        // The enum collapses Commander's play/war split — this line keeps it.
        Metrics.Track("commander_kind", ("variant", playerCommands ? "play" : "war"));
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
    /// STORY: an episode file performed by the robot cast — the fixed-cast
    /// movie mode (MOVIE_PLAN.md). The cast comes from the episode itself,
    /// so no robot select in front of it; autoExit is the render harness
    /// asking the mode to flag completion instead of waiting for Escape.
    /// </summary>
    public void StartStory(string episodeId = "pilot", bool autoExit = false)
    {
        Mode = GameMode.Story;
        DestroySpectatorRig();
        ResetMatchState();
        RestoreAllDeRez();

        _story = StoryDirector.Begin(this, _roster, episodeId, autoExit);

        _menuCanvas.SetActive(false);
        // A render run keeps the frame clean — no hint bar burned into the
        // movie. Interactive viewers still get told where the exit is.
        if (!autoExit)
            ShowOverlay("STORY   ·   ESC — Menu",
                        "STORY   ·   tap MENU to go back");
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
    public void StartTankRaid() => StartTankRaidMode(playerDrives: true);

    /// <summary>
    /// Tank Raid's exhibition run: the same battlefield with a machine at the
    /// hero's controls. Its own pilot rather than a raider brain — see
    /// <see cref="TankPilot"/> — because the hero's job is to get up the field,
    /// which is a different problem from holding a range.
    /// </summary>
    public void StartTankRaidWar() => StartTankRaidMode(playerDrives: false);

    void StartTankRaidMode(bool playerDrives)
    {
        Mode = playerDrives ? GameMode.TankRaid : GameMode.TankRaidWar;
        CloseRobotSelect();
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        _tankRaid = TankRaid.Begin(this, _roster, _cyanRobot, playerDrives);

        _menuCanvas.SetActive(false);
        if (playerDrives)
            ShowOverlay("WASD — Drive   ·   Mouse — Turret   ·   the guns fire themselves   ·   " +
                        "take the outposts   ·   = — Thumb sticks   ·   ESC — Menu",
                        "left thumb drives   ·   right thumb aims   ·   tap MENU to go back");
        else
            ShowOverlay("TANK RAID: AI v AI — the machine drives, the outposts fall   ·   ESC — Menu",
                        "TANK RAID: AI v AI — tap MENU to go back");
        LockCursor(false);
    }

    // ---------- Dogfight ----------

    /// <summary>
    /// DOGFIGHT: the sky duel. Both picked robots land on pads, fold into
    /// their jet forms by stop motion, and race to five wrecks over a navy
    /// void. Its own set, so no arena select comes in front of it — the Tank
    /// Raid shape with a player in the cyan cockpit.
    /// </summary>
    public void StartDogfight() => StartDogfightMode(playerControls: true);

    /// <summary>Dogfight's exhibition: two CPU pilots, the couch gets a
    /// broadcast camera that answers C and SPACE.</summary>
    public void StartDogfightWar() => StartDogfightMode(playerControls: false);

    void StartDogfightMode(bool playerControls)
    {
        Mode = playerControls ? GameMode.Dogfight : GameMode.DogfightWar;
        CloseRobotSelect();
        DestroySpectatorRig();
        ResetMatchState();
        // Before the characters are hidden — same order every mode uses.
        RestoreAllDeRez();

        _dogfight = Dogfight.Begin(this, _roster, _cyanRobot, _magentaRobot, playerControls);

        _menuCanvas.SetActive(false);
        if (playerControls)
            ShowOverlay("Mouse — Steer / Aim   ·   Click — Guns   ·   R-Click — Missile   ·   " +
                        "T — Transform   ·   F — Flares   ·   C — Cockpit   ·   ? — Help   ·   ESC — Menu",
                        "left thumb steers   ·   right thumb throttles and aims   ·   " +
                        "tap an enemy to LOCK   ·   FIRE sends the missile   ·   tap MENU to go back");
        else
            ShowOverlay("DOGFIGHT: AI v AI   ·   C — Cockpit   ·   SPACE — Next jet   ·   ? — Help   ·   ESC — Menu",
                        "DOGFIGHT: AI v AI   ·   tap MENU to go back");
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
        Metrics.Track("mode_select", ("mode", mode.ToString()));
        _menuCanvas.SetActive(false);
        CloseChineseDeckSelect();
        _chineseDeckSelect = ChineseDeckSelect.Build(this, mode);
    }

    /// <summary>Escape/BACK from the deck screen goes back a step, to the main menu.</summary>
    public void CancelChineseDeckSelect()
    {
        Metrics.Track("select_cancel", ("screen", "deck"));
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
        if (!_hintWasTouch && _hintWasUnlocked)
            message = "CLICK  TO  LOOK   ·   " + message;
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

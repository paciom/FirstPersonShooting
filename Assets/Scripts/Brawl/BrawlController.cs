using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns a Brawl session: swaps the arena world for the fight stage, spawns
/// the two fighters, and hands the world back exactly as the menu expects it.
///
/// The world swap is CommanderController's, minus the NavMesh: deactivate
/// every child of Environment, parent the stage there, and teardown destroys
/// the stage and calls ArenaRuntime.Load(CurrentIndex) — one authority for
/// world state. No bake in either direction: fighters move on a lane, not a
/// mesh, and the arena reload at teardown does its own bake.
/// </summary>
public class BrawlController : MonoBehaviour
{
    public static BrawlController Instance { get; private set; }

    public BrawlFighter Cyan { get; private set; }
    public BrawlFighter Magenta { get; private set; }
    public BrawlCamera Camera { get; private set; }

    GameObject _stageRoot;
    GameObject _cameraRig;
    ArenaBlockManager _blockManager;

    /// <summary>
    /// FPS characters hidden for the duration — restored at teardown. NOT
    /// readonly: a script recompile during Play serializes plain private
    /// fields across the assembly reload but silently resets readonly ones,
    /// and this list is the only record of a cast nothing else can bring
    /// back (the same landmine Commander documents).
    /// </summary>
    List<GameObject> _hiddenCharacters = new List<GameObject>();

    [SerializeField] int _cyanRobot;
    [SerializeField] int _magentaRobot;

    /// <summary>True when a human holds P1; false is the exhibition bout.</summary>
    [SerializeField] bool _playerControls = true;

    /// <summary>The chosen BrawlDifficulty level (1-based) for every CPU corner.</summary>
    [SerializeField] int _difficulty = 3;

    /// <summary>The stage pick (0 = RANDOM) — resolved to a def in Setup.</summary>
    [SerializeField] int _stageSelection;

    public static BrawlController Begin(GameModeController owner, RobotRoster roster,
        int cyanRobot, int magentaRobot, bool playerControls, int difficulty, int stageSelection)
    {
        var go = new GameObject("Brawl");
        go.transform.SetParent(owner.transform, false);
        var controller = go.AddComponent<BrawlController>();
        controller._cyanRobot = cyanRobot;
        controller._magentaRobot = magentaRobot;
        controller._playerControls = playerControls;
        controller._difficulty = difficulty;
        controller._stageSelection = stageSelection;
        controller.Setup(roster);
        return controller;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Setup(RobotRoster roster)
    {
        // The FPS cast has no part in a duel. Caller (StartBrawl) has already
        // run RestoreAllDeRez, so deactivating cannot strand a coroutine
        // mid-de-rez or mid-fold.
        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        // RegrowStrays would march a hidden HANGAR block back into the middle
        // of the stage — same reason Commander switches it off.
        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        // Fresh terrain and prop registries every session.
        BrawlGround.Clear();
        BrawlProps.Clear();

        var def = BrawlArenas.Resolve(_stageSelection);

        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        Transform environment = surface != null ? surface.transform : null;
        if (def.remixArena)
        {
            // The remix fights INSIDE an arena: load it and leave its
            // geometry standing — the strip drops into its middle.
            ArenaRuntime.Load(def.arenaIndex);
        }
        else if (environment != null)
        {
            // Deactivate BEFORE building, so the stage itself is never on the
            // list of things we turned off.
            foreach (Transform child in environment)
                child.gameObject.SetActive(false);
        }

        _stageRoot = BrawlStage.Build(environment, def, roster);

        _cameraRig = BuildCameraRig();
        Camera = _cameraRig.GetComponent<BrawlCamera>();

        Cyan = BrawlFighter.Spawn(_stageRoot.transform, roster, _cyanRobot, 0);
        Magenta = BrawlFighter.Spawn(_stageRoot.transform, roster, _magentaRobot, 1);
        Cyan.Opponent = Magenta;
        Magenta.Opponent = Cyan;

        // P1 is a human's hands or a second brain — the fighter can't tell.
        // Touch pads only exist when someone is actually driving. Every CPU
        // corner runs the chosen difficulty level; per-spawn jitter keeps
        // two same-level brains from mirroring.
        if (_playerControls)
        {
            gameObject.AddComponent<BrawlInput>().Fighter = Cyan;
            gameObject.AddComponent<BrawlTouch>();
        }
        else
        {
            var cyanBrain = gameObject.AddComponent<BrawlBrain>();
            cyanBrain.Fighter = Cyan;
            cyanBrain.ApplyDifficulty(_difficulty);
        }
        var magentaBrain = gameObject.AddComponent<BrawlBrain>();
        magentaBrain.Fighter = Magenta;
        magentaBrain.ApplyDifficulty(_difficulty);

        BrawlDebug.Attach(gameObject, Cyan, Magenta);

        Camera.SetTargets(Cyan.transform, Magenta.transform);

        // The CPU wears its level on the health bar.
        string levelTag = "  ·  " + BrawlDifficulty.NameOf(_difficulty);
        string cyanName = DisplayName(roster, _cyanRobot, _playerControls ? "PLAYER" : "CYAN");
        string magentaName = DisplayName(roster, _magentaRobot, _playerControls ? "CPU" : "MAGENTA");
        var hud = BrawlHud.Build(transform,
            _playerControls ? cyanName : cyanName + levelTag,
            magentaName + levelTag, def.name);
        gameObject.AddComponent<BrawlMatch>().Bind(Cyan, Magenta, hud,
            _playerControls ? "PLAYER  WINS" : cyanName.ToUpperInvariant() + "  WINS",
            _playerControls ? "CPU  WINS" : magentaName.ToUpperInvariant() + "  WINS");

        // Crystal corners bite: a shove that slams the lane end sparks and
        // stretches the stagger.
        if (def.crystalCorners)
        {
            System.Action<BrawlFighter, float> slam = (victim, speed) =>
            {
                victim.AddStun(0.18f);
                Vector3 wall = victim.transform.position + Vector3.up * 1.1f;
                VfxUtil.ImpactBurst(wall, new Color(0.45f, 0.9f, 1f));
                BrawlAudio.Play(BrawlAudio.Id.Graze, wall, 0.9f);
                Camera.Kick(0.10f);
            };
            Cyan.OnWallSlam += slam;
            Magenta.OnWallSlam += slam;
        }

        // Every landed hit thumps the camera and freezes the world for a
        // few hundredths — the hit-stop that makes contact feel like contact.
        System.Action<BrawlFighter, int, bool> thump = (victim, damage, knockdown) =>
        {
            Camera.Kick(knockdown ? 0.16f : 0.07f);
            HitStop(knockdown ? 0.11f : 0.05f);
        };
        Cyan.OnHitLanded += thump;
        Magenta.OnHitLanded += thump;
    }

    // Realtime, because scaled time is exactly what a hit-stop stops.
    float _hitStopUntil;

    void HitStop(float seconds)
    {
        Time.timeScale = 0.06f;
        _hitStopUntil = Time.realtimeSinceStartup + seconds;
    }

    void Update()
    {
        if (Time.timeScale < 1f && Time.realtimeSinceStartup >= _hitStopUntil)
            Time.timeScale = 1f;
    }

    static string DisplayName(RobotRoster roster, int index, string fallback)
    {
        if (roster == null || !roster.HasRobots)
            return fallback;
        var entry = roster.Get(index);
        return string.IsNullOrEmpty(entry.displayName) ? fallback : entry.displayName;
    }

    /// <summary>
    /// Hand the world back. Called by GameModeController on the way to the
    /// menu; destroys this object too, so a session is strictly one-shot.
    /// </summary>
    public void Teardown()
    {
        // Never hand the menu a frozen world: Escape can land mid-hit-stop.
        Time.timeScale = 1f;

        BrawlProps.DespawnAll();
        BrawlGround.Clear();

        if (_cameraRig != null)
            Destroy(_cameraRig);

        // DestroyImmediate, not Destroy: ArenaRuntime.Load below re-bakes and
        // (on a first-ever load) captures Environment's children as
        // scene-authored geometry this same frame. A deferred destroy would
        // leave the stage baked into the arena's mesh and captured as
        // geometry to re-activate forever after. The fighters are children of
        // the stage root, so they go with it.
        if (_stageRoot != null)
        {
            DestroyImmediate(_stageRoot);
            _stageRoot = null;
        }

        // Belt and braces for the recompile-during-Play landmine: the list
        // survives a reload as a serialized field, but if it is empty anyway,
        // re-derive it. Brawl is only ever entered from the menu, where the
        // whole cast is active — so any inactive character at teardown is one
        // this session hid.
        if (_hiddenCharacters.Count == 0)
        {
            var player = FindFirstObjectByType<PlayerBrain>(FindObjectsInactive.Include);
            if (player != null && !player.gameObject.activeSelf)
                _hiddenCharacters.Add(player.gameObject);
            foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!bot.gameObject.activeSelf)
                    _hiddenCharacters.Add(bot.gameObject);
        }

        foreach (var go in _hiddenCharacters)
            if (go != null)
                go.SetActive(true);
        _hiddenCharacters.Clear();

        if (_blockManager != null)
            _blockManager.enabled = true;

        // One call restores everything else: arena geometry, NavMesh,
        // character placement, atmosphere, and the block manager's rescan.
        ArenaRuntime.Load(ArenaRuntime.CurrentIndex);

        Destroy(gameObject);
    }

    void Hide(GameObject character)
    {
        if (character == null || !character.activeSelf)
            return;
        character.SetActive(false);
        _hiddenCharacters.Add(character);
    }

    GameObject BuildCameraRig()
    {
        var rig = new GameObject("BrawlCamera");
        // Tagged so Camera.main keeps working while the player camera is
        // inactive — FlashQuad billboarding and every screen helper read it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = BrawlCamera.Fov;
        // The frustum sees past the stage edges into nothing; clear to the
        // same colour the backdrop resolves to so "nothing" reads as depth.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<BrawlCamera>();
        return rig;
    }
}

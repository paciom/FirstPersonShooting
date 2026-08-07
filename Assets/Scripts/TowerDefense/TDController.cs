using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns a Tower Defense session: swaps the world from whatever arena is
/// loaded to the canyon, runs the siege, and hands the world back exactly
/// as the menu expects it.
///
/// The lifecycle is CommanderController's, beat for beat — deactivate the
/// Environment children, build our map under the NavMeshSurface, bake, play,
/// then destroy everything and let ArenaRuntime.Load restore the arena as
/// the one authority on world state. Divergence here is how a mode switch
/// grows a haunted arena.
/// </summary>
public class TDController : MonoBehaviour
{
    public static TDController Instance { get; private set; }

    /// <summary>The build cursor — GameModeController's Escape chain asks it first.</summary>
    public TDPlacer Placer { get; private set; }

    GameObject _mapRoot;
    GameObject _cameraRig;
    ArenaBlockManager _blockManager;

    /// <summary>
    /// FPS characters hidden for the duration — restored at teardown. NOT
    /// readonly: a recompile during Play keeps plain private fields and
    /// silently resets readonly ones, and this list is the only record of
    /// a cast nothing else can bring back.
    /// </summary>
    List<GameObject> _hiddenCharacters = new List<GameObject>();

    /// <summary>The MAP CODE this session's canyon is rolled from.</summary>
    [SerializeField] int _seed;

    /// <summary>
    /// The Core's Building, held directly: its Definition is a TD-catalog
    /// def that Building can't re-derive after a recompile (it only knows
    /// BuildingCatalog), so anyone who needs "the Core" asks here instead
    /// of scanning definitions.
    /// </summary>
    [SerializeField] Building _core;

    public Building Core => _core;

    public static TDController Begin(GameModeController owner, int seed)
    {
        var go = new GameObject("TowerDefense");
        go.transform.SetParent(owner.transform, false);
        var controller = go.AddComponent<TDController>();
        controller._seed = seed;
        controller.Setup();
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

    void Setup()
    {
        // The FPS cast has no part in this mode, and live NavMeshAgents must
        // not stand on a mesh about to be re-baked from under them. The
        // caller has already run RestoreAllDeRez, so hiding can't strand a
        // coroutine mid-cycle.
        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        // Same reason Commander does this: RegrowStrays would march a hidden
        // HANGAR block into the middle of the canyon.
        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if (surface == null)
        {
            Debug.LogError("[TowerDefense] No NavMeshSurface in the scene — cannot build the canyon.");
            return;
        }

        // Deactivate BEFORE building, so the canyon is never on the list of
        // things we turned off.
        foreach (Transform child in surface.transform)
            child.gameObject.SetActive(false);

        _mapRoot = TDMap.Build(surface.transform, _seed);
        // The Commander night: same fog, same flat ambient — one battlefield
        // atmosphere across the strategy modes, and ArenaRuntime.Load already
        // knows how to undo it.
        CommanderMap.ApplyAtmosphere();
        surface.BuildNavMesh();
        // After the bake — the skirt must never be walkable.
        CommanderMap.BuildSkirt(_mapRoot.transform);

        _cameraRig = BuildCameraRig();

        TDEconomy.Reset();

        // The Photon Core: the Command Center model in its defender's role,
        // standing in the pocket the whole canyon drains toward.
        _core = Building.Construct(TDTowerCatalog.CoreDefinition, 0,
            TDMap.CoreSite + Vector3.up * 0.12f);

        Placer = gameObject.AddComponent<TDPlacer>();
        gameObject.AddComponent<TDGarrison>();
        gameObject.AddComponent<TDHud>();
        gameObject.AddComponent<TDDirector>();
        gameObject.AddComponent<TDWaves>();
        gameObject.AddComponent<TDMatch>();
        gameObject.AddComponent<CommanderTouch>()
            .Bind(_cameraRig.GetComponent<CommanderCamera>(), null);
    }

    void Update()
    {
        // H — home: snap the view back to the Core, the one place the
        // defense always needs to find fast. Same framing as the opening
        // shot, so "home" always looks like home.
        if (!Input.GetKeyDown(KeyCode.H))
            return;
        var camera = _cameraRig != null ? _cameraRig.GetComponent<CommanderCamera>() : null;
        if (camera != null)
            camera.SnapTo(TDMap.CoreSite + new Vector3(0f, 0f, 10f), 30f);
    }

    /// <summary>
    /// Hand the world back. Called by GameModeController on the way to the
    /// menu; destroys this object too, so a session is strictly one-shot.
    /// </summary>
    public void Teardown()
    {
        if (_cameraRig != null)
            Destroy(_cameraRig);

        // DestroyImmediate, not Destroy: ArenaRuntime.Load below re-bakes
        // the NavMesh this same frame — a deferred destroy would bake the
        // canyon into the arena's mesh. Same hazard, same cure as Commander.
        if (_mapRoot != null)
        {
            DestroyImmediate(_mapRoot);
            _mapRoot = null;
        }

        // Raiders are CommanderUnits, so Commander's sweep retires them —
        // and the bolts and mortar orbs in flight with them.
        CommanderArmy.DespawnAll();
        Building.DespawnAll();

        // Belt and braces for the recompile-during-Play landmine: if the
        // serialized list is empty anyway, re-derive it — TD is only ever
        // entered from the menu, where the whole cast was active, so any
        // inactive character at teardown is one this session hid.
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
        // character placement, atmosphere, block-manager rescan.
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
        var rig = new GameObject("TDCamera");
        // Tagged so Camera.main keeps working while the player camera is
        // inactive — every screen-ray helper and billboard reads it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = CommanderCamera.Fov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = CommanderMap.VoidColor;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;

        var rts = rig.AddComponent<CommanderCamera>();
        // This map is half Commander's size; the pan clamps must know. The
        // south grace goes high enough that the height-aware body clamp
        // never governs — on a 90 m board it would otherwise pin the focus
        // at mid-map and eat every southward drag; the skirt covers what
        // the camera overhangs.
        rts.worldHalfExtent = TDMap.HalfExtent;
        rts.southClampGrace = 60f;
        // No selection in this mode, so the left button may as well pan —
        // the placer takes the mouse only while a ghost is up.
        rts.primaryDragPans = true;
        // Open on the Core with the canyon ahead — your base bottom-of-
        // screen, the war to the north, as every RTS opens. Height 30: at
        // the default 45 the south clamp can't centre this smaller map's
        // pocket, and the opening shot should hold the whole first fight.
        rts.SnapTo(TDMap.CoreSite + new Vector3(0f, 0f, 10f), 30f);
        return rig;
    }
}

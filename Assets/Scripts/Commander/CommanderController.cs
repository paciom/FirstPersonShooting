using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns a Commander session: swaps the world from whatever arena is loaded to
/// the battlefield, runs the mode, and hands the world back exactly as the
/// menu expects it.
///
/// The swap borrows ArenaRuntime's own trick in reverse. Building the
/// battlefield means deactivating every child of Environment (the scene's
/// HANGAR geometry, or a generated arena, or both) and parenting our map there
/// instead, so the NavMeshSurface's Children-scoped bake sees only Commander
/// geometry. Teardown then simply destroys the map and calls
/// ArenaRuntime.Load(CurrentIndex), which already knows how to rebuild the
/// arena, re-activate scene-authored geometry, re-bake, replace characters and
/// reapply atmosphere — one authority for world state instead of two.
/// </summary>
public class CommanderController : MonoBehaviour
{
    public static CommanderController Instance { get; private set; }

    /// <summary>The player's input surface — GameModeController's Escape handling asks it first.</summary>
    public CommanderSelection Selection { get; private set; }

    /// <summary>The build cursor. Escape cancels it before it cancels anything else.</summary>
    public BuildPlacer Placer { get; private set; }

    GameObject _mapRoot;
    GameObject _cameraRig;
    ArenaBlockManager _blockManager;

    /// <summary>
    /// FPS characters hidden for the duration — restored at teardown. NOT
    /// readonly: a script recompile during Play serializes plain private
    /// fields across the assembly reload but silently resets readonly ones,
    /// and this list is the only record of a cast nothing else can bring back.
    /// </summary>
    List<GameObject> _hiddenCharacters = new List<GameObject>();

    /// <summary>True when a human holds the cyan seat; false is the AI-war spectator mode.</summary>
    [SerializeField] bool _playerCommands = true;

    public static CommanderController Begin(GameModeController owner, bool playerCommands)
    {
        var go = new GameObject("Commander");
        go.transform.SetParent(owner.transform, false);
        var controller = go.AddComponent<CommanderController>();
        controller._playerCommands = playerCommands;
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
        // not be standing on a mesh that is about to be re-baked out from
        // under them. Caller (GameModeController.StartCommander) has already
        // run RestoreAllDeRez, so deactivating cannot strand a coroutine
        // mid-de-rez — the landmine that RestoreAllDeRez exists for.
        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        // ArenaBlockManager keeps relocating and regrowing its cached cover
        // blocks in every mode — RegrowStrays would happily march a hidden
        // HANGAR block back to visibility in the middle of the battlefield.
        // Disabled for the duration; teardown's arena reload rescans it anyway.
        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if (surface == null)
        {
            Debug.LogError("[Commander] No NavMeshSurface in the scene — cannot build the battlefield.");
            return;
        }

        // Deactivate BEFORE building, so the battlefield itself is never on
        // the list of things we turned off.
        foreach (Transform child in surface.transform)
            child.gameObject.SetActive(false);

        _mapRoot = CommanderMap.Build(surface.transform);
        CommanderMap.ApplyAtmosphere();
        surface.BuildNavMesh();
        // After the bake — the skirt must never be walkable.
        CommanderMap.BuildSkirt(_mapRoot.transform);

        _cameraRig = BuildCameraRig();

        // Armies after the bake: units are NavMeshAgents and need the mesh
        // under their feet from frame one. The roster rides on the same
        // GameController object that owns this controller's parent.
        CommanderEconomy.Reset();

        // Each side opens with its Command Center standing on the pad — the
        // adjacency seed every other structure grows from, and the thing
        // whose loss will end the match.
        for (int team = 0; team < 2; team++)
            Building.Construct(BuildingCatalog.Get(BuildingCatalog.CommandCenter), team,
                CommanderMap.BaseSite(team) + Vector3.up * 0.12f);

        CommanderArmy.SpawnSkirmish(GetComponentInParent<RobotRoster>());

        // The magenta seat is always an AI. The cyan seat is the player's
        // UI — or a second AI plus the auto-director, and that is the whole
        // difference between commanding the war and watching it.
        var magenta = gameObject.AddComponent<CommanderAI>();
        magenta.teamId = 1;
        if (_playerCommands)
        {
            Selection = gameObject.AddComponent<CommanderSelection>();
            Placer = gameObject.AddComponent<BuildPlacer>();
            gameObject.AddComponent<CommanderHud>();
        }
        else
        {
            var cyan = gameObject.AddComponent<CommanderAI>();
            cyan.teamId = 0;
            _cameraRig.AddComponent<CommanderDirector>();
        }
        gameObject.AddComponent<CommanderMatch>();
        gameObject.AddComponent<CommanderMinimap>();
        gameObject.AddComponent<CommanderTouch>()
            .Bind(_cameraRig.GetComponent<CommanderCamera>(), Selection);
    }

    void Update()
    {
        // H — home: snap the view back to your Command Center (cyan's, in
        // either mode — it is the bottom-of-screen base).
        if (!Input.GetKeyDown(KeyCode.H))
            return;
        var camera = _cameraRig != null ? _cameraRig.GetComponent<CommanderCamera>() : null;
        if (camera == null)
            return;
        foreach (var building in Building.All)
        {
            if (building != null && building.TeamId == 0 && building.IsAlive
                && building.Definition != null && building.Definition.isHeadquarters)
            {
                camera.SnapTo(building.transform.position + new Vector3(0f, 0f, 6f));
                return;
            }
        }
    }

    /// <summary>
    /// Hand the world back. Called by GameModeController on the way to the
    /// menu; destroys this object too, so a session is strictly one-shot.
    /// </summary>
    public void Teardown()
    {
        if (_cameraRig != null)
            Destroy(_cameraRig);

        // DestroyImmediate, not Destroy: ArenaRuntime.Load below re-bakes the
        // NavMesh and (on a first-ever load) captures Environment's children
        // as scene-authored geometry this same frame. A deferred destroy would
        // leave the battlefield alive for both — baked into the arena's mesh
        // and captured as geometry to re-activate forever after.
        if (_mapRoot != null)
        {
            DestroyImmediate(_mapRoot);
            _mapRoot = null;
        }

        // Units go with the map, and just as immediately — they are agents
        // standing on the mesh the arena bake below is about to replace.
        CommanderArmy.DespawnAll();
        Building.DespawnAll();

        // Belt and braces for the recompile-during-Play landmine: the list
        // survives a reload as a serialized field, but if it is empty anyway,
        // re-derive it. Commander is only ever entered from the menu, where
        // the whole cast is active — so any inactive character at teardown is
        // one this session hid.
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

        // One call restores everything else: arena geometry (rebuilt or
        // re-activated), NavMesh, character placement, atmosphere, and the
        // block manager's rescan.
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
        var rig = new GameObject("CommanderCamera");
        // Tagged so Camera.main keeps working while the player camera is
        // inactive — FlashQuad billboarding and every screen-ray helper read it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        // Tighter than the FPS's 65°: less perspective splay on a look-down
        // view, closer to the classic RTS read.
        cam.fieldOfView = CommanderCamera.Fov;
        // Where the frustum overshoots the world's edge at high zoom, it
        // clears to the same colour the fog and the skirt resolve to.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = CommanderMap.VoidColor;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;

        var rts = rig.AddComponent<CommanderCamera>();
        // Open on the cyan base with the map ahead of it, the way an RTS
        // match starts: your base bottom-of-screen, the war to the north.
        rts.SnapTo(CommanderMap.BaseSite(0) + new Vector3(0f, 0f, 8f));
        return rig;
    }
}

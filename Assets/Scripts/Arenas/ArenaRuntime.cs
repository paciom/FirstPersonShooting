using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Swaps the world to a different arena while the game is running: tears down
/// the last one, builds the new geometry, re-bakes the NavMesh, scatters cover
/// and moves every character to its new spawn.
///
/// HANGAR is special. It is authored into the scene by ArenaBuilder rather than
/// generated, so loading it re-activates that geometry and loading anything
/// else deactivates it. That is what lets arenas ship without re-running the
/// scene builder — arena code takes effect on Play alone.
/// </summary>
public static class ArenaRuntime
{
    public static int CurrentIndex { get; private set; }

    /// <summary>The scene's own Environment children — HANGAR, captured once.</summary>
    static readonly List<GameObject> SceneAuthored = new List<GameObject>();
    static bool _captured;

    /// <summary>Root of whatever was generated for the current arena.</summary>
    static GameObject _built;

    public static void Load(int index)
    {
        var def = ArenaLibrary.Get(index);
        if (def == null)
        {
            Debug.LogError("[ArenaRuntime] No arenas registered.");
            return;
        }

        var surface = Object.FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if (surface == null)
        {
            Debug.LogError("[ArenaRuntime] No NavMeshSurface in the scene — cannot load an arena.");
            return;
        }
        Transform env = surface.transform;

        CaptureSceneAuthored(env);

        // DestroyImmediate, not Destroy: the NavMesh bake below runs this same
        // frame and would otherwise still see the outgoing arena's geometry.
        if (_built != null)
        {
            Object.DestroyImmediate(_built);
            _built = null;
        }
        ArenaMaterials.Clear();

        bool sceneArena = def.SceneAuthored;
        foreach (var go in SceneAuthored)
            if (go != null)
                go.SetActive(sceneArena);

        if (!sceneArena)
        {
            _built = new GameObject($"Arena_{def.DisplayName}");
            _built.transform.SetParent(env, false);
            def.Build(_built.transform, new ArenaKit(_built.transform, def.DisplayName));
        }

        ArenaContext.Current = def;
        CurrentIndex = index;

        def.ApplyAtmosphere();
        ApplyBloom(def.Palette.bloom);

        surface.BuildNavMesh();

        // Cover goes up AFTER the bake, so its carving obstacles drive bot
        // pathing instead of being baked into the floor as permanent walls.
        if (!sceneArena)
            BuildCover(def);

        PlaceCharacters(def);
        RefreshManagers();

        Debug.Log($"[ArenaRuntime] Loaded {def.DisplayName}.");
    }

    /// <summary>
    /// Remember the scene's authored geometry the first time we touch it, so
    /// HANGAR can be restored by re-activation rather than rebuilt.
    /// </summary>
    static void CaptureSceneAuthored(Transform env)
    {
        if (_captured)
            return;
        _captured = true;
        foreach (Transform child in env)
            SceneAuthored.Add(child.gameObject);
    }

    // ---------- cover ----------

    static void BuildCover(ArenaDefinition def)
    {
        if (def.CoverCount <= 0 || _built == null)
            return;

        var p = def.Palette;
        var mats = def.CoverMaterials();
        if (mats == null || mats.Length == 0)
            mats = new[] { ArenaMaterials.Lit("Cover_Fallback", p.wall) };

        // Fixed seed: the same arena lays out the same way every time it loads,
        // so a layout problem found once can be looked at again.
        var rng = new System.Random(def.DisplayName.GetHashCode());
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        var keepOut = def.KeepOut;
        var placed = new List<Vector3>();
        float extent = def.CoverHalfExtent;

        int attempts = 0;
        while (placed.Count < def.CoverCount && attempts++ < 800)
        {
            var size = new Vector3(Next(1.4f, 3.4f), Next(1.2f, 2.4f), Next(1f, 1.9f));
            var pos = new Vector3(Next(-extent, extent), def.GroundY + size.y * 0.5f, Next(-extent, extent));

            if (!def.IsOpenFloor(pos))
                continue;

            bool blocked = false;
            foreach (var keep in keepOut)
            {
                var flat = new Vector3(keep.x - pos.x, 0f, keep.z - pos.z);
                if (flat.magnitude < 4.5f) { blocked = true; break; }
            }
            foreach (var other in placed)
            {
                if (blocked) break;
                var flat = new Vector3(other.x - pos.x, 0f, other.z - pos.z);
                if (flat.magnitude < 3f) blocked = true;
            }
            if (blocked)
                continue;
            placed.Add(pos);

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Cover";
            block.transform.SetParent(_built.transform, false);
            block.transform.position = pos;
            block.transform.rotation = Quaternion.Euler(0f, Next(-45f, 45f), 0f);
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = mats[placed.Count % mats.Length];

            var obstacle = block.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;   // scaled by the transform to match the cube
            obstacle.carving = true;

            var ab = block.AddComponent<ArenaBlock>();
            ab.color = p.accentB;
            ab.maxHealth = 55f;
        }
    }

    // ---------- characters ----------

    static void PlaceCharacters(ArenaDefinition def)
    {
        var playerBrain = Object.FindFirstObjectByType<PlayerBrain>(FindObjectsInactive.Include);
        if (playerBrain != null)
            Move(playerBrain.gameObject, def.PlayerSpawn, 0f);

        var team0 = def.TeamSpawns(0);
        var team1 = def.TeamSpawns(1);
        int next0 = 0, next1 = 0;

        var brains = Object.FindObjectsByType<AIBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var brain in brains)
        {
            if (brain == null)
                continue;
            var shield = brain.GetComponent<EnergyShield>();
            int team = shield != null ? shield.teamId : 1;

            Vector3 spot;
            float yaw;
            if (team == 0)
            {
                spot = team0[next0++ % team0.Length];
                yaw = 0f;
            }
            else
            {
                spot = team1[next1++ % team1.Length];
                yaw = 180f;
            }
            Move(brain.gameObject, spot, yaw);
        }
    }

    /// <summary>
    /// Move a character to its new spawn — and re-seed the spawn its de-rez
    /// cycle returns it to. DeRezEffect captures that at Awake, so without this
    /// every character re-materializes at the PREVIOUS arena's coordinates,
    /// usually inside a wall.
    /// </summary>
    static void Move(GameObject go, Vector3 position, float yaw)
    {
        var rotation = Quaternion.Euler(0f, yaw, 0f);

        // Arena Builder deactivates the player entirely. Agents and character
        // controllers do nothing useful on an inactive object, so move the
        // transform directly and let it start from there when it comes back.
        if (!go.activeInHierarchy)
        {
            go.transform.SetPositionAndRotation(position, rotation);
            var sleeping = go.GetComponent<DeRezEffect>();
            if (sleeping != null)
                sleeping.SetSpawn(position, rotation);
            return;
        }

        var agent = go.GetComponent<NavMeshAgent>();
        var motor = go.GetComponent<CharacterMotor>();
        if (agent != null && agent.enabled)
        {
            if (!agent.Warp(position))
            {
                agent.enabled = false;
                go.transform.position = position;
                agent.enabled = true;
            }
        }
        else if (motor != null)
        {
            motor.Teleport(position);
        }
        else
        {
            go.transform.position = position;
        }
        go.transform.rotation = rotation;

        var deRez = go.GetComponent<DeRezEffect>();
        if (deRez != null)
            deRez.SetSpawn(position, rotation);
    }

    // ---------- managers ----------

    static void RefreshManagers()
    {
        // ArenaBlockManager caches its block list (and its shatter subscriptions)
        // at Start. After a swap that list is all destroyed or deactivated
        // blocks, and the new ones would never move, regrow or drop treasure.
        var blockManager = Object.FindFirstObjectByType<ArenaBlockManager>();
        if (blockManager != null)
            blockManager.Rescan();
    }

    static void ApplyBloom(float intensity)
    {
        var volume = Object.FindFirstObjectByType<Volume>();
        if (volume == null)
            return;
        // Volume.profile (not sharedProfile) hands back a runtime clone, so this
        // does not write through to the PhotonArena_PostFX asset on disk.
        if (volume.profile != null && volume.profile.TryGet<Bloom>(out var bloom))
            bloom.intensity.Override(intensity);
    }
}

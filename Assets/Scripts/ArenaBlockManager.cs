using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Drives the living arena. Two jobs:
///
///  * <b>Churn</b> — on a timer, slide an active block slowly to a new spot or,
///    rarely, make one dash there suddenly. Keeps the cover layout in constant,
///    watchable motion.
///  * <b>Renewal</b> — every block that gets shot out grows back somewhere
///    else, so cover keeps circulating instead of the arena wearing flat. One
///    destruction in <see cref="TreasureChance"/> also kicks a treasure out of
///    the wreckage, which makes shooting cover a play rather than just tidying.
/// </summary>
public class ArenaBlockManager : MonoBehaviour
{
    [Header("Cadence")]
    public float minInterval = 2.5f;
    public float maxInterval = 5f;

    [Header("Movement bounds (arena interior)")]
    [Tooltip("Clearance blocks keep from spawn points and fixed props. " +
             "The bounds themselves come from the active arena — see ArenaContext.")]
    public float minSpawnClearance = 4.5f;

    [Header("Renewal")]
    [Tooltip("Seconds between a block shattering and its replacement erupting elsewhere.")]
    public float regrowDelay = 1.3f;

    [Tooltip("Metres the replacement must be from the wreck, so it visibly grows back SOMEWHERE ELSE.")]
    public float relocateDistance = 7f;

    [Tooltip("Chance a destroyed block throws out a treasure.")]
    [Range(0f, 1f)] public float TreasureChance = 1f / 3f;

    [Tooltip("Never let block-pops flood the field past this many live treasures.")]
    public int treasureHardCap = 12;

    struct PendingRegrow
    {
        public ArenaBlock block;
        public float dueAt;
        public Vector3 avoid;
    }

    ArenaBlock[] _blocks;
    TreasureSpawner _spawner;
    readonly List<PendingRegrow> _pending = new List<PendingRegrow>();
    float _nextAction;

    void Start()
    {
        _spawner = GetComponent<TreasureSpawner>();
        Rescan();
    }

    /// <summary>
    /// Re-discover the arena's cover blocks. Called at Start, and again by
    /// ArenaRuntime after an arena swap: the cached list is otherwise full of
    /// destroyed or deactivated blocks from the outgoing arena, and the incoming
    /// ones would never move, regrow, or drop treasure.
    /// </summary>
    public void Rescan()
    {
        Unsubscribe();
        _pending.Clear();
        _blocks = FindObjectsByType<ArenaBlock>(FindObjectsSortMode.None);
        foreach (var block in _blocks)
            if (block != null)
                block.OnShattered += HandleShattered;
        _nextAction = Time.time + 2f;
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Unsubscribe()
    {
        if (_blocks == null)
            return;
        foreach (var block in _blocks)
            if (block != null)
                block.OnShattered -= HandleShattered;
    }

    // ------------------------------------------------------------------ renewal

    void HandleShattered(ArenaBlock block, Vector3 deathPoint)
    {
        _pending.Add(new PendingRegrow
        {
            block = block,
            dueAt = Time.time + regrowDelay,
            avoid = deathPoint,
        });

        // Only during a live match — the arena keeps churning in the builder
        // preview, and loot has no business appearing there.
        if (_spawner != null && _spawner.MatchRunning && Random.value < TreasureChance)
            ThrowTreasureFrom(deathPoint);
    }

    /// <summary>
    /// Kick a treasure out of the wreckage onto reachable floor nearby. The
    /// landing spot is sampled on the NavMesh for the same reason airdrops are:
    /// a prize bots can't path to is just scenery.
    /// </summary>
    void ThrowTreasureFrom(Vector3 deathPoint)
    {
        if (TreasureDrop.Active.Count >= treasureHardCap)
            return;

        Vector3 landing = deathPoint;
        landing.y = 0f;
        bool found = false;
        for (int attempt = 0; attempt < 12 && !found; attempt++)
        {
            var offset = Random.insideUnitCircle.normalized * Random.Range(2f, 4.5f);
            var candidate = new Vector3(deathPoint.x + offset.x, 0f, deathPoint.z + offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
                && !IsOnCover(hit.position))
            {
                landing = hit.position;
                found = true;
            }
        }
        // Nothing clear nearby (dense cover) — the loot would land inside a
        // block, so skip it rather than hide a prize in the scenery.
        if (!found)
            return;

        // No announcer call-out: the eruption happens inside the fight the
        // player is already looking at, and at this rate of block destruction a
        // toast per pop would bury the messages that actually matter. A mine
        // still announces itself when it arms.
        TreasureDrop.Launch(TreasureCatalog.Roll(), deathPoint, landing);
    }

    void TickPendingRegrowth()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var entry = _pending[i];
            if (entry.block == null)
            {
                _pending.RemoveAt(i);
                continue;
            }
            if (Time.time < entry.dueAt)
                continue;
            // Still mid-sink; check again next frame rather than popping it
            // back up through its own descent.
            if (!entry.block.IsHidden)
                continue;

            _pending.RemoveAt(i);
            RelocateAndRegrow(entry.block, entry.avoid);
        }
    }

    /// <summary>Move a hidden block's home to a fresh spot and grow it back there.</summary>
    void RelocateAndRegrow(ArenaBlock block, Vector3 avoid)
    {
        Vector3 spot = RandomSpot(avoid);
        // Home is the resting CENTRE, so it sits half the block's height above
        // whatever the active arena calls ground.
        block.Home = new Vector3(spot.x,
                                 ArenaContext.GroundY + block.transform.localScale.y * 0.5f,
                                 spot.z);
        block.Regrow();
    }

    // ------------------------------------------------------------------ churn

    void Update()
    {
        if (_blocks == null || _blocks.Length == 0)
            return;

        TickPendingRegrowth();

        if (Time.time < _nextAction)
            return;
        _nextAction = Time.time + Random.Range(minInterval, maxInterval);

        // Safety net: anything hidden that never got scheduled (or whose
        // scheduling was lost across a reload) still comes back.
        if (Random.value < 0.4f && RegrowStrays())
            return;
        MoveOne(fast: Random.value > 0.9f);
    }

    bool RegrowStrays()
    {
        var hidden = PickRandom(b => b.IsHidden && !IsPending(b));
        if (hidden == null)
            return false;
        RelocateAndRegrow(hidden, hidden.transform.position);
        return true;
    }

    bool IsPending(ArenaBlock block)
    {
        foreach (var entry in _pending)
            if (entry.block == block)
                return true;
        return false;
    }

    void MoveOne(bool fast)
    {
        var block = PickRandom(b => b.IsActive);
        if (block == null)
            return;
        Vector3 target = RandomSpot(block.transform.position);
        // Slow drift vs. sudden dash.
        block.SlideTo(target, fast ? 0.4f : Random.Range(2.5f, 4f));
    }

    /// <summary>
    /// A spot clear of the team spawns and at least
    /// <see cref="relocateDistance"/> from <paramref name="avoid"/> (the wreck
    /// we're replacing), relaxing the distance rule if the arena is too full to
    /// satisfy it.
    /// </summary>
    Vector3 RandomSpot(Vector3 avoid)
    {
        float extent = ArenaContext.CoverHalfExtent;
        var keepOut = ArenaContext.KeepOut;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            var p = new Vector3(Random.Range(-extent, extent), 0f, Random.Range(-extent, extent));

            bool clear = true;
            foreach (var zone in keepOut)
                if (Vector3.Distance(p, zone) < minSpawnClearance) { clear = false; break; }
            if (!clear)
                continue;

            // Don't park a block on top of loot someone is already running for.
            if (CoversTreasure(p))
                continue;

            // Relax the "somewhere else" rule over the last third of the tries.
            float required = attempt < 16 ? relocateDistance : 0f;
            var flatAvoid = new Vector3(avoid.x, 0f, avoid.z);
            if (Vector3.Distance(p, flatAvoid) < required)
                continue;

            return p;
        }
        return new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
    }

    bool IsOnCover(Vector3 point)
    {
        // One source of truth for "how much room a crate needs".
        float clearance = _spawner != null ? _spawner.blockClearance : 0.8f;
        foreach (var block in _blocks)
            if (block != null && block.CoversPoint(point, clearance))
                return true;
        return false;
    }

    static bool CoversTreasure(Vector3 blockCenter)
    {
        foreach (var drop in TreasureDrop.Active)
            if (drop != null && Vector3.Distance(drop.GroundPoint, blockCenter) < 3f)
                return true;
        return false;
    }

    ArenaBlock PickRandom(System.Predicate<ArenaBlock> filter)
    {
        int start = Random.Range(0, _blocks.Length);
        for (int i = 0; i < _blocks.Length; i++)
        {
            var b = _blocks[(start + i) % _blocks.Length];
            if (b != null && filter(b))
                return b;
        }
        return null;
    }

    /// <summary>Arena Builder mode (R key): scatter every active block to a new spot.</summary>
    public void Reshuffle()
    {
        if (_blocks == null)
            return;
        foreach (var b in _blocks)
            if (b != null && b.IsActive)
                b.SlideTo(RandomSpot(b.transform.position), Random.Range(0.6f, 1.4f));
    }
}

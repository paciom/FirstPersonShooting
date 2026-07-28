using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Match director for the airdrops: decides when something falls, where it
/// lands, and what it is. Lives on the GameController and only runs between
/// <see cref="BeginMatch"/> and <see cref="EndMatch"/>, which
/// <see cref="GameModeController"/> drives — Player-v-AI and AI-v-AI both get
/// drops, the menus and the arena preview don't.
///
/// Landing spots are chosen on the NavMesh so a bot can always path to whatever
/// falls; a crate stranded on top of cover would just be scenery.
/// </summary>
public class TreasureSpawner : MonoBehaviour
{
    [Header("Cadence")]
    [Tooltip("Seconds after the match starts before the first drop.")]
    public float firstDropDelay = 3f;

    [Tooltip("Random seconds between drops (min, max).")]
    public Vector2 dropInterval = new Vector2(3.5f, 6f);

    [Tooltip("Parachutes allowed in the air/on the field at once from the scheduler.")]
    public int maxActiveDrops = 8;

    [Header("Geometry")]
    [Tooltip("Metres above the floor a crate is released from.")]
    public float dropHeight = 16f;

    // The drop area's half-extent used to live here as a fixed 16x16. It now
    // comes from the active arena (ArenaContext.HalfExtent), so a swap moves
    // the drop zone with the geometry.

    [Tooltip("Minimum spacing between two drops so they don't stack.")]
    public float dropSpacing = 6f;

    [Tooltip("Clearance a landing spot needs from every cover block's footprint.")]
    // 0.8 m: half a crate (0.4) plus breathing room. Kept deliberately tight —
    // the arena carries ~40 blocks, and inflating each footprint by a metre or
    // two would swallow most of the floor and start silently skipping drops.
    public float blockClearance = 0.8f;

    public bool MatchRunning { get; private set; }

    ArenaBlock[] _blocks;
    float _nextDrop;
    float _nextBankTick;

    public void BeginMatch()
    {
        ClearAll();
        MatchRunning = true;
        _nextDrop = Time.time + firstDropDelay;
        _nextBankTick = Time.time + 1f;
    }

    public void EndMatch()
    {
        MatchRunning = false;
        ClearAll();
    }

    /// <summary>Remove every crate on the field (match end, mode switch).</summary>
    public void ClearAll()
    {
        // Vanish() mutates the shared list, so walk a snapshot backwards.
        for (int i = TreasureDrop.Active.Count - 1; i >= 0; i--)
        {
            var drop = TreasureDrop.Active[i];
            if (drop != null)
                drop.Vanish();
        }
        TreasureDrop.Active.Clear();
    }

    void Update()
    {
        if (!MatchRunning)
            return;

        // Teams that banked enough gold cash it in for a robot. Ticked here
        // rather than at pickup time so a team whose robots are all mid-de-rez
        // keeps its gold and builds a moment later instead of losing the buy.
        if (Time.time >= _nextBankTick)
        {
            _nextBankTick = Time.time + 1f;
            TeamBank.TryBuildReinforcements();
        }

        if (Time.time < _nextDrop)
            return;
        _nextDrop = Time.time + Random.Range(dropInterval.x, dropInterval.y);

        if (TreasureDrop.Active.Count >= maxActiveDrops)
            return;

        DropOne();
    }

    void DropOne()
    {
        if (!TryPickLandingSpot(out Vector3 spot))
            return;

        var def = TreasureCatalog.Roll();
        TreasureDrop.Spawn(def, spot, dropHeight);

        // At a drop every few seconds the sky is permanently busy, so an
        // "inbound" toast per crate is noise — the marked landing ring and the
        // descending chute are the telegraph. Mines still get called out,
        // because walking into one is the mistake worth warning about.
        if (def.hazard)
            MatchAnnouncer.Say("SCRAP MINE INBOUND", $"{def.displayName} — {def.blurb}", def.color);
    }

    /// <summary>
    /// Open floor: reachable, clear of every cover block, and spaced away from
    /// the other drops.
    ///
    /// The NavMesh sample alone isn't enough to keep crates off the blocks.
    /// SamplePosition snaps a candidate that landed inside a block's carved hole
    /// out to the nearest mesh edge — which is flush against the block — and
    /// blocks slide and regrow anyway, so their carving isn't where it was when
    /// the mesh was last queried. The footprint test is the authority.
    /// </summary>
    bool TryPickLandingSpot(out Vector3 spot)
    {
        // Blocks are created once by ArenaBuilder; find them lazily because the
        // spawner's Awake can run before they're all in the scene.
        if (_blocks == null || _blocks.Length == 0)
            _blocks = FindObjectsByType<ArenaBlock>(FindObjectsSortMode.None);

        // The active arena's bounds, not this component's: an arena swap has to
        // move the drop zone with it.
        Vector2 extent = ArenaContext.HalfExtent;
        float[] planes = ArenaContext.DropPlanes;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            // Multi-level arenas list every floor. Sampling only from y=0 would
            // leave the upper storeys without loot, and the vertical space dead.
            float plane = planes[Random.Range(0, planes.Length)];
            var candidate = new Vector3(
                Random.Range(-extent.x, extent.x), plane,
                Random.Range(-extent.y, extent.y));

            // Tight radius: a wide snap would drag the point back onto a block.
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                continue;

            if (IsOnCover(hit.position) || IsCrowded(hit.position))
                continue;

            spot = hit.position;
            return true;
        }

        spot = Vector3.zero;
        return false;
    }

    bool IsOnCover(Vector3 point)
    {
        foreach (var block in _blocks)
            if (block != null && block.CoversPoint(point, blockClearance))
                return true;
        return false;
    }

    bool IsCrowded(Vector3 point)
    {
        foreach (var other in TreasureDrop.Active)
            if (other != null && Vector3.Distance(other.GroundPoint, point) < dropSpacing)
                return true;
        return false;
    }
}

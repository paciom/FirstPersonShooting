using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Robot Factory's work order: unit keys queue up, the head of the queue
/// builds at whatever rate the power grid affords, and finished robots walk
/// out the door toward the war.
///
/// Rides on the factory's building root (added by Building.Construct), so a
/// factory collapsing mid-build takes its half-made robot with it — the
/// credits were spent when the order was placed, which is exactly the sting
/// losing a factory is supposed to have.
///
/// Queue state is strings and floats on purpose: both survive the
/// recompile-during-Play reload, so a dev tweaking balance mid-match doesn't
/// void the queue.
/// </summary>
public class ProductionQueue : MonoBehaviour
{
    public const int MaxQueue = 5;

    [SerializeField] List<string> _queue = new List<string>();
    [SerializeField] float _workDone;

    Building _building;
    static int _serial;

    public int QueueLength => _queue.Count;

    /// <summary>Unit key currently on the assembly line, or null when idle.</summary>
    public string HeadKey => _queue.Count > 0 ? _queue[0] : null;

    /// <summary>0..1 on the unit currently building; 0 with an empty queue.</summary>
    public float HeadProgress
    {
        get
        {
            if (_queue.Count == 0)
                return 0f;
            var def = UnitCatalog.Get(_queue[0]);
            return def == null ? 0f : Mathf.Clamp01(_workDone / def.BuildSeconds);
        }
    }

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    public bool Enqueue(string unitKey)
    {
        if (_queue.Count >= MaxQueue || UnitCatalog.Get(unitKey) == null)
            return false;
        _queue.Add(unitKey);
        return true;
    }

    /// <summary>The factory with the shortest line, for the build bar to feed.</summary>
    public static ProductionQueue LeastBusy(int teamId)
    {
        ProductionQueue best = null;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != teamId || !building.IsAlive)
                continue;
            var queue = building.GetComponent<ProductionQueue>();
            if (queue == null || queue.QueueLength >= MaxQueue)
                continue;
            if (best == null || queue.QueueLength < best.QueueLength)
                best = queue;
        }
        return best;
    }

    /// <summary>Total queued across a team's factories — the build bar's readout.</summary>
    public static int TotalQueued(int teamId, string unitKey)
    {
        int total = 0;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != teamId || !building.IsAlive)
                continue;
            var queue = building.GetComponent<ProductionQueue>();
            if (queue == null)
                continue;
            foreach (var key in queue._queue)
                if (key == unitKey)
                    total++;
        }
        return total;
    }

    void Update()
    {
        if (_queue.Count == 0 || _building == null || !_building.IsAlive)
            return;

        var def = UnitCatalog.Get(_queue[0]);
        if (def == null)
        {
            _queue.RemoveAt(0);   // catalog key vanished in a rebalance
            _workDone = 0f;
            return;
        }

        // The whole point of the power grid: a browned-out factory crawls.
        _workDone += Time.deltaTime * CommanderPower.Efficiency(_building.TeamId);
        if (_workDone < def.BuildSeconds)
            return;

        _queue.RemoveAt(0);
        _workDone = 0f;
        Deliver(def);
    }

    /// <summary>
    /// Roll the finished robot out the map-centre side of the factory and
    /// send it a few strides toward the war — the default rally. Collectors
    /// rally themselves: their idle brain drives them to the nearest field.
    /// </summary>
    void Deliver(UnitDefinition def)
    {
        int team = _building.TeamId;
        Vector3 toCentre = -transform.position;
        toCentre.y = 0f;
        toCentre = toCentre.sqrMagnitude > 0.01f ? toCentre.normalized
            : (team == 0 ? Vector3.forward : Vector3.back);

        var footprint = _building.Definition != null
            ? _building.Definition.footprint : new Vector2(6f, 6f);
        Vector3 door = transform.position + toCentre * (Mathf.Max(footprint.x, footprint.y) * 0.5f + 2f);
        door.y = CommanderMap.GroundY;

        var roster = FindFirstObjectByType<RobotRoster>();
        var entry = UnitCatalog.EntryOf(roster, def.robotName);
        float yaw = Quaternion.LookRotation(toCentre, Vector3.up).eulerAngles.y;
        string name = $"CmdUnit{team}_{def.key}_{++_serial}";

        CommanderUnit unit = def.isCollector
            ? CommanderCollector.BuildCollector(name, entry.modelPrefab, team, door, yaw)
            : CommanderUnit.Build<CommanderUnit>(name, entry.modelPrefab, entry.vehiclePrefab,
                team, door, yaw, armed: true, secondaryWeapon: def.secondaryWeapon,
                transformStages: entry.transformStages,
                paintAnchorHue: entry.paintAnchorHue,
                jetStages: entry.jetStages);
        def.ApplyTo(unit);

        VfxUtil.EnergyBurst(door + Vector3.up * 1f, MatchAnnouncer.TeamColor(team), 0.8f);
        CommanderOps.Log(team, $"{def.displayName} deployed");

        if (!def.isCollector)
            unit.IssueMove(door + toCentre * 6f);
    }
}

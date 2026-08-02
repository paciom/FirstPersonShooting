using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A harvestable crystal field: a capacity in credits and the shard visuals
/// that shrink away as it is mined out. Sits on a per-field object whose
/// children are the shards CommanderMap built; collectors find fields through
/// the static registry and drain them through <see cref="Harvest"/>.
///
/// Depletion is visible on purpose — a field two-thirds gone has a third of
/// its shards left, so the state of the map's economy can be read straight
/// off the battlefield from any zoom, no UI required.
/// </summary>
public class CrystalField : MonoBehaviour
{
    /// <summary>Collectors park within this range of the field centre to mine.</summary>
    public const float HarvestRadius = 7f;

    /// <summary>
    /// Live fields. OnEnable/OnDisable-maintained with the same self-heal
    /// rationale as CommanderUnit.All: statics do not survive a mid-play
    /// recompile.
    /// </summary>
    public static readonly List<CrystalField> All = new List<CrystalField>();

    public float capacity = 3000f;

    float _remaining;
    List<Transform> _shards;
    int _visibleShards;

    /// <summary>Called by the map builder right after the shards are created.</summary>
    public void Init(List<Transform> shards, float fieldCapacity)
    {
        _shards = shards;
        capacity = fieldCapacity;
        _remaining = fieldCapacity;
        _visibleShards = shards.Count;
    }

    public float Remaining => _remaining;
    public bool IsExhausted => _remaining <= 0f;

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// Take up to <paramref name="amount"/> credits' worth of crystal.
    /// Returns what was actually there to take.
    /// </summary>
    public float Harvest(float amount)
    {
        if (_remaining <= 0f || amount <= 0f)
            return 0f;

        float taken = Mathf.Min(amount, _remaining);
        _remaining -= taken;
        UpdateShards();
        return taken;
    }

    /// <summary>
    /// The nearest field with crystal left in it, or null if the map is mined
    /// dry. Squared-distance scan over a list of eight — no spatial structure
    /// needed at this scale.
    /// </summary>
    public static CrystalField Nearest(Vector3 from)
    {
        // Registry self-heal: a recompile during Play wipes the static list
        // and OnEnable never re-runs. Exhausted fields stay registered until
        // destroyed, so "empty with fields in the scene" only means a reload.
        if (All.Count == 0)
            All.AddRange(FindObjectsByType<CrystalField>(FindObjectsSortMode.None));

        CrystalField best = null;
        float bestSqr = float.MaxValue;
        foreach (var field in All)
        {
            if (field == null || field.IsExhausted)
                continue;
            float sqr = (field.transform.position - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = field;
            }
        }
        return best;
    }

    void UpdateShards()
    {
        if (_shards == null || _shards.Count == 0)
            return;

        // Ceil, not round: the last shard survives until the field is truly
        // empty, so an active field never looks exhausted.
        int shouldShow = _remaining <= 0f
            ? 0
            : Mathf.CeilToInt(_shards.Count * (_remaining / capacity));

        for (int i = _visibleShards - 1; i >= shouldShow; i--)
            if (_shards[i] != null)
                _shards[i].gameObject.SetActive(false);
        _visibleShards = Mathf.Min(_visibleShards, shouldShow);
    }
}

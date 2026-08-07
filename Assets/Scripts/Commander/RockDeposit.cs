using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A boulder worth a prospector's time. Robots dig at it between wars;
/// enough accumulated robot-seconds and it bursts — sometimes onto a fresh
/// crystal field underneath, sometimes onto nothing, because a prospect you
/// could read from the surface wouldn't be a prospect. Whether crystal
/// waits underneath was rolled with the map, mirrored on both twins, so
/// neither side's luck is better than the other's.
/// </summary>
public class RockDeposit : MonoBehaviour
{
    /// <summary>Live prospects. Same self-healing registry as CrystalField.</summary>
    public static readonly List<RockDeposit> All = new List<RockDeposit>();

    /// <summary>How close a robot must stand to swing at it.</summary>
    public const float DigRadius = 3.2f;

    [SerializeField] bool _hasCrystal;
    [SerializeField] float _digRequired = 12f;
    [SerializeField] float _dug;

    public void Init(bool hasCrystal, float digRequired)
    {
        _hasCrystal = hasCrystal;
        _digRequired = digRequired;
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    public static RockDeposit Nearest(Vector3 from)
    {
        // Registry self-heal after a mid-play recompile (OnEnable won't re-run).
        if (All.Count == 0)
            All.AddRange(FindObjectsByType<RockDeposit>(FindObjectsSortMode.None));

        RockDeposit best = null;
        float bestSqr = float.MaxValue;
        foreach (var rock in All)
        {
            if (rock == null)
                continue;
            float sqr = (rock.transform.position - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = rock;
            }
        }
        return best;
    }

    /// <summary>
    /// One robot's tick of pickwork. Several robots on one boulder pool
    /// their effort. Returns true when this swing cracked it open.
    /// </summary>
    public bool Dig(float seconds)
    {
        _dug += seconds;
        if (_dug < _digRequired)
            return false;
        Open();
        return true;
    }

    void Open()
    {
        var amber = new Color(1f, 0.65f, 0.2f);
        VfxUtil.Explosion(transform.position + Vector3.up * 0.8f,
            _hasCrystal ? amber : new Color(0.5f, 0.5f, 0.55f), _hasCrystal ? 1.3f : 0.8f);

        // The strike: a fresh small field where the boulder stood. The rock's
        // parent is the map root, which is exactly where fields live.
        if (_hasCrystal)
            CommanderMap.SpawnFieldAt(transform.parent, transform.position, 1100f, 7);

        Destroy(gameObject);
    }
}

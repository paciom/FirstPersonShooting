using UnityEngine;

/// <summary>
/// The raider's drive-by gun: chip fire at the player's structures while the
/// march rolls on. A sibling component, not TDCreep code, for the usual
/// reason — this needs an Update, and a CommanderUnit subclass declaring one
/// hides the base order-brain.
///
/// Two rules keep it honest. Defenders outrank towers: while the base brain
/// has a robot fight going (InCombat), this gun stays quiet rather than
/// split fire the balance never priced. And it never stops the march — the
/// weapon component happily fires down any direction the muzzle isn't
/// facing, which is what a passing shot IS; towers out-shield a passing
/// wave and regenerate between them, so drive-by fire is pressure on the
/// player's repairs, not a wrecking ball on the whole rim.
/// </summary>
public class TDRaiderGun : MonoBehaviour
{
    const float Range = 14f;
    const float RetargetSeconds = 0.6f;

    /// <summary>
    /// Seconds between shots — well under the gun's own cadence, so a
    /// structure takes a passing pot-shot, not the full anti-robot rate.
    /// </summary>
    const float ShotInterval = 0.9f;

    CommanderUnit _unit;
    Weapon _weapon;
    Building _target;
    float _nextRetarget;
    float _nextShot;

    void Update()
    {
        if (_unit == null)
            _unit = GetComponent<CommanderUnit>();
        if (_weapon == null)
            _weapon = GetComponentInChildren<Weapon>();
        if (_unit == null || _weapon == null || !_unit.IsAlive || _unit.InCombat)
            return;

        if (Time.time >= _nextRetarget)
        {
            _nextRetarget = Time.time + RetargetSeconds;
            _target = Acquire();
        }
        if (_target == null || !_target.IsAlive || Time.time < _nextShot)
            return;

        _nextShot = Time.time + ShotInterval;
        Vector3 aim = AimPoint(_target) - _weapon.muzzle.position;
        _weapon.TryFire(aim.normalized);
    }

    /// <summary>
    /// Mid-body of a rim tower: high enough that the shot clears the cliff
    /// lip from the lane, and independent of Building.Definition — which a
    /// recompile nulls for TD keys.
    /// </summary>
    static Vector3 AimPoint(Building building) =>
        building.transform.position + Vector3.up * 2.2f;

    /// <summary>
    /// Nearest live player structure in reach — except the Core, whose only
    /// wound is a raider's ARRIVAL (the leak fiction; shooting a 6000-point
    /// shield would just be noise), checked by reference because the
    /// definition lookup doesn't survive a recompile.
    /// </summary>
    Building Acquire()
    {
        Building core = TDController.Instance != null ? TDController.Instance.Core : null;
        Building best = null;
        float bestSqr = Range * Range;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId != 0 || !building.IsAlive
                || building == core)
                continue;
            float sqr = (building.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            Vector3 from = transform.position + Vector3.up * 1.1f;
            Vector3 to = AimPoint(building);
            if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform.root != building.transform.root
                && hit.transform.root != transform)
                continue;
            bestSqr = sqr;
            best = building;
        }
        return best;
    }
}

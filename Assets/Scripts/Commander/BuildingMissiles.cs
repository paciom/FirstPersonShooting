using UnityEngine;

/// <summary>
/// The Missile Battery's launcher: watches the sky and answers robots in jet
/// form with homing missiles — real Missiles Pack bodies riding GenericBolt's
/// homing, the same dress-up the arena's seeker weapons wear. Strictly
/// anti-air: ground robots are the turret's problem, and a battery with
/// nothing overhead holds its fire and its budget.
///
/// Added by Building.Construct for missile-battery definitions; rides the
/// building's shield for team identity and dies with the building.
/// </summary>
public class BuildingMissiles : MonoBehaviour
{
    const float Range = 40f;
    const float SalvoSeconds = 2.6f;

    int _teamId;
    float _nextSalvo;
    Transform _roof;

    void Start()
    {
        var shield = GetComponent<EnergyShield>();
        _teamId = shield != null ? shield.teamId : 0;

        var def = GetComponent<Building>()?.Definition;
        _roof = new GameObject("LaunchPoint").transform;
        _roof.SetParent(transform, false);
        _roof.localPosition = new Vector3(0f, (def != null ? def.height : 2.2f) + 0.25f, 0f);
    }

    void Update()
    {
        if (Time.time < _nextSalvo)
            return;

        var target = NearestAirborne();
        if (target == null)
            return;

        // The grid sets the reload, same law as the photon turrets.
        _nextSalvo = Time.time + SalvoSeconds / CommanderPower.Efficiency(_teamId);

        var spec = new BoltSpec
        {
            speed = 22f,
            damage = 34f,
            color = new Color(1f, 0.55f, 0.2f),
            size = 0.14f,
            glow = 1.6f,          // a metal body, not an energy bolt
            homingDegreesPerSecond = 230f,
            homingRange = 55f,
            lifetime = 6f,
            trailTime = 0.5f,
            trailWidth = 0.22f,
            trailGlow = 1.7f,
            splashRadius = 2.4f,
            splashScale = 1.1f,
        };
        Vector3 aim = target.transform.position + Vector3.up * 0.5f - _roof.position;
        var bolt = GenericBolt.Spawn(_roof.position, aim.normalized, spec, _teamId, transform);
        MissileModels.Dress(bolt, 2, 1.05f);
        VfxUtil.Explosion(_roof.position, new Color(1f, 0.7f, 0.35f), 0.4f);
    }

    CommanderUnit NearestAirborne()
    {
        CommanderUnit best = null;
        float bestSqr = Range * Range;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId == _teamId || !unit.IsAlive || !unit.IsAirborne)
                continue;
            float sqr = (unit.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = unit;
            }
        }
        return best;
    }
}

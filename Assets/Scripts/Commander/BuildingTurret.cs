using UnityEngine;

/// <summary>
/// The Photon Turret's brain and gun: a swiveling barrel on the roof that
/// picks the nearest visible enemy unit and fires team-coloured bolts at a
/// cadence the power grid sets — a browned-out base defends at quarter speed,
/// which is the whole reason to shoot power plants.
///
/// Added by Building.Construct for turret definitions only; rides the
/// building's own EnergyShield for team identity and dies with the building.
/// </summary>
public class BuildingTurret : MonoBehaviour
{
    const float Range = 24f;
    const float BaseShotsPerSecond = 3f;

    LaserBlaster _weapon;
    Transform _pivot;
    CommanderUnit _target;
    float _nextRetarget;
    int _teamId;

    void Start()
    {
        var shield = GetComponent<EnergyShield>();
        _teamId = shield != null ? shield.teamId : 0;
        Color tint = MatchAnnouncer.TeamColor(_teamId);

        var def = GetComponent<Building>()?.Definition;
        float roof = def != null ? def.height : 2.6f;

        // Pivot on the roof; barrel hangs off it and yaws with it.
        _pivot = new GameObject("TurretPivot").transform;
        _pivot.SetParent(transform, false);
        _pivot.localPosition = new Vector3(0f, roof + 0.25f, 0f);

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(barrel.GetComponent<Collider>());
        barrel.name = "Barrel";
        barrel.transform.SetParent(_pivot, false);
        barrel.transform.localPosition = new Vector3(0f, 0.1f, 0.5f);
        barrel.transform.localScale = new Vector3(0.22f, 0.22f, 1.3f);
        barrel.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("Cmd_GunMetal", new Color(0.10f, 0.11f, 0.13f), 0.5f, 0.6f);

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(_pivot, false);
        muzzle.localPosition = new Vector3(0f, 0.1f, 1.2f);

        // Inactive-holder trick, as everywhere: the weapon's Awake caches
        // muzzle/ownerRoot/team, so those must be set first.
        var gun = new GameObject("Gun");
        gun.SetActive(false);
        gun.transform.SetParent(_pivot, false);
        _weapon = gun.AddComponent<LaserBlaster>();
        _weapon.muzzle = muzzle;
        _weapon.ownerRoot = transform;
        _weapon.damage = 12f;
        _weapon.boltSpeed = 50f;
        _weapon.color = tint;
        gun.SetActive(true);
    }

    void Update()
    {
        if (Time.time >= _nextRetarget)
        {
            _nextRetarget = Time.time + 0.4f;
            _target = Acquire();
            // The grid sets the rate of fire. Re-read on the retarget tick so
            // a power plant dying mid-fight slows the guns within half a second.
            _weapon.shotsPerSecond = BaseShotsPerSecond * CommanderPower.Efficiency(_teamId);
        }

        if (_target == null || !_target.IsAlive)
            return;

        Vector3 aim = _target.transform.position + Vector3.up * 1.1f - _pivot.position;
        var flat = new Vector3(aim.x, 0f, aim.z);
        if (flat.sqrMagnitude > 0.01f)
            _pivot.rotation = Quaternion.RotateTowards(_pivot.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up), 360f * Time.deltaTime);

        // Fire once the barrel is roughly on — turrets telegraph by swiveling.
        if (Vector3.Angle(_pivot.forward, flat) < 20f)
        {
            _weapon.TryFire(aim.normalized);
            CommanderAmmo.AccrueFiring(_teamId, _weapon, Time.deltaTime);
        }
    }

    CommanderUnit Acquire()
    {
        CommanderUnit best = null;
        float bestSqr = Range * Range;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId == _teamId || !unit.IsAlive)
                continue;
            float sqr = (unit.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            // Same sight rule as the units — no shooting through ridges. The
            // pivot sits ABOVE our own box collider, so a downward line can
            // clip our own roof edge: a self-hit is not an obstruction.
            Vector3 from = _pivot != null ? _pivot.position : transform.position + Vector3.up * 2f;
            Vector3 to = unit.transform.position + Vector3.up * 1.1f;
            if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform.root != unit.transform.root
                && hit.transform.root != transform)
                continue;
            bestSqr = sqr;
            best = unit;
        }
        return best;
    }
}

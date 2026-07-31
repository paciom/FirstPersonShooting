using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A tower's working parts, riding on a standard Building: the swivel gun
/// (Pulse / Rail / Mortar), the stasis field, or the refinery's income
/// drip, per its TDTowerDefinition. Added by TDPlacer right after
/// Building.Construct — the same pattern BuildingTurret uses in Commander,
/// generalized to a catalog of roles.
///
/// Towers fire the arena's own weapon components — LaserBlaster, RailZapper,
/// PlasmaLobber — configured down a muzzle on a roof pivot, so tower fire is
/// made of the same bolts, rails and orbs every other mode speaks.
/// </summary>
public class TDTower : MonoBehaviour
{
    const float RetargetSeconds = 0.35f;

    /// <summary>The catalog key — the one field that must survive a recompile.</summary>
    [SerializeField] string _key;

    TDTowerDefinition _def;
    Building _building;
    Transform _pivot;
    Weapon _weapon;
    CommanderUnit _target;
    float _nextRetarget;
    float _nextTick;

    /// <summary>Re-derived from the key on demand — same trap and same cure as Building.Definition.</summary>
    TDTowerDefinition Def => _def ?? (_def = TDTowerCatalog.Get(_key));

    /// <summary>Placement entry point: bind the definition and build the working parts.</summary>
    public void Configure(TDTowerDefinition def)
    {
        _key = def.key;
        _def = def;

        switch (def.kind)
        {
            case TDTowerKind.Pulse:
            case TDTowerKind.Rail:
            case TDTowerKind.Mortar:
                BuildGunRig(def);
                break;

            case TDTowerKind.Stasis:
                // The field made visible: a ring the size of the truth.
                CommanderUnit.GlowQuad(transform, "FieldRing", "VFX/ring",
                    new Color(0.55f, 1f, 0.4f), 0.55f, def.range * 2f, 0.14f);
                break;

            case TDTowerKind.Refinery:
                CommanderUnit.GlowQuad(transform, "IncomeRing", "VFX/ring",
                    new Color(1f, 0.72f, 0.25f), 0.55f, 4.5f, 0.14f);
                break;
        }
    }

    /// <summary>
    /// Pivot on the roof, barrel and muzzle hanging off it, weapon built
    /// with the inactive-holder trick — Weapon.Awake caches muzzle, owner
    /// and team, so those must be set before it runs. Cyan bolts: the
    /// building's shield says team 0, and defense fire should look like it.
    /// </summary>
    void BuildGunRig(TDTowerDefinition def)
    {
        Color tint = MatchAnnouncer.TeamColor(0);
        float roof = def.building.height;

        _pivot = new GameObject("TowerPivot").transform;
        _pivot.SetParent(transform, false);
        _pivot.localPosition = new Vector3(0f, roof + 0.35f, 0f);

        var metal = ArenaMaterials.Lit("Cmd_GunMetal", new Color(0.10f, 0.11f, 0.13f), 0.5f, 0.6f);
        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(barrel.GetComponent<Collider>());
        barrel.name = "Barrel";
        barrel.transform.SetParent(_pivot, false);
        switch (def.kind)
        {
            case TDTowerKind.Rail:
                // Long and thin — the sniper telegraphs by silhouette.
                barrel.transform.localPosition = new Vector3(0f, 0.1f, 0.8f);
                barrel.transform.localScale = new Vector3(0.16f, 0.16f, 2f);
                break;
            case TDTowerKind.Mortar:
                // Stubby and pitched up, the lob made legible.
                barrel.transform.localPosition = new Vector3(0f, 0.25f, 0.35f);
                barrel.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                break;
            default:
                barrel.transform.localPosition = new Vector3(0f, 0.1f, 0.5f);
                barrel.transform.localScale = new Vector3(0.22f, 0.22f, 1.3f);
                break;
        }

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(_pivot, false);
        muzzle.localPosition = def.kind == TDTowerKind.Mortar
            ? new Vector3(0f, 0.6f, 0.7f)
            : new Vector3(0f, 0.1f, 1.4f);

        var gun = new GameObject("Gun");
        gun.SetActive(false);
        gun.transform.SetParent(_pivot, false);
        switch (def.kind)
        {
            case TDTowerKind.Rail:
                var rail = gun.AddComponent<RailZapper>();
                // Cadence lives in charge time; the 0.4 s cooldown is fixed.
                rail.chargeTime = Mathf.Max(0.2f, 1f / def.shotsPerSecond - 0.4f);
                rail.railRange = def.range + 4f;
                _weapon = rail;
                break;
            case TDTowerKind.Mortar:
                var lobber = gun.AddComponent<PlasmaLobber>();
                lobber.shotsPerSecond = def.shotsPerSecond;
                lobber.maxAimDistance = def.range + 4f;
                _weapon = lobber;
                break;
            default:
                var blaster = gun.AddComponent<LaserBlaster>();
                blaster.shotsPerSecond = def.shotsPerSecond;
                blaster.boltSpeed = 55f;
                _weapon = blaster;
                break;
        }
        _weapon.muzzle = muzzle;
        _weapon.ownerRoot = transform;
        _weapon.damage = def.damage;
        _weapon.color = tint;
        gun.SetActive(true);
    }

    void Update()
    {
        var def = Def;
        if (def == null)
            return;
        if (_building == null)
            _building = GetComponent<Building>();
        if (_building != null && !_building.IsAlive)
            return;   // collapsing under us — the rig dies with the building

        switch (def.kind)
        {
            case TDTowerKind.Stasis:
                TickStasis(def);
                break;
            case TDTowerKind.Refinery:
                TickIncome(def);
                break;
            default:
                TickGun(def);
                break;
        }
    }

    // ------------------------------------------------------------- roles

    void TickGun(TDTowerDefinition def)
    {
        // A recompile clears the rig caches; the children survive by name.
        if (_pivot == null)
        {
            _pivot = transform.Find("TowerPivot");
            _weapon = GetComponentInChildren<Weapon>();
            if (_pivot == null || _weapon == null)
                return;
        }

        if (Time.time >= _nextRetarget)
        {
            _nextRetarget = Time.time + RetargetSeconds;
            _target = Acquire(def);
        }
        if (_target == null || !_target.IsAlive)
            return;

        Vector3 aim = AimPoint(def, _target) - _pivot.position;
        var flat = new Vector3(aim.x, 0f, aim.z);
        if (flat.sqrMagnitude > 0.01f)
            _pivot.rotation = Quaternion.RotateTowards(_pivot.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up), 360f * Time.deltaTime);

        // Fire once the swivel is roughly on. The rail waits for a true
        // line — a sniper that fires off-axis wastes its whole cadence.
        float onTarget = def.kind == TDTowerKind.Rail ? 5f
            : def.kind == TDTowerKind.Mortar ? 30f : 20f;
        if (Vector3.Angle(_pivot.forward, flat) < onTarget)
            _weapon.TryFire(aim.normalized);
    }

    /// <summary>
    /// Lead the march: raiders move on rails (literally — the lane), so a
    /// little dead reckoning turns projectile towers from "shoots where a
    /// robot was" into "shoots robots".
    /// </summary>
    Vector3 AimPoint(TDTowerDefinition def, CommanderUnit target)
    {
        Vector3 chest = target.transform.position + Vector3.up * 1.1f;
        var agent = target.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled)
            return chest;
        float lead = def.kind == TDTowerKind.Mortar
            ? 0.7f   // the lob's typical hang time
            : def.kind == TDTowerKind.Rail
                ? 0f    // hitscan needs none
                : Vector3.Distance(chest, _pivot.position) / 55f;
        return chest + agent.velocity * lead;
    }

    CommanderUnit Acquire(TDTowerDefinition def)
    {
        CommanderUnit best = null;
        float bestSqr = def.range * def.range;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId == 0 || !unit.IsAlive)
                continue;
            float sqr = (unit.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            // Rim towers shoot down past their own cliff lip: sight is
            // checked to the raider's HEAD, from a pivot high enough that
            // only real terrain — a plateau between two lanes — blocks.
            Vector3 from = _pivot != null ? _pivot.position : transform.position + Vector3.up * 2f;
            Vector3 to = unit.transform.position + Vector3.up * 1.6f;
            if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform.root != unit.transform.root
                && hit.transform.root != transform)
                continue;
            bestSqr = sqr;
            best = unit;
        }
        return best;
    }

    /// <summary>One grip per second on everything in the field — coverage, not burst.</summary>
    void TickStasis(TDTowerDefinition def)
    {
        if (Time.time < _nextTick)
            return;
        _nextTick = Time.time + 1f;
        float rangeSqr = def.range * def.range;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId == 0 || !unit.IsAlive)
                continue;
            Vector3 flat = unit.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > rangeSqr)
                continue;
            var pace = unit.GetComponent<TDPace>();
            if (pace != null)
                pace.ApplySlow(def.slowFactor, def.slowSeconds);
        }
    }

    void TickIncome(TDTowerDefinition def)
    {
        if (Time.time < _nextTick)
            return;
        _nextTick = Time.time + def.tickSeconds;
        TDEconomy.Grant(def.incomePerTick);
    }
}

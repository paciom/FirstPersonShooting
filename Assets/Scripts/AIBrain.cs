using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Bot brain: hunts the nearest enemy (any EnergyShield on another team — the
/// player or an opposing bot), which makes Player-vs-AI and AI-vs-AI the same
/// code path. Deliberately imperfect: reaction delay and an aim-error cone keep
/// it fair for kids and fun to watch.
///
/// The bot carries only two basic guns (see <see cref="WeaponLoadout"/>) and
/// swaps between them on range, a personality bias, and a periodic reconsider.
/// Everything more exciting has to be won off the airdrops, so a bot also
/// breaks off to grab crates, keeps clear of armed Scrap Mines, and will shoot
/// one deliberately when an enemy is standing in its blast — which is where
/// most of the good moments in a match come from.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnergyShield))]
public class AIBrain : MonoBehaviour
{
    [Header("Combat")]
    public float sightRange = 50f;
    public float reactionDelay = 0.35f;   // human-like: never instant
    public float aimErrorDegrees = 4f;    // never aimbot-precise

    [Header("Weapons")]
    public Weapon[] weapons;
    [Tooltip("Index of this bot's preferred weapon — it leans toward this one.")]
    public int favoriteWeapon = 0;

    [Tooltip("Debug: when >= 0 the bot locks to this weapon index (set by WeaponDebugConsole).")]
    public int forcedWeaponIndex = -1;

    [Header("Movement")]
    public float repathInterval = 0.4f;

    [Header("Treasure")]
    [Tooltip("How far away a crate still looks worth walking to.")]
    public float treasureInterest = 28f;

    [Tooltip("Personality 0–1: how much of that range this bot will actually detour over.")]
    [Range(0f, 1f)] public float greed = 0.7f;

    [Tooltip("Close enough to grab even while trading fire — the detour costs nothing.")]
    public float grabAnywayRange = 5f;

    [Tooltip("Below this shield fraction, a Repair Pack is worth breaking off a fight for.")]
    [Range(0f, 1f)] public float woundedShieldFraction = 0.45f;

    [Tooltip("Speed multiplier while running for a crate — makes the intent read on camera.")]
    public float lootSprint = 1.3f;

    /// <summary>Stay at least this far from an armed mine, and back off if closer.</summary>
    const float MineDangerRadius = 4.2f;

    NavMeshAgent _agent;
    EnergyShield _myShield;
    StatusEffects _statusEffects;   // added on demand by weapons; cached lazily
    WeaponLoadout _loadout;
    int _loadoutVersion = -1;
    EnergyShield _target;
    TreasureDrop _treasureTarget;   // crate we're walking to
    TreasureDrop _mineToAvoid;      // armed mine too close for comfort
    TreasureDrop _mineToShoot;      // armed mine with an enemy inside its blast
    Weapon _active;
    float _nextRepath;
    float _nextTargetScan;
    float _nextTreasureScan;
    float _treasureCooldownUntil;
    float _nextWeaponReconsider;
    float _sawTargetAt = -1f;
    bool _running;
    float[] _weights;

    void Awake()
    {
        _running = true;
        _agent = GetComponent<NavMeshAgent>();
        _myShield = GetComponent<EnergyShield>();
        _loadout = GetComponent<WeaponLoadout>();
        // Created up front rather than lazily: it owns agent speed, so the
        // loot-dash multiplier has to have somewhere to live from frame one.
        _statusEffects = StatusEffects.Get(transform);

        if (weapons == null || weapons.Length == 0)
            weapons = GetComponentsInChildren<Weapon>();
        _weights = new float[weapons.Length];
        if (weapons.Length > 0)
            _active = weapons[Mathf.Clamp(favoriteWeapon, 0, weapons.Length - 1)];
        RefreshLoadout();
    }

    /// <summary>
    /// Track what the loadout currently allows. Picking up a Weapon Pod snaps
    /// the bot straight onto the new gun — a robot that grabbed a Comet Sling
    /// and then kept plinking with its starter laser would look broken.
    /// </summary>
    void RefreshLoadout()
    {
        if (_loadout == null || _loadout.Version == _loadoutVersion)
            return;
        _loadoutVersion = _loadout.Version;

        weapons = _loadout.Available;
        _weights = new float[weapons.Length];

        if (_loadout.Special != null)
            _active = _loadout.Special;
        else if (weapons.Length > 0 && System.Array.IndexOf(weapons, _active) < 0)
            _active = weapons[Mathf.Clamp(favoriteWeapon, 0, weapons.Length - 1)];
    }

    /// <summary>
    /// Debug lock: force this bot onto one slot of its usable set (-1 releases
    /// it). The console grants the weapon through the loadout first, so pick up
    /// that change before validating the index.
    /// </summary>
    public void SetForcedWeapon(int index)
    {
        RefreshLoadout();
        forcedWeaponIndex = (weapons != null && index >= 0 && index < weapons.Length) ? index : -1;
        if (forcedWeaponIndex >= 0 && weapons[forcedWeaponIndex] != null)
            _active = weapons[forcedWeaponIndex];
    }

    /// <summary>Called by DeRezEffect while de-rezzed and by the mode controller in menus.</summary>
    public void SetActive(bool active)
    {
        _running = active;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = !active;
        _sawTargetAt = -1f;
        _target = null;
        _treasureTarget = null;
        _mineToAvoid = null;
        _mineToShoot = null;
        SetSprinting(false);
    }

    void Update()
    {
        if (!_running)
            return;

        // Frozen solid: a statue in ice doesn't walk, turn, aim, or fire.
        if (_statusEffects != null && _statusEffects.IsFrozen)
            return;

        SeparateFromNeighbors();
        RefreshLoadout();

        if (Time.time >= _nextTargetScan)
        {
            _nextTargetScan = Time.time + 0.5f;
            _target = FindNearestEnemy();
        }

        if (Time.time >= _nextTreasureScan)
        {
            _nextTreasureScan = Time.time + 0.6f;
            ScanDrops();
        }

        if (!IsValidTarget(_target))
        {
            _sawTargetAt = -1f;
            // No enemy in play, but a crate on the ground is still worth walking to.
            DriveMovement(null, 0f, 0f, false);
            return;
        }

        Vector3 targetCenter = _target.transform.position + Vector3.up * 1.2f;
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        float distance = Vector3.Distance(transform.position, _target.transform.position);

        MaybeSwitchWeapon(distance);
        float engageRange = _active != null ? _active.preferredRange : 18f;

        bool hasLineOfSight = false;
        if (distance < sightRange)
        {
            Vector3 toTarget = (targetCenter - eye).normalized;
            if (Physics.Raycast(eye, toTarget, out RaycastHit hit, sightRange, ~0, QueryTriggerInteraction.Ignore))
                hasLineOfSight = hit.transform.root == _target.transform.root;
        }

        // "Engaged" means this bot can actually put shots on someone right now
        // (the same test the firing block uses). That's the line between
        // fighting and being free to go collect — see WantsTreasure.
        bool engaged = hasLineOfSight && distance <= engageRange * 1.15f;

        DriveMovement(_target, distance, engageRange, engaged);

        // A mine with an enemy standing next to it beats any shot at the enemy.
        if (TryShootMine())
            return;

        if (hasLineOfSight)
        {
            // Face the target while engaging.
            Vector3 flat = _target.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(flat), 8f * Time.deltaTime);

            if (_sawTargetAt < 0f)
                _sawTargetAt = Time.time;   // reaction timer starts on first sight

            // Fire a little past preferred range so approaches still shoot.
            if (engaged && Time.time - _sawTargetAt >= reactionDelay && _active != null)
            {
                Vector3 aim = (targetCenter - _active.muzzle.position).normalized;
                aim = Quaternion.Euler(
                    Random.Range(-aimErrorDegrees, aimErrorDegrees),
                    Random.Range(-aimErrorDegrees, aimErrorDegrees), 0f) * aim;
                _active.TryFire(aim);
            }
        }
        else
        {
            _sawTargetAt = -1f;
        }
    }

    // ------------------------------------------------------------------ drops

    /// <summary>
    /// Fight, or go shopping? This is the decision that shapes how a robot
    /// plays the drop game, so it's spelled out rather than buried in scoring.
    ///
    /// The baseline rule is the intuitive one: <b>a robot that's actually
    /// trading fire keeps trading fire; one that isn't goes and collects.</b>
    /// Two exceptions earn their keep:
    ///
    ///  * Something practically underfoot is free — half a second's detour
    ///    mid-firefight to pick up a better gun is obviously right.
    ///  * A hurt robot breaks off for a Repair Pack. Losing a fight it was
    ///    going to lose anyway to go and heal is the smart play, and it reads
    ///    as a robot deciding something.
    /// </summary>
    bool WantsTreasure(bool engaged)
    {
        if (_treasureTarget == null)
            return false;

        float distance = Vector3.Distance(transform.position, _treasureTarget.GroundPoint);
        if (distance <= grabAnywayRange)
            return true;

        bool wounded = _myShield != null && _myShield.Normalized < woundedShieldFraction;
        if (wounded && _treasureTarget.Def != null && _treasureTarget.Def.kind == TreasureKind.RepairPack)
            return true;

        return !engaged;
    }

    /// <summary>
    /// Where the agent walks this tick, in priority order: get clear of a live
    /// mine, then whatever <see cref="WantsTreasure"/> decided, then chase the
    /// enemy. Note the bot keeps firing at anything it can see the whole time —
    /// running for a crate doesn't holster the gun.
    /// </summary>
    void DriveMovement(EnergyShield target, float distance, float engageRange, bool engaged)
    {
        if (Time.time < _nextRepath || _agent == null || !_agent.isOnNavMesh)
            return;
        _nextRepath = Time.time + repathInterval;

        if (_mineToAvoid != null)
        {
            Vector3 away = transform.position - _mineToAvoid.GroundPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = transform.forward;
            _agent.isStopped = false;
            _agent.SetDestination(transform.position + away.normalized * 6f);
            SetSprinting(false);
            return;
        }

        if (WantsTreasure(engaged))
        {
            _agent.isStopped = false;
            _agent.SetDestination(_treasureTarget.GroundPoint);
            SetSprinting(true);
            return;
        }

        SetSprinting(false);

        if (target == null)
        {
            _agent.isStopped = true;
            return;
        }

        // Chase: close to the active weapon's preferred range, then hold.
        bool close = distance <= engageRange && engaged;
        _agent.isStopped = close;
        if (!close)
            _agent.SetDestination(target.transform.position);
    }

    /// <summary>Break into a run (or stop running). Goes through StatusEffects — see sprintMultiplier.</summary>
    void SetSprinting(bool sprinting)
    {
        if (_statusEffects != null)
            _statusEffects.sprintMultiplier = sprinting ? lootSprint : 1f;
    }

    /// <summary>
    /// Pick this bot's crate of interest and its mine situation. Mines are
    /// never a pickup target — they're something to back away from, or to shoot
    /// when someone else is standing too close to one.
    /// </summary>
    void ScanDrops()
    {
        _treasureTarget = null;
        _mineToAvoid = null;
        _mineToShoot = null;

        // Cautious bots only detour for what's nearly underfoot; greedy ones
        // cross the arena for a crate. Zero while the post-pickup cooldown runs,
        // so nobody turns the match into an uninterrupted shopping trip.
        bool looting = Time.time >= _treasureCooldownUntil;
        float reach = looting ? treasureInterest * Mathf.Max(0.25f, greed) : 0f;
        float weaponReach = _active != null ? _active.range : 40f;
        bool wounded = _myShield != null && _myShield.Normalized < woundedShieldFraction;
        float bestScore = float.MaxValue;

        foreach (var drop in TreasureDrop.Active)
        {
            if (drop == null || !drop.IsAvailable)
                continue;
            float distance = Vector3.Distance(transform.position, drop.GroundPoint);

            if (drop.IsHazard)
            {
                if (!drop.HasLanded)
                    continue;
                if (distance < MineDangerRadius && _mineToAvoid == null)
                    _mineToAvoid = drop;
                // Only worth shooting from outside our own blast radius.
                if (_mineToShoot == null
                    && distance > TreasureEffects.BlastRadius * 1.2f
                    && distance < weaponReach
                    && EnemyNear(drop.GroundPoint, TreasureEffects.BlastRadius * 0.9f))
                    _mineToShoot = drop;
                continue;
            }

            // A hurt robot sees medicine differently: a Repair Pack is visible
            // at full range (even mid-cooldown) and rates as if it were much
            // nearer than it is. Without this the "break off and heal" branch in
            // WantsTreasure could never fire, because any closer crate — gold,
            // whatever — would have won the nearest-wins race first.
            bool isRepair = drop.Def != null && drop.Def.kind == TreasureKind.RepairPack;
            bool urgent = wounded && isRepair;

            if (distance > (urgent ? treasureInterest : reach))
                continue;

            float score = urgent ? distance * 0.35f : distance;
            if (score >= bestScore)
                continue;
            bestScore = score;
            _treasureTarget = drop;
        }
    }

    /// <summary>
    /// Called by TreasureDrop when this bot collects something. With crates
    /// landing every few seconds a bot would otherwise walk from pickup to
    /// pickup and never commit to a fight; this buys a stretch of pure combat
    /// before it goes shopping again. Greedy bots get back to it sooner.
    /// </summary>
    public void NotifyPickedUpTreasure()
    {
        _treasureTarget = null;
        _treasureCooldownUntil = Time.time + Mathf.Lerp(9f, 3.5f, greed) * Random.Range(0.8f, 1.2f);
    }

    bool EnemyNear(Vector3 point, float radius)
    {
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == _myShield.teamId || shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            if (Vector3.Distance(shield.transform.position, point) <= radius)
                return true;
        }
        return false;
    }

    /// <summary>Take the shot at a mine if we have one lined up. True when this tick's fire was spent on it.</summary>
    bool TryShootMine()
    {
        if (_mineToShoot == null || !_mineToShoot.IsAvailable || _active == null)
        {
            _mineToShoot = null;
            return false;
        }

        Vector3 eye = transform.position + Vector3.up * 1.4f;
        Vector3 toMine = _mineToShoot.AimPoint - eye;
        float range = toMine.magnitude;
        if (!Physics.Raycast(eye, toMine / range, out RaycastHit hit, range + 0.5f, ~0,
                QueryTriggerInteraction.Ignore)
            || hit.collider.GetComponentInParent<TreasureDrop>() != _mineToShoot)
            return false;   // something's in the way; go back to shooting robots

        Vector3 flat = _mineToShoot.GroundPoint - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(flat), 10f * Time.deltaTime);

        _active.TryFire((_mineToShoot.AimPoint - _active.muzzle.position).normalized);
        return true;
    }

    /// <summary>
    /// Soft separation: NavMesh avoidance only steers pathfinding — pull
    /// weapons (Magnet Ram, Black Hole, Tornado) drag agents directly and can
    /// stack robots inside each other. This gently pushes overlapping
    /// characters apart every frame so clumps resolve into a ring.
    /// </summary>
    void SeparateFromNeighbors()
    {
        const float MinSeparation = 1.0f;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        foreach (var other in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (other.transform.root == transform || !other.gameObject.activeInHierarchy)
                continue;
            Vector3 delta = transform.position - other.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance >= MinSeparation)
                continue;
            if (distance < 0.01f)
            {
                // Perfectly stacked: pick a random flat direction to escape.
                var r = Random.insideUnitCircle.normalized;
                delta = new Vector3(r.x, 0f, r.y);
                distance = 0.01f;
            }
            _agent.Move(delta / distance * ((MinSeparation - distance) * 4f * Time.deltaTime));
        }
    }

    /// <summary>
    /// Frequently re-pick the active weapon by weighted-random choice. Weapons
    /// suited to the current range weigh more, the bot's favorite gets a nudge,
    /// and a weight floor keeps every weapon in rotation — then we prefer a
    /// weapon *different* from the current one so the fight visibly cycles
    /// through the arsenal instead of settling on one gun.
    /// </summary>
    void MaybeSwitchWeapon(float distance)
    {
        // Debug lock wins over all normal switching.
        if (forcedWeaponIndex >= 0 && weapons != null && forcedWeaponIndex < weapons.Length)
        {
            if (weapons[forcedWeaponIndex] != null)
                _active = weapons[forcedWeaponIndex];
            return;
        }

        if (weapons == null || weapons.Length <= 1)
            return;
        if (Time.time < _nextWeaponReconsider)
            return;
        _nextWeaponReconsider = Time.time + Random.Range(1.4f, 2.6f);

        // Re-create after a mid-play script recompile (non-serialized → null).
        if (_weights == null || _weights.Length != weapons.Length)
            _weights = new float[weapons.Length];

        float total = 0f;
        for (int i = 0; i < weapons.Length; i++)
        {
            var w = weapons[i];
            if (w == null) { _weights[i] = 0f; continue; }
            // Range fit in [0,1] (within ~28m of preferred still counts a bit),
            // plus a floor so off-range weapons still appear, plus a favorite nudge.
            float fit = Mathf.Clamp01(1f - Mathf.Abs(distance - w.preferredRange) / 28f);
            _weights[i] = 0.25f + fit + (i == favoriteWeapon ? 0.35f : 0f);
            // A gun won off an airdrop is the whole point of winning it — lean
            // on it hard while the timer runs.
            if (_loadout != null && w == _loadout.Special)
                _weights[i] += 2.5f;
            total += _weights[i];
        }

        _active = WeightedPick(total, avoid: _active);
        // A second roll biased against the current weapon makes changes frequent.
        if (_active == null)
            _active = weapons[0];
    }

    Weapon WeightedPick(float total, Weapon avoid)
    {
        if (total <= 0f)
            return _active;

        // First pass: if the pick equals `avoid`, roll once more to encourage change.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            float r = Random.value * total;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null) continue;
                r -= _weights[i];
                if (r <= 0f)
                {
                    if (weapons[i] != avoid || attempt == 1)
                        return weapons[i];
                    break;   // landed on current weapon; re-roll once
                }
            }
        }
        return avoid;
    }

    bool IsValidTarget(EnergyShield shield)
    {
        return shield != null
            && shield.gameObject.activeInHierarchy
            && !shield.IsDown
            && shield.teamId != _myShield.teamId;
    }

    EnergyShield FindNearestEnemy()
    {
        EnergyShield best = null;
        float bestDistance = float.MaxValue;
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (!IsValidTarget(shield))
                continue;
            float d = (shield.transform.position - transform.position).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = shield;
            }
        }
        return best;
    }
}

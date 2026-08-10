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

    [Tooltip("Never walk closer than this to whatever it is shooting at, whatever " +
             "the gun would prefer. A robot that closes to arm's length has thrown " +
             "away the only advantage its weapon had.")]
    public float minEngageDistance = 7f;

    [Header("Movement")]
    public float repathInterval = 0.4f;

    [Header("Under fire")]
    [Tooltip("Seconds of evasive movement after being hit. Each new hit restarts it.")]
    public float evadeSeconds = 1.7f;

    [Tooltip("How far to the side an evading robot breaks.")]
    public float evadeDistance = 4.5f;

    [Tooltip("Seconds before an evading robot reverses its strafe, so it weaves " +
             "instead of committing to one long slide.")]
    public float evadeFlipSeconds = 0.65f;

    [Tooltip("Speed multiplier while under fire.")]
    public float evadeSprint = 1.25f;

    [Tooltip("Below this shield fraction, being shot means breaking contact " +
             "rather than dancing in front of it.")]
    [Range(0f, 1f)] public float breakOffShieldFraction = 0.3f;

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

    [Header("Vehicle form")]
    [Tooltip("Transform to cross at least this far for a crate; stay a robot for anything nearer.")]
    public float vehicleDashRange = 18f;

    [Tooltip("Stand back up once an enemy is this close, rather than detour for loot.")]
    public float vehicleSafeRange = 22f;

    [Tooltip("Minimum seconds between transformations, so bots don't flicker between forms.")]
    public float vehicleDwell = 1.1f;

    [Tooltip("Fold up when the enemy is further away than this. Tuned against " +
             "the arena, which is only ~32 units across — set much higher and " +
             "bots converge inside it and never transform at all.")]
    public float vehicleChargeRange = 24f;

    [Tooltip("Stand back up once the enemy is closer than this. Must stay well " +
             "under vehicleChargeRange or the bot oscillates at the boundary — " +
             "the gap between the two IS the charge.")]
    public float vehicleChargeStopRange = 14f;

    [Tooltip("Whether this bot fights in vehicle form at range. Off makes a bot " +
             "that only ever drives for loot, which is how cautious personas read.")]
    public bool chargeInVehicle = true;

    [Tooltip("With no enemy to fight, fold up to travel further than this. " +
             "Most transformations in a quiet moment come from here.")]
    public float travelFoldDistance = 13f;

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
    TransformMode _vehicle;
    RobotJump _jump;
    float _nextHopThink;
    float _nextVehicleChange;
    float _nextRepath;
    float _nextTargetScan;
    float _nextTreasureScan;
    float _treasureCooldownUntil;
    float _nextWeaponReconsider;
    float _sawTargetAt = -1f;
    bool _running;
    float[] _weights;

    Transform _threat;        // whoever last shot us; may outlive _target
    Vector3 _threatPoint;     // where their shot landed, if we never saw them
    float _evadeUntil;
    float _evadeFlipAt;
    int _evadeSide = 1;

    void Awake()
    {
        _running = true;
        _agent = GetComponent<NavMeshAgent>();
        _myShield = GetComponent<EnergyShield>();
        _loadout = GetComponent<WeaponLoadout>();
        _vehicle = GetComponent<TransformMode>();
        // Created up front rather than lazily: it owns agent speed, so the
        // loot-dash multiplier has to have somewhere to live from frame one.
        _statusEffects = StatusEffects.Get(transform);

        // Every bot leaps the gaps in its own path. Ensured here as well as in
        // ArenaBuilder so it reaches bots already serialized into the scene, and
        // reinforcement clones, without waiting on a scene rebuild.
        _jump = GetComponent<RobotJump>();
        if (_jump == null)
            _jump = gameObject.AddComponent<RobotJump>();

        if (weapons == null || weapons.Length == 0)
            weapons = GetComponentsInChildren<Weapon>();
        _weights = new float[weapons.Length];
        if (weapons.Length > 0)
            _active = weapons[Mathf.Clamp(favoriteWeapon, 0, weapons.Length - 1)];
        RefreshLoadout();

        _myShield.OnDamaged += HandleDamaged;
    }

    void OnDestroy()
    {
        if (_myShield != null)
            _myShield.OnDamaged -= HandleDamaged;
    }

    /// <summary>
    /// Getting shot is the loudest thing that happens to a robot, and standing
    /// there taking it is what made these fights look unwatchable — the bot
    /// held its ground because nothing in the movement code had any idea it was
    /// being hit.
    ///
    /// So a hit does three things: it points the robot at whoever fired if it
    /// was not already fighting someone, it opens an evasion window (see
    /// <see cref="TryEvade"/>), and it re-rolls which way to break. Only the
    /// FIRST hit of a burst picks a side — re-rolling on every hit would leave
    /// a robot under sustained fire twitching on the spot instead of moving.
    /// </summary>
    void HandleDamaged(float damage, Vector3 hitPoint)
    {
        if (!_running)
            return;

        _threatPoint = hitPoint;

        // Set even when null: not every hit site names an attacker, and a stale
        // one from the last fight is a worse guess than the point on our own hull
        // the shot landed on, which at least has the right side of us.
        var attacker = _myShield.LastAttacker;
        _threat = attacker;

        if (attacker != null)
        {
            // Shot from somewhere we weren't looking: fight whoever it is. Only
            // when there's nothing else in play, so a bot can't be pulled off a
            // target it can actually see by a stray splash from across the map.
            if (!IsValidTarget(_target))
            {
                var theirs = attacker.GetComponentInChildren<EnergyShield>();
                if (IsValidTarget(theirs))
                    _target = theirs;
            }
        }

        if (Time.time >= _evadeUntil)
        {
            _evadeSide = Random.value < 0.5f ? -1 : 1;
            _evadeFlipAt = Time.time + evadeFlipSeconds;
        }
        _evadeUntil = Time.time + evadeSeconds;
    }

    /// <summary>
    /// Situational jumping — the bot half of the player's Space bar. Two
    /// situations call for it, both read off state the brain already keeps:
    /// being under fire (the evade window HandleDamaged opens), where a hop
    /// breaks the attacker's tracking mid-strafe, and actively trading fire,
    /// where an occasional leap keeps the bot from being a flat-footed
    /// target. Rolled on a short cadence rather than per frame so hops arrive
    /// in ones, not flurries; RobotJump.Hop's own cooldown and form rules cap
    /// it from below. Deliberately more likely under fire than merely engaged
    /// — jumping is a defence here, not a war dance.
    /// </summary>
    void ConsiderHop(bool tradingFire)
    {
        if (Time.time < _nextHopThink)
            return;
        _nextHopThink = Time.time + Random.Range(0.45f, 0.9f);

        bool underFire = Time.time < _evadeUntil;
        if (!underFire && !tradingFire)
            return;
        if (Random.value < (underFire ? 0.45f : 0.2f) && _jump != null)
            _jump.Hop();
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
    /// What the bot is firing right now. Read by HeldWeaponMount so the gun in
    /// its hands is the gun in the fight — the same contract PlayerBrain's
    /// ActiveWeapon() gives the first-person viewmodel.
    /// </summary>
    public Weapon ActiveWeapon() => _active;

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
        _threat = null;
        _evadeUntil = 0f;
        SetSprinting(false);

        // Menus and de-rezzes both land here. Unfold synchronously rather than
        // playing the clip: the object is usually about to be deactivated, and
        // that kills the coroutine mid-fold.
        if (!active && _vehicle != null)
            _vehicle.ForceRobotForm();
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
            // No enemy in play, but a crate on the ground is still worth walking
            // to — and a robot being shot at from somewhere it cannot see still
            // has every reason to stop standing in the open.
            DriveMovement(null, 0f, 0f, false, false);
            ConsiderHop(tradingFire: false);
            return;
        }

        Vector3 targetCenter = _target.transform.position + Vector3.up * 1.2f;
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        float distance = Vector3.Distance(transform.position, _target.transform.position);

        MaybeSwitchWeapon(distance);
        float engageRange = _active != null ? _active.preferredRange : 18f;
        float standoff = StandoffRange(engageRange);

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
        //
        // Measured against the standoff as well as the preferred range, because
        // the standoff is where the bot is going to be STANDING: a floor that
        // parked it further out than it was willing to shoot from would leave it
        // holding position and never firing.
        bool engaged = hasLineOfSight && distance <= Mathf.Max(engageRange, standoff) * 1.15f;

        DriveMovement(_target, distance, standoff, engaged, hasLineOfSight);
        ConsiderHop(tradingFire: engaged && hasLineOfSight);

        // A tank turns its TURRET, not its hull: the chassis keeps facing the
        // way it drives (the agent owns that) and the gun does the tracking,
        // which is the whole reason to be a tank holding a standoff. A robot
        // turns its whole body to face the target, as it always has — and so
        // does a tank whose model has no rigged turret.
        //
        // Tracked whether or not there is a shot: a turret that only starts
        // swinging once the enemy steps out of cover is a turret that is always
        // half a second late, and it costs nothing to keep the gun on them.
        TankTurret turret = _vehicle != null && _vehicle.IsVehicle && _vehicle.Turret.HasTurret
            ? _vehicle.Turret : null;
        if (turret != null)
            turret.AimAt(targetCenter);

        // The training range: bots chase, loot and hold their aim, but never
        // pull a trigger — at mines either. The gate sits here, in front of
        // both fire paths, rather than at each TryFire, so every future shot
        // this method grows inherits it.
        bool holdFire = GameModeController.TrainingMatch;

        // A mine with an enemy standing next to it beats any shot at the enemy.
        if (!holdFire && TryShootMine())
            return;

        if (hasLineOfSight)
        {
            if (turret == null)
            {
                Vector3 flat = _target.transform.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, Quaternion.LookRotation(flat), 8f * Time.deltaTime);
            }

            if (_sawTargetAt < 0f)
                _sawTargetAt = Time.time;   // reaction timer starts on first sight

            // Fire a little past preferred range so approaches still shoot —
            // but never before the barrel has caught up, or the tank shoots
            // sideways out of a turret that is visibly still swinging.
            if (engaged && !holdFire && Time.time - _sawTargetAt >= reactionDelay && _active != null
                && (turret == null || turret.OnTarget))
            {
                // A tank shoots where its barrel points, bearing AND elevation.
                // Firing at the target instead would be right only while the
                // gun is exactly on it, and every frame it isn't — the swing,
                // the gun still coming up, a target that stepped aside — the
                // shells would leave the muzzle sideways.
                Vector3 toTarget = targetCenter - _active.muzzle.position;
                Vector3 barrel = turret != null ? turret.BarrelDirection : Vector3.zero;
                Vector3 aim = barrel.sqrMagnitude > 0.01f ? barrel : toTarget.normalized;
                // Bots are never aimbots: the error cone rides the shot rather
                // than the turret, so the gun still visibly points at the enemy.
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
    /// mine, then get out of the way of whoever is shooting, then whatever
    /// <see cref="WantsTreasure"/> decided, then take up position on the enemy.
    /// Note the bot keeps firing at anything it can see the whole time — neither
    /// running for a crate nor dodging holsters the gun.
    ///
    /// Evasion sits above looting deliberately. A crate is worth a detour; it is
    /// not worth walking a straight line through someone's fire to reach, which
    /// is exactly what the old order did.
    /// </summary>
    void DriveMovement(EnergyShield target, float distance, float standoff, bool engaged,
                       bool sighted)
    {
        // Deliberately ahead of the repath gate: which form to be in is a
        // slower decision than where to walk, and it has its own dwell timer.
        UpdateVehicleForm(target, distance, engaged);

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

        if (TryEvade(target))
            return;

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

        // Take up position at the gun's range and hold there — never walk to the
        // enemy's feet. Closing all the way in throws away whatever reach the
        // weapon had and reads as a robot with no plan; it is also why fights
        // used to collapse into a scrum the moment anyone spotted anyone.
        //
        // With no line of sight there is nothing to hold a range against, so the
        // bot closes to the floor distance instead — walking round the cover is
        // the only way to find an angle on someone behind it.
        float wanted = sighted ? standoff : Mathf.Min(minEngageDistance, standoff);

        // A band rather than a point: matching the distance exactly leaves a bot
        // shuffling forward and back on the spot forever.
        if (distance <= wanted * 1.15f && distance >= wanted * 0.75f)
        {
            _agent.isStopped = true;
            return;
        }

        _agent.isStopped = false;
        _agent.SetDestination(StandoffPoint(target.transform.position, wanted));
    }

    /// <summary>
    /// How far this bot wants to be from what it is shooting at: the active
    /// gun's preferred range, floored by <see cref="minEngageDistance"/> so it
    /// never walks into arm's reach — and then capped by what the gun can
    /// actually hit, so the floor can never park a short-range weapon outside
    /// its own range and leave the bot standing there holding it.
    /// </summary>
    float StandoffRange(float engageRange)
    {
        float reach = _active != null ? _active.range : engageRange;
        return Mathf.Min(Mathf.Max(minEngageDistance, engageRange), Mathf.Max(2f, reach * 0.9f));
    }

    /// <summary>
    /// A point <paramref name="range"/> out from <paramref name="center"/> on the
    /// side this bot is already on, snapped to the navmesh.
    ///
    /// Approaching this instead of the enemy itself is what puts a floor under
    /// how close a bot gets, and it doubles as the back-off destination when
    /// something has shoved it too near — the same ring works in both
    /// directions, so there is only one number to reason about.
    /// </summary>
    Vector3 StandoffPoint(Vector3 center, float range)
    {
        Vector3 out_ = transform.position - center;
        out_.y = 0f;
        if (out_.sqrMagnitude < 0.01f)
            out_ = -transform.forward;

        Vector3 wanted = center + out_.normalized * range;
        if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 3f, _agent.areaMask))
            return hit.position;
        return wanted;
    }

    /// <summary>
    /// Move like something is shooting at you, for a moment after something was.
    ///
    /// Two behaviours, chosen on how much shield is left, because they read as
    /// two different decisions to anyone watching:
    ///
    ///  * Healthy — sidestep. Break across the threat's line rather than away
    ///    from it, so the robot keeps its own gun on target and the exchange
    ///    stays a fight. The side flips every <see cref="evadeFlipSeconds"/>, so
    ///    it weaves instead of sliding away in one long straight line.
    ///  * Hurt — leave. Straight back out of the line of fire, or to a Repair
    ///    Pack if one is on the field, which turns "about to lose" into a robot
    ///    visibly going to fix itself.
    ///
    /// Returns true when it has taken the tick's movement decision.
    /// </summary>
    bool TryEvade(EnergyShield target)
    {
        if (Time.time >= _evadeUntil)
            return false;

        Vector3 threat = ThreatPosition(target);
        Vector3 away = transform.position - threat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = -transform.forward;
        away.Normalize();

        bool hurt = _myShield != null && _myShield.Normalized < breakOffShieldFraction;
        Vector3 wanted;

        if (hurt && _treasureTarget != null && _treasureTarget.Def != null
            && _treasureTarget.Def.kind == TreasureKind.RepairPack)
        {
            wanted = _treasureTarget.GroundPoint;
        }
        else if (hurt)
        {
            wanted = transform.position + away * (evadeDistance * 1.6f);
        }
        else
        {
            if (Time.time >= _evadeFlipAt)
            {
                _evadeSide = -_evadeSide;
                _evadeFlipAt = Time.time + evadeFlipSeconds;
            }
            // A little backward lean on the strafe: purely sideways at close
            // range still arcs round into their lap.
            Vector3 side = Vector3.Cross(Vector3.up, away) * _evadeSide;
            wanted = transform.position + side * evadeDistance + away * (evadeDistance * 0.3f);
        }

        if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 3f, _agent.areaMask))
            wanted = hit.position;
        else
            _evadeSide = -_evadeSide;   // wall on that side; try the other way next tick

        _agent.isStopped = false;
        _agent.SetDestination(wanted);
        SetSpeedScale(evadeSprint);
        return true;
    }

    /// <summary>
    /// Where the shooting is coming from. The attacker if we know who it was —
    /// EnergyShield records that on every hit — and otherwise the point their
    /// shot landed on us, which at least gives the right side of the robot to
    /// break away from.
    /// </summary>
    Vector3 ThreatPosition(EnergyShield target)
    {
        if (_threat != null && _threat.gameObject.activeInHierarchy)
            return _threat.position;
        if (target != null)
            return target.transform.position;
        return _threatPoint;
    }

    /// <summary>
    /// Robot or vehicle? Vehicle form is a travel decision, not a combat one:
    /// it buys speed and a low profile at the price of the whole loadout bar one
    /// gun, so a bot folds up only when it has somewhere to be and nobody to
    /// shoot on the way.
    ///
    /// The two ranges overlap on purpose — fold to cross more than
    /// <see cref="vehicleDashRange"/>, stand back up once the crate is closer
    /// than half that. Matching thresholds would leave a bot transforming and
    /// untransforming on the spot at the boundary.
    /// </summary>
    void UpdateVehicleForm(EnergyShield target, float distanceToTarget, bool engaged)
    {
        if (_vehicle == null || !_vehicle.CanTransform || Time.time < _nextVehicleChange)
            return;

        bool enemyNear = IsValidTarget(target) && distanceToTarget < vehicleSafeRange;
        bool wounded = _myShield != null && _myShield.Normalized < woundedShieldFraction;

        bool want = false;
        if (!engaged && !enemyNear && !wounded && _treasureTarget != null)
        {
            float run = Vector3.Distance(transform.position, _treasureTarget.GroundPoint);
            // Already driving? Hold form until nearly on top of the crate.
            want = _vehicle.IsVehicle ? run > vehicleDashRange * 0.5f : run > vehicleDashRange;
        }

        // Fight at range as a tank, close as a robot.
        //
        // The siege kit is slow and heavy and the arsenal is fast and light, so
        // each form has a distance it is actually better at. Driving that
        // directly off range means bots transform constantly through a match —
        // fold to shell from afar, unfold as the gap closes — which is both the
        // readable tell and the reason to watch a fight.
        //
        // The thresholds are tuned against an arena only ~32 units across.
        // Anything much wider than the map means bots converge inside it and
        // never transform, which is exactly how this behaved at 34/20.
        //
        // Deliberately not while wounded: a wounded bot standing up inside your
        // range is a gift, and one that folds to escape reads as cowardice
        // rather than aggression.
        if (!want && !wounded && chargeInVehicle && IsValidTarget(target))
        {
            want = _vehicle.IsVehicle
                ? distanceToTarget > vehicleChargeStopRange
                : distanceToTarget > vehicleChargeRange;
        }

        // Nothing to shoot: drive. With no valid target the branches above are
        // all dead, which used to leave a bot walking the whole arena on foot —
        // the single biggest reason transformations were never seen.
        if (!want && !wounded && !IsValidTarget(target) && _agent != null
            && _agent.isOnNavMesh && _agent.hasPath)
        {
            float remaining = _agent.remainingDistance;
            if (!float.IsInfinity(remaining))
                want = _vehicle.IsVehicle
                    ? remaining > travelFoldDistance * 0.4f
                    : remaining > travelFoldDistance;
        }

        if (want == _vehicle.IsVehicle)
            return;
        _nextVehicleChange = Time.time + vehicleDwell;
        _vehicle.SetVehicle(want);
    }

    /// <summary>Break into a run (or stop running). Goes through StatusEffects — see sprintMultiplier.</summary>
    void SetSprinting(bool sprinting) => SetSpeedScale(sprinting ? lootSprint : 1f);

    void SetSpeedScale(float scale)
    {
        if (_statusEffects != null)
            _statusEffects.sprintMultiplier = scale;
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

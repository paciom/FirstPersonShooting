using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One RTS unit: a roster robot on a NavMeshAgent with exactly one weapon and
/// a tiny order-driven brain (Idle / Move / AttackMove / Attack).
///
/// Deliberately NOT a clone of an arena bot. An arena robot carries all 54
/// weapon components, a transform rig, treasure loadout state and a
/// chase-and-scramble AIBrain; at RTS unit counts that is thousands of
/// components doing nothing anyone ordered. This rig is built from the same
/// public pieces (RobotFactory model normalization, EnergyShield, one Weapon,
/// RobotLocomotion riding along on the model prefab) and nothing else.
///
/// Units are spawned at the SCENE ROOT, not under the Commander object:
/// LaserBolt finds its victim via hit.transform.root.GetComponent&lt;EnergyShield&gt;,
/// so a unit parented under anything else is unhittable. CommanderController
/// sweeps the <see cref="All"/> registry at teardown instead of destroying a
/// parent.
///
/// No DeRezEffect on purpose: that component always re-materializes, which is
/// right for arena characters and wrong for an army — a de-rezzed unit folds
/// into light and is gone, and rebuilding it is what factories are for.
/// </summary>
public class CommanderUnit : MonoBehaviour
{
    public enum OrderKind { Idle, Move, AttackMove, Attack }

    /// <summary>
    /// Every live unit. Maintained by OnEnable/OnDisable, self-healed from the
    /// slow tick because a script recompile during Play wipes static state —
    /// same reason AIBrain re-creates its weights.
    /// </summary>
    public static readonly List<CommanderUnit> All = new List<CommanderUnit>();

    [Header("Combat")]
    public float sightRange = 26f;
    public float attackRange = 20f;

    /// <summary>
    /// Whether this unit's own brain picks fights with enemy STRUCTURES
    /// (attack-move and idle auto-engage). Commander units say yes — sieging
    /// is how wars end. Tower Defense raiders say no: their structure fire
    /// is a drive-by side gun by design, and a raider that parks at its own
    /// attack range to duel a tower stands just outside the tower's — a
    /// stationary fight the tower can never answer. Explicit orders
    /// (IssueAttackBuilding) are not gated; this governs only what the unit
    /// decides for itself.
    /// </summary>
    protected virtual bool AttacksStructures => true;

    /// <summary>Seconds between slow-brain ticks (scans, arrivals, mining).</summary>
    protected const float ThinkInterval = 0.4f;

    /// <summary>How far an auto-engaging idle unit will drift before walking home.</summary>
    const float LeashRange = 30f;

    NavMeshAgent _agent;
    EnergyShield _shield;
    Weapon _weapon;
    Weapon[] _weapons = new Weapon[0];
    Transform _body;
    GameObject _selectRing;

    // Transformation state: robots fold into their vehicle form for long
    // drives and unfold to fight, arena fiction kept. Stages beat the
    // stand-alone vehicle prefab where a robot has them: the ranger's own
    // stop-motion sequence ends in its TANK, which is the transformation
    // the arena made canon — the separately generated hovercraft is only
    // the fallback for robots that never went through the video pipeline.
    [SerializeField] GameObject _modelPrefab;
    [SerializeField] GameObject _vehiclePrefab;
    [SerializeField] GameObject[] _stages;
    [SerializeField] GameObject[] _jetStages;
    [SerializeField] Color _tint;

    /// <summary>
    /// Dominant hue of this unit's robot, for the away-team repaint. Serialized
    /// alongside the tint it travels with, so a unit cloned as a reinforcement
    /// repaints the same way its template did. Negative falls back to the home
    /// team's own hue, which barely moves a warm-dominant robot — see TeamPaint.
    /// </summary>
    [SerializeField] float _paintAnchorHue = -1f;
    [SerializeField] bool _vehicleForm;
    [SerializeField] float _robotSpeed;
    /// <summary>The gun's own muzzle — where shots come FROM in robot form,
    /// and what the weapons return to when a fold unwinds.</summary>
    [SerializeField] Transform _gunMuzzle;
    Coroutine _morphRoutine;
    TankTurret _turret;

    // Jet travel: with an AIRBASE standing and a flight slot free, a long
    // enough journey is flown — fold to jet, climb, cruise in a straight
    // line over everything, land, unfold, fight. The flight owns the unit
    // for its duration (the ground brain skips while airborne).
    [SerializeField] bool _jetForm;
    Coroutine _jetRoutine;
    /// <summary>Journeys longer than this go by air when a slot is free.</summary>
    const float JetDistance = 55f;
    const float JetCruiseSpeed = 15f;
    const float JetAltitude = 8f;
    const float JetClimbSpeed = 7f;

    /// <summary>In the air right now — what missile batteries hunt for.</summary>
    public bool IsAirborne => _jetForm;

    bool HasJetStages => _jetStages != null && _jetStages.Length > 1;

    /// <summary>Seconds each stop-motion stage holds; below ~0.15 a still never registers.</summary>
    const float StageSeconds = 0.11f;

    /// <summary>Travel further than this and the robot folds into its vehicle.</summary>
    const float TransformDistance = 32f;

    /// <summary>Public: Tower Defense's pace-keeper composes speeds around the fold.</summary>
    public const float VehicleSpeedFactor = 1.55f;

    /// <summary>
    /// Per-team permission for idle fighters to work the mines. Asserted
    /// every frame by CommanderController for BOTH teams (self-healing the
    /// static across a reload): by playtest decree, no robot idles — the
    /// player's included. Finish your orders, pick up a shovel; any command
    /// or a spotted enemy drops it instantly.
    /// </summary>
    public static readonly bool[] IdleWorkEnabled = new bool[2];

    /// <summary>A fighter's pockets — a fraction of a collector's hold.</summary>
    const float FighterCarry = 60f;
    const float FighterMineSeconds = 10f;
    const float FighterUnloadRadius = 9f;

    [SerializeField] float _workCarrying;
    CrystalField _workField;
    RockDeposit _digRock;

    /// <summary>Currently moonlighting in the mines (and interruptible by anything).</summary>
    public bool IsWorking =>
        _order == OrderKind.Idle
        && (_workCarrying > 0f || _workField != null || _digRock != null);

    OrderKind _order = OrderKind.Idle;
    Vector3 _destination;
    Vector3 _leashOrigin;
    CommanderUnit _target;
    /// <summary>Structure under attack — the Attack order's other kind of victim.</summary>
    [SerializeField] Building _targetBuilding;
    /// <summary>Where an attack-move resumes to once its current fight is won.</summary>
    Vector3 _resumeDestination;
    bool _hasResume;
    /// <summary>
    /// True only for fights the unit picked ITSELF from Idle. The leash must
    /// not touch a player-issued attack — right-clicking an enemy across the
    /// map is an order to cross the map.
    /// </summary>
    bool _leashed;
    float _nextThink;
    float _nextRepath;
    bool _dying;

    public int TeamId => _shield != null ? _shield.teamId : -1;
    public bool IsAlive => !_dying && _shield != null && !_shield.IsDown;
    public bool IsSelected => _selectRing != null && _selectRing.activeSelf;

    /// <summary>Mid-fight right now — what the spectator camera hunts for.</summary>
    public bool InCombat => _order == OrderKind.Attack
        && (_target != null || _targetBuilding != null);

    /// <summary>
    /// Folded for travel right now. Read by TDPace, which owns agent speed
    /// in Tower Defense and must fold the vehicle bonus into its formula
    /// rather than fight Morph's own writes.
    /// </summary>
    public bool InVehicleForm => _vehicleForm;

    // ------------------------------------------------------------- factory

    /// <summary>
    /// Builds a unit at <paramref name="position"/> (which must be on the
    /// baked NavMesh). <paramref name="modelPrefab"/> may be null — a capsule
    /// stands in, the same graceful fallback the arena uses for a missing
    /// roster.
    /// </summary>
    public static CommanderUnit Build(string name, GameObject modelPrefab, int teamId,
        Vector3 position, float yaw)
    {
        return Build<CommanderUnit>(name, modelPrefab, teamId, position, yaw, armed: true);
    }

    /// <summary>
    /// The generic body of <see cref="Build"/>: same rig, any CommanderUnit
    /// subclass riding it. <paramref name="armed"/> is false for units whose
    /// job is not shooting (the Collector) — they get no gun at all rather
    /// than a gun they never fire.
    /// </summary>
    public static T Build<T>(string name, GameObject modelPrefab, int teamId,
        Vector3 position, float yaw, bool armed) where T : CommanderUnit
    {
        return Build<T>(name, modelPrefab, null, teamId, position, yaw, armed, null);
    }

    /// <summary>
    /// Full form: <paramref name="vehiclePrefab"/> (or better,
    /// <paramref name="transformStages"/>) enables the travel transformation,
    /// <paramref name="secondaryWeapon"/> ("plasma", "rail", "beam") adds a
    /// second gun to swap to mid-fight.
    /// </summary>
    public static T Build<T>(string name, GameObject modelPrefab, GameObject vehiclePrefab,
        int teamId, Vector3 position, float yaw, bool armed, string secondaryWeapon,
        GameObject[] transformStages = null, float paintAnchorHue = -1f,
        GameObject[] jetStages = null)
        where T : CommanderUnit
    {
        Color tint = MatchAnnouncer.TeamColor(teamId);

        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        // Shield FIRST: Weapon.Awake reads its teamId from ownerRoot's shield,
        // so the shield must be configured before any weapon component wakes.
        var shield = root.AddComponent<EnergyShield>();
        shield.teamId = teamId;
        shield.maxShield = 80f;
        // Slow trickle — an RTS fight should leave marks, not erase itself.
        shield.regenDelay = 5f;
        shield.regenPerSecond = 8f;
        // Awake already ran with the default max; resync Current. Same trap
        // HoloDecoy.Spawn documents — without this every unit opens at 100/80.
        shield.Rematerialize();

        // Body rig at local y=1, matching the arena characters — RobotFactory's
        // feet-on-floor math solves against exactly that offset.
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);

        if (modelPrefab != null)
        {
            RobotFactory.InstantiateNormalized(modelPrefab, body, tint);
        }
        else
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(body, false);
            capsule.transform.localScale = new Vector3(0.7f, 0.8f, 0.7f);
            capsule.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Lit($"Cmd_Unit{teamId}", tint * 0.6f);
        }

        // What the bolts actually hit. The agent itself carries no collider.
        var collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1f, 0f);
        collider.height = 2f;
        collider.radius = 0.45f;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.45f;
        agent.height = 2f;
        agent.speed = 4.2f;
        agent.acceleration = 14f;
        agent.angularSpeed = 480f;
        agent.stoppingDistance = 0.4f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        // Staggered priorities: identical priorities make a crowd of agents
        // shove symmetrically and deadlock in doorways — i.e. in chokepoints.
        agent.avoidancePriority = 40 + (int)(Mathf.Abs(position.x * 7f + position.z * 13f) % 20);
        // A factory door can open onto carved or occupied ground; a failed
        // Warp leaves the agent off-mesh and the unit a statue. Snap to the
        // nearest mesh point within a few strides instead.
        if (!agent.Warp(position)
            && NavMesh.SamplePosition(position, out NavMeshHit navHit, 6f, NavMesh.AllAreas))
            agent.Warp(navHit.position);

        Transform gunMuzzle = null;
        if (armed)
            gunMuzzle = BuildGun(root, body, tint, secondaryWeapon);

        // Selection ring: white so it reads as "yours, selected" against both
        // team colours, flat on the ground like the team rings.
        var unit = root.AddComponent<T>();
        unit._selectRing = GlowQuad(root.transform, "SelectRing", "VFX/ring",
            Color.white, 1.2f, 2.2f, 0.06f);
        unit._selectRing.SetActive(false);
        unit._modelPrefab = modelPrefab;
        unit._vehiclePrefab = vehiclePrefab;
        unit._stages = transformStages;
        unit._jetStages = jetStages;
        unit._tint = tint;
        unit._paintAnchorHue = paintAnchorHue;
        unit._gunMuzzle = gunMuzzle;

        return unit;
    }

    /// <summary>
    /// A stub blaster: one dark box and a muzzle. Built inactive so the weapon
    /// component's Awake — which caches muzzle, ownerRoot and team — runs only
    /// after those fields are assigned, then switched on. The optional second
    /// gun shares the muzzle: it is added AFTER activation, when Awake's
    /// defaults (muzzle = its own transform, owner = root) are already right.
    /// </summary>
    static Transform BuildGun(GameObject root, Transform body, Color tint, string secondaryWeapon)
    {
        var gun = new GameObject("Blaster");
        gun.SetActive(false);
        gun.transform.SetParent(body, false);
        gun.transform.localPosition = new Vector3(0.34f, 0.18f, 0.28f);

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(barrel.GetComponent<Collider>());
        barrel.transform.SetParent(gun.transform, false);
        barrel.transform.localScale = new Vector3(0.10f, 0.12f, 0.5f);
        barrel.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("Cmd_GunMetal", new Color(0.10f, 0.11f, 0.13f), 0.5f, 0.6f);

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(gun.transform, false);
        muzzle.localPosition = new Vector3(0f, 0f, 0.3f);

        var weapon = gun.AddComponent<LaserBlaster>();
        weapon.muzzle = muzzle;
        weapon.ownerRoot = root.transform;
        weapon.damage = 10f;
        weapon.shotsPerSecond = 4f;
        weapon.boltSpeed = 45f;
        // Team-coloured fire: on a battlefield read from 45 m up, whose lasers
        // are whose matters more than which gun they came from.
        weapon.color = tint;
        gun.SetActive(true);

        Weapon secondary = null;
        switch (secondaryWeapon)
        {
            case "plasma": secondary = gun.AddComponent<PlasmaLobber>(); break;
            case "rail": secondary = gun.AddComponent<RailZapper>(); break;
            case "beam": secondary = gun.AddComponent<PhotonBeam>(); break;
        }
        if (secondary != null)
        {
            secondary.muzzle = muzzle;
            secondary.color = tint;
            secondary.damage = 10f;
        }
        return muzzle;
    }

    /// <summary>
    /// Every call site passes constants, so quads share cached materials —
    /// a fresh Material per ring across hundreds of units and sessions would
    /// pile up on the heap forever (materials do not die with GameObjects).
    /// </summary>
    static readonly Dictionary<string, Material> QuadMaterials = new Dictionary<string, Material>();

    public static GameObject GlowQuad(Transform parent, string name, string texturePath,
        Color color, float intensity, float size, float y)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, y, 0f);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = Vector3.one * size;

        string key = $"{texturePath}_{color}_{intensity}";
        if (!QuadMaterials.TryGetValue(key, out var mat) || mat == null)
        {
            mat = new Material(Shader.Find("PhotonArena/Additive"));
            mat.SetTexture("_MainTex", Resources.Load<Texture2D>(texturePath));
            mat.SetColor("_Color", color);
            mat.SetFloat("_Intensity", intensity);
            QuadMaterials[key] = mat;
        }
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return quad;
    }

    // ------------------------------------------------------------- lifecycle

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _shield = GetComponent<EnergyShield>();
        _weapons = GetComponentsInChildren<Weapon>();
        _weapon = _weapons.Length > 0 ? _weapons[0] : null;
        _body = transform.Find("Body");
        _leashOrigin = transform.position;
        // Stagger thinking so a hundred units don't all scan on the same frame.
        _nextThink = Time.time + Random.value * ThinkInterval;
    }

    /// <summary>
    /// Subscription lives here, not in Awake: a recompile during Play severs
    /// every delegate and re-runs OnEnable but never Awake — an Awake-only
    /// subscription leaves post-reload kills unheard and the unit an immortal
    /// statue. The -=/+= pair keeps the first-enable path idempotent.
    /// </summary>
    void OnEnable()
    {
        // A reload can also resurrect a unit that died mid-shrink (the
        // coroutine is gone, the flag survives). Finish the job.
        if (_dying)
        {
            Destroy(gameObject);
            return;
        }
        if (!All.Contains(this))
            All.Add(this);
        if (_shield != null)
        {
            _shield.OnDeRezzed -= HandleDeRez;
            _shield.OnDeRezzed += HandleDeRez;
            _shield.OnDamaged -= HandleDamaged;
            _shield.OnDamaged += HandleDamaged;
        }
    }

    void OnDisable()
    {
        All.Remove(this);
        if (_shield != null)
        {
            _shield.OnDeRezzed -= HandleDeRez;
            _shield.OnDamaged -= HandleDamaged;
        }
    }

    /// <summary>
    /// Taking fire is intelligence sight never needed: a robot deep in a
    /// crystal field can't SEE its attacker through its own shards — the
    /// exact posture mining puts it in — but the shield knows who hit it.
    /// Idle (mining included) and attack-moving units answer force with
    /// force; an explicit Move order stays sacred, because interrupting a
    /// commanded retreat gets robots killed.
    /// </summary>
    void HandleDamaged(float amount, Vector3 hitPoint)
    {
        if (_dying || _shield == null || _shield.IsDown)
            return;
        if (_order == OrderKind.Attack && _target != null && _target.IsAlive)
            return;   // already in a fight — finish it
        if (_order != OrderKind.Idle && _order != OrderKind.AttackMove)
            return;

        CommanderUnit attacker = null;
        if (_shield.LastAttacker != null)
            attacker = _shield.LastAttacker.GetComponent<CommanderUnit>();
        if (attacker != null && (!attacker.IsAlive || attacker.TeamId == TeamId))
            attacker = null;
        OnUnderAttack(attacker);
    }

    /// <summary>
    /// Fight back — against the robot that shot, or the turret that shot
    /// (robots shoot buildings now). Only when the attacker is untraceable
    /// does an idle unit fall back toward home. The Collector overrides this
    /// to always run: it has no gun to answer with.
    /// </summary>
    protected virtual void OnUnderAttack(CommanderUnit attacker)
    {
        if (attacker != null)
        {
            // Straight to Attack, preserving an attack-move's resume point —
            // IssueAttack would erase it.
            if (_order == OrderKind.Idle)
            {
                _leashOrigin = transform.position;
                _leashed = true;
            }
            _order = OrderKind.Attack;
            _target = attacker;
            _targetBuilding = null;
            return;
        }

        // A turret, then: it bleeds like anything else with a shield.
        Building turret = _shield.LastAttacker != null
            ? _shield.LastAttacker.GetComponent<Building>() : null;
        if (turret != null && turret.IsAlive && turret.TeamId != TeamId)
        {
            if (_order == OrderKind.Idle)
            {
                _leashOrigin = transform.position;
                _leashed = true;
            }
            _order = OrderKind.Attack;
            _target = null;
            _targetBuilding = turret;
            return;
        }

        // Shot by something untraceable while idle: step out of its range.
        if (_order == OrderKind.Idle)
        {
            Vector3 home = CommanderMap.BaseSite(TeamId) - transform.position;
            home.y = 0f;
            if (home.sqrMagnitude > 1f)
                SetAgentDestination(transform.position + home.normalized * 14f);
        }
    }

    // ------------------------------------------------------------- orders

    public void SetSelected(bool selected)
    {
        if (_selectRing != null)
            _selectRing.SetActive(selected);
    }

    public virtual void IssueMove(Vector3 destination)
    {
        _order = OrderKind.Move;
        _destination = destination;
        _target = null;
        _hasResume = false;
        _leashed = false;
        SetAgentDestination(destination);
    }

    public virtual void IssueAttackMove(Vector3 destination)
    {
        _order = OrderKind.AttackMove;
        _destination = destination;
        _resumeDestination = destination;
        _hasResume = true;
        _target = null;
        _leashed = false;
        SetAgentDestination(destination);
    }

    public virtual void IssueAttack(CommanderUnit target)
    {
        if (target == null || !target.IsAlive)
            return;
        _order = OrderKind.Attack;
        _target = target;
        _targetBuilding = null;
        _hasResume = false;
        _leashed = false;
    }

    /// <summary>Right-click on an enemy structure: robots shoot buildings too.</summary>
    public virtual void IssueAttackBuilding(Building target)
    {
        if (target == null || !target.IsAlive)
            return;
        _order = OrderKind.Attack;
        _target = null;
        _targetBuilding = target;
        _hasResume = false;
        _leashed = false;
    }

    // ------------------------------------------------------------- brain

    void Update()
    {
        if (_dying)
            return;
        // Down but not dying should be impossible — unless a reload severed
        // the OnDeRezzed delegate before the kill landed. HandleDeRez is
        // idempotent, so calling it here costs nothing and heals that.
        if (_shield.IsDown)
        {
            HandleDeRez();
            return;
        }

        // Airborne: the flight coroutine IS the brain. If a mid-play
        // recompile killed that coroutine, the unit would hang in the sky
        // forever — restart the leg toward wherever it was going.
        if (_jetForm)
        {
            if (_jetRoutine == null)
                _jetRoutine = StartCoroutine(JetRoutine(_destination));
            return;
        }

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + ThinkInterval;
            Think();
        }

        if (_order == OrderKind.Attack)
            TickAttack();

        TickTurret();
    }

    /// <summary>
    /// A tank on the move keeps its gun on whatever it is going to fight, even
    /// though it will stand up to fight it — the barrel swinging round to
    /// follow an enemy is what tells a player, from across the map, that this
    /// column has seen their army.
    ///
    /// The component is added on first use rather than at build time: units are
    /// assembled by CommanderArmy at runtime and there is no prefab to put it on.
    /// </summary>
    void TickTurret()
    {
        if (!_vehicleForm)
        {
            if (_turret != null)
                _turret.enabled = false;
            // Shots come from the shoulder again the moment the fold unwinds.
            if (_gunMuzzle != null)
                foreach (var weapon in _weapons)
                    if (weapon != null)
                        weapon.muzzle = _gunMuzzle;
            return;
        }

        if (_turret == null)
            _turret = gameObject.AddComponent<TankTurret>();
        _turret.enabled = true;

        // While the rig has a real cut turret, the guns fire out of its
        // barrel — the anchor outlives model swaps, which is its whole point.
        if (_turret.HasTurret)
            foreach (var weapon in _weapons)
                if (weapon != null)
                    weapon.muzzle = _turret.Muzzle;

        if (_target != null && _target.IsAlive)
        {
            _turret.AimAt(_target.transform.position + Vector3.up * 1.1f);
        }
        else if (_targetBuilding != null && _targetBuilding.IsAlive)
        {
            var def = _targetBuilding.Definition;
            _turret.AimAt(_targetBuilding.transform.position
                + Vector3.up * (def != null ? def.height * 0.45f : 1.5f));
        }
    }

    /// <summary>The slow tick: registry self-heal, arrivals, target acquisition.</summary>
    void Think()
    {
        if (!All.Contains(this))
            All.Add(this);   // recompile during Play wiped the registry

        ConsiderForm();
        ConsiderWeaponSwap();

        switch (_order)
        {
            case OrderKind.Idle:
                ThinkIdle();
                break;

            case OrderKind.Move:
                if (Arrived())
                    _order = OrderKind.Idle;
                break;

            case OrderKind.AttackMove:
                var enemy = NearestEnemy(sightRange);
                if (enemy != null)
                {
                    _order = OrderKind.Attack;
                    _target = enemy;
                }
                else
                {
                    // No robots to fight — structures will do (for units
                    // whose doctrine says so). This is how a wave that
                    // reaches an empty base actually ends the war instead
                    // of loitering outside the Command Center.
                    var structure = AttacksStructures ? NearestEnemyBuilding(sightRange) : null;
                    if (structure != null)
                    {
                        _order = OrderKind.Attack;
                        _targetBuilding = structure;
                    }
                    else if (Arrived())
                    {
                        _order = OrderKind.Idle;
                        _hasResume = false;
                    }
                }
                break;

            case OrderKind.Attack:
                if (TargetGone())
                    FightOver();
                // An idle unit that auto-engaged does not chase across the
                // map on one glimpse — past the leash it walks home instead.
                // Only self-picked fights are leashed: a player's right-click
                // on a distant enemy is an order to cross the map.
                else if (_leashed &&
                         Vector3.Distance(transform.position, _leashOrigin) > LeashRange)
                {
                    _target = null;
                    IssueMove(_leashOrigin);
                }
                break;
        }
    }

    /// <summary>
    /// What an unoccupied unit does with its slow tick. Fighters look for
    /// trouble first — and finding none, help in the mines rather than
    /// stand posing at the rally line. The Collector overrides this with
    /// its own full-time harvest cycle. Runs only while no order is active,
    /// so a player Move (or an AI wave push) always wins.
    /// </summary>
    protected virtual void ThinkIdle()
    {
        var intruder = NearestEnemy(sightRange);
        if (intruder != null)
        {
            _leashOrigin = transform.position;
            _leashed = true;
            _order = OrderKind.Attack;
            _target = intruder;
            return;
        }
        // Enemy structures in sight get the same treatment — a turret built
        // up against your mining field is an intruder that happens to stand
        // still. (Units whose doctrine forbids it skip straight to work.)
        var structure = AttacksStructures ? NearestEnemyBuilding(sightRange) : null;
        if (structure != null)
        {
            _leashOrigin = transform.position;
            _leashed = true;
            _order = OrderKind.Attack;
            _targetBuilding = structure;
            return;
        }
        TickIdleWork();
    }

    /// <summary>
    /// The moonlight shift: nearest live field, chip a pocketful, haul it
    /// home, repeat. A pale imitation of a collector — small pockets, slow
    /// hands — but an army of imitations between waves adds up, and robots
    /// WORKING is what a battlefield of robots should look like.
    /// </summary>
    void TickIdleWork()
    {
        int team = TeamId;
        if (team < 0 || team >= IdleWorkEnabled.Length || !IdleWorkEnabled[team])
            return;

        // Full pockets (or nothing left anywhere to mine): bank it.
        if (_workCarrying >= FighterCarry
            || (_workCarrying > 0f && CrystalField.Nearest(transform.position) == null))
        {
            Vector3 depot = CommanderMap.BaseSite(team);
            Vector3 flat = depot - transform.position;
            flat.y = 0f;
            if (flat.magnitude <= FighterUnloadRadius)
            {
                CommanderEconomy.Grant(team, Mathf.RoundToInt(_workCarrying));
                _workCarrying = 0f;
            }
            else
            {
                SetAgentDestination(depot);
            }
            return;
        }

        if (_workField == null || _workField.IsExhausted)
            _workField = CrystalField.Nearest(transform.position);

        // Prospecting: when the nearest live field is a long walk (or gone
        // entirely) and a boulder is close, dig the boulder instead — some
        // of them hide fresh crystal, and a robot finds out by digging.
        var rock = RockDeposit.Nearest(transform.position);
        bool prospect = rock != null
            && (_workField == null
                || FlatTo(rock.transform.position) + 10f < FlatTo(_workField.transform.position));
        if (prospect)
        {
            _digRock = rock;
            if (FlatTo(rock.transform.position) > RockDeposit.DigRadius)
            {
                SetAgentDestination(rock.transform.position);
                return;
            }
            if (rock.Dig(ThinkInterval))
                _digRock = null;   // cracked it — next tick sees what's under
            return;
        }
        _digRock = null;

        if (_workField == null)
            return;   // map mined dry and no rocks left — stand down for real

        Vector3 fieldPos = _workField.transform.position;
        Vector3 toField = fieldPos - transform.position;
        toField.y = 0f;
        if (toField.magnitude > CrystalField.HarvestRadius)
        {
            // Park short of the centre, same as the professionals do.
            SetAgentDestination(fieldPos - toField.normalized * 4f);
            return;
        }

        _workCarrying += _workField.Harvest(FighterCarry * (ThinkInterval / FighterMineSeconds));
    }

    float FlatTo(Vector3 to)
    {
        Vector3 delta = to - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    bool TargetGone()
    {
        if (_target != null && _target.IsAlive)
            return false;
        if (_targetBuilding != null && _targetBuilding.IsAlive)
            return false;
        return true;
    }

    /// <summary>Per-frame attack behaviour: chase, face, fire — robot or building alike.</summary>
    void TickAttack()
    {
        // Resolve whichever kind of victim this order holds. Robots first —
        // a moving gun outranks a standing wall.
        Vector3 aimPoint;
        Transform victimRoot;
        float reachBonus;
        if (_target != null && _target.IsAlive)
        {
            aimPoint = _target.transform.position + Vector3.up * 1.1f;
            victimRoot = _target.transform;
            reachBonus = 0f;
        }
        else if (_targetBuilding != null && _targetBuilding.IsAlive)
        {
            var def = _targetBuilding.Definition;
            aimPoint = _targetBuilding.transform.position
                + Vector3.up * (def != null ? def.height * 0.45f : 1.5f);
            victimRoot = _targetBuilding.transform;
            // Range is measured to the wall, not the centre — a 6 m-wide
            // factory should not need robots inside it to be shootable.
            reachBonus = def != null ? Mathf.Max(def.footprint.x, def.footprint.y) * 0.5f : 2f;
        }
        else
        {
            return;   // Think() resolves what happens next
        }

        Vector3 toTarget = victimRoot.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude - reachBonus;

        // Out of range OR occluded: keep moving. Without the line-of-sight
        // half, two units 20 m apart through a ridge both stop and pour fire
        // into the rock forever — neither dies, neither moves, permanent
        // deadlock at exactly the chokepoints where armies actually meet.
        if (distance > attackRange || !CanSeePoint(victimRoot, aimPoint))
        {
            // Chase — with a repath throttle so a hundred pursuers don't all
            // recompute paths every frame.
            if (Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + 0.5f;
                SetAgentDestination(victimRoot.position);
            }
            return;
        }

        // In range: stand and shoot. A robot turns its whole body to face;
        // a tank with a real cut turret HOLDS ITS HULL and lets the turret
        // do the aiming (TickTurret is already slewing it) — a tank that
        // pirouettes in place is a robot in a costume. The fire gate asks
        // whichever part actually aims.
        if (_agent.enabled && _agent.isOnNavMesh && !_agent.isStopped)
            _agent.isStopped = true;

        bool turretAims = _vehicleForm && _turret != null && _turret.enabled && _turret.HasTurret;
        Quaternion face = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        if (!turretAims)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, face, 540f * Time.deltaTime);

        bool onTarget = turretAims
            ? _turret.OnTarget
            : Quaternion.Angle(transform.rotation, face) < 15f;
        if (_weapon != null && onTarget)
        {
            Vector3 aim = aimPoint - _weapon.muzzle.position;
            _weapon.TryFire(aim.normalized);
            // Ammunition is cheap, never free — trigger time goes on the books.
            CommanderAmmo.AccrueFiring(TeamId, _weapon, Time.deltaTime);
        }
    }

    // ------------------------------------------------------------- forms & guns

    /// <summary>
    /// The travel transformation: a long drive is done in vehicle form —
    /// faster, and the fold/unfold on each end is the transformation show
    /// the arena modes made this fiction's signature. Combat is always
    /// fought unfolded; a robot that closes to fighting range stands up.
    /// </summary>
    bool HasStages => _stages != null && _stages.Length > 1;

    void ConsiderForm()
    {
        if (_modelPrefab == null || _body == null)
            return;
        if (_jetForm)
            return;   // the flight owns its own exit

        Vector3 goal = transform.position;
        if (_order == OrderKind.Move || _order == OrderKind.AttackMove)
            goal = _destination;
        else if (_order == OrderKind.Attack && _target != null)
            goal = _target.transform.position;
        Vector3 flat = goal - transform.position;
        flat.y = 0f;

        // Air first: a truly long journey goes by jet — if this robot HAS a
        // jet form, and an airbase has a flight slot to sustain it.
        if (flat.magnitude > JetDistance && HasJetStages && _order != OrderKind.Idle
            && CommanderAir.CanLaunch(TeamId))
        {
            if (_morphRoutine != null)
            {
                StopCoroutine(_morphRoutine);
                _morphRoutine = null;
            }
            _jetRoutine = StartCoroutine(JetRoutine(goal));
            return;
        }

        if (_vehiclePrefab == null && !HasStages)
            return;
        bool wantVehicle = flat.magnitude > TransformDistance;
        if (wantVehicle != _vehicleForm)
            Morph(wantVehicle);
    }

    /// <summary>
    /// The air leg, end to end: fold through the jet stages, climb, cruise a
    /// straight line over ridges and armies alike, descend on the goal, land
    /// on the mesh, unfold — and hand back to whatever order was standing.
    /// The flight replaces the ground brain for its whole duration.
    /// </summary>
    System.Collections.IEnumerator JetRoutine(Vector3 goal)
    {
        _jetForm = true;

        // A tank folds up to fly: unwind the vehicle bookkeeping first.
        if (_vehicleForm)
        {
            _vehicleForm = false;
            if (_robotSpeed > 0f)
                _agent.speed = _robotSpeed;
        }

        // Robot → jet, stop motion, same recipe as the tank fold.
        for (int i = 0; i < _jetStages.Length; i++)
        {
            var current = _body.Find("Model");
            if (current != null)
                Destroy(current.gameObject);
            if (_jetStages[i] != null)
                GroundAlignedInstance(_jetStages[i], 2.4f);
            VfxUtil.Explosion(transform.position + Vector3.up * 0.9f, _tint, 0.35f);
            yield return new WaitForSeconds(StageSeconds);
        }

        // Wheels up: the agent lets go of the ground.
        if (_agent.enabled)
        {
            if (_agent.isOnNavMesh)
                _agent.isStopped = true;
            _agent.enabled = false;
        }

        Vector3 destination = new Vector3(goal.x, CommanderMap.GroundY, goal.z);
        while (transform.position.y < JetAltitude)
        {
            transform.position += Vector3.up * (JetClimbSpeed * Time.deltaTime);
            FaceFlat(destination, 240f);
            yield return null;
        }

        while (true)
        {
            Vector3 flat = destination - transform.position;
            flat.y = 0f;
            if (flat.magnitude < 5f)
                break;
            FaceFlat(destination, 240f);
            transform.position += flat.normalized * (JetCruiseSpeed * Time.deltaTime);
            yield return null;
        }

        while (transform.position.y > CommanderMap.GroundY + 0.05f)
        {
            transform.position += Vector3.down * (JetClimbSpeed * Time.deltaTime);
            yield return null;
        }

        // Touchdown: back onto the mesh, wherever the mesh actually is.
        var ground = new Vector3(transform.position.x, CommanderMap.GroundY, transform.position.z);
        if (NavMesh.SamplePosition(ground, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
            ground = navHit.position;
        transform.position = ground;
        _agent.enabled = true;
        _agent.Warp(ground);

        // Jet → robot, stages in reverse, landing on the real animated rig.
        for (int i = _jetStages.Length - 1; i >= 0; i--)
        {
            var current = _body.Find("Model");
            if (current != null)
                Destroy(current.gameObject);
            if (_jetStages[i] != null)
                GroundAlignedInstance(_jetStages[i], 2.4f);
            VfxUtil.Explosion(transform.position + Vector3.up * 0.9f, _tint, 0.35f);
            yield return new WaitForSeconds(StageSeconds);
        }
        var lastStage = _body.Find("Model");
        if (lastStage != null)
            Destroy(lastStage.gameObject);
        RobotFactory.InstantiateNormalized(_modelPrefab, _body, _tint, _paintAnchorHue);

        _jetForm = false;
        _jetRoutine = null;
        // Whatever order was standing (or arrived mid-flight) resumes with
        // the next think tick; a Move that flew its whole distance simply
        // finds itself Arrived.
    }

    void FaceFlat(Vector3 worldPoint, float degreesPerSecond)
    {
        Vector3 flat = worldPoint - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f)
            return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(flat.normalized, Vector3.up),
            degreesPerSecond * Time.deltaTime);
    }

    void Morph(bool toVehicle)
    {
        _vehicleForm = toVehicle;

        // Speed switches at the decision, not the animation's end — the
        // fold happens on the move, which is also what sells the pops.
        if (toVehicle)
        {
            _robotSpeed = _agent.speed;
            _agent.speed = _robotSpeed * VehicleSpeedFactor;
        }
        else if (_robotSpeed > 0f)
        {
            _agent.speed = _robotSpeed;
        }

        if (_morphRoutine != null)
            StopCoroutine(_morphRoutine);
        _morphRoutine = StartCoroutine(MorphRoutine(toVehicle));
    }

    /// <summary>
    /// The transformation, as stop motion: one whole mesh swapped for the
    /// next through the stage sequence, robot at one end and the TANK at the
    /// other. Nothing interpolates — consecutive stages share no topology —
    /// so each swap pops; a burst covers every pop and the unit keeps
    /// driving straight through, which is the same recipe the select-screen
    /// cards use. Robots without stages get a single flash-swap to their
    /// legacy vehicle prefab.
    /// </summary>
    IEnumerator MorphRoutine(bool toVehicle)
    {
        var old = _body.Find("Model");
        if (old != null)
            Destroy(old.gameObject);

        if (HasStages)
        {
            // Walk the sequence toward the target form, ends included; each
            // step is a fresh normalized instance of that stage's mesh.
            int from = toVehicle ? 0 : _stages.Length - 1;
            int step = toVehicle ? 1 : -1;
            for (int i = from; toVehicle ? i < _stages.Length : i >= 0; i += step)
            {
                var current = _body.Find("Model");
                if (current != null)
                    Destroy(current.gameObject);
                if (_stages[i] != null)
                    GroundAlignedInstance(_stages[i], 2.0f, VehicleSkin_StageYaw);
                VfxUtil.Explosion(transform.position + Vector3.up * 0.9f, _tint, 0.35f);
                yield return new WaitForSeconds(StageSeconds);
            }

            // Unfolding ends on the REAL robot — animated rig, not a still.
            if (!toVehicle)
            {
                var last = _body.Find("Model");
                if (last != null)
                    Destroy(last.gameObject);
                RobotFactory.InstantiateNormalized(_modelPrefab, _body, _tint);
            }
        }
        else
        {
            if (toVehicle)
                GroundAlignedInstance(_vehiclePrefab, 2.2f);
            else
                RobotFactory.InstantiateNormalized(_modelPrefab, _body, _tint);
            VfxUtil.Explosion(transform.position + Vector3.up * 0.9f, _tint, 0.55f);
        }
        _morphRoutine = null;
    }

    /// <summary>Stage stills are authored facing -Z — VehicleSkin.stageYawOffset's twin.</summary>
    const float VehicleSkin_StageYaw = 180f;

    /// <summary>
    /// Stage and vehicle models carry no RobotLocomotion, so the factory's
    /// normalizer would centre them mid-air; ground them by bounds instead,
    /// nose along +Z with the unit's facing.
    ///
    /// "Nose along +Z" takes enforcing: generated models don't agree on
    /// which way is forward, so this applies VehicleSkin.FitToRobot's rule
    /// verbatim — a ground vehicle is longer than it is wide, so the long
    /// horizontal axis is turned to run down +Z (plus the stage stills'
    /// authored 180). Without it, an X-long tank drives the whole map
    /// SIDEWAYS. Rotation lands before anything is measured; fitting first
    /// would solve centring and grounding for the wrong orientation.
    /// </summary>
    void GroundAlignedInstance(GameObject prefab, float targetSize, float extraYaw = 0f)
    {
        if (prefab == null)
            return;
        var instance = Instantiate(prefab, _body);
        instance.name = "Model";
        // Zeroed BEFORE the first measure, exactly as VehicleSkin holds
        // these same assets: the long-axis test must read the mesh in a
        // known pose, not through whatever rotation the prefab root
        // happened to ship with.
        instance.transform.localRotation = Quaternion.identity;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        // Turned to face the WORLD for the long-axis test, because renderer
        // bounds are a world box and this unit is rebuilt mid-drive, on
        // whatever heading it happens to be on. Measured through the unit's own
        // facing, a tank driving north-east reads as neither X-long nor Z-long
        // in particular, and half the army ends up crabbing sideways.
        instance.transform.rotation = Quaternion.identity;
        var bounds = MeasureBounds(renderers);
        float yaw = (bounds.size.x > bounds.size.z ? 90f : 0f) + extraYaw;
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        bounds = MeasureBounds(renderers);

        float scale = targetSize / Mathf.Max(0.01f,
            Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
        instance.transform.localScale *= scale;
        Vector3 centre = _body.InverseTransformPoint(bounds.center);
        Vector3 bottom = _body.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        instance.transform.localPosition = new Vector3(
            -centre.x * scale,
            -bottom.y * scale - _body.localPosition.y,
            -centre.z * scale);
        TeamPaint.Apply(renderers, _tint, TeamPaint.DefaultSize, false, _paintAnchorHue);
    }

    static Bounds MeasureBounds(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    /// <summary>
    /// Arena robots famously will not stick to one gun; these carry two and
    /// swap on a whim mid-fight — variety on camera, same damage numbers.
    /// </summary>
    void ConsiderWeaponSwap()
    {
        if (_weapons.Length < 2 || _order != OrderKind.Attack)
            return;
        if (Random.value > 0.22f)
            return;
        // Weighted toward the laser: the exotics are seasoning, not the meal.
        _weapon = Random.value < 0.62f ? _weapons[0] : _weapons[Random.Range(1, _weapons.Length)];
    }

    void FightOver()
    {
        _target = null;
        _targetBuilding = null;
        if (_hasResume)
            IssueAttackMove(_resumeDestination);
        else
            _order = OrderKind.Idle;
    }

    /// <summary>
    /// Nearest enemy structure the unit can draw a sightline to — turrets
    /// first, always: the thing shooting back dies before the thing that
    /// merely pays for it.
    /// </summary>
    Building NearestEnemyBuilding(float within)
    {
        Building best = null, bestTurret = null;
        float bestSqr = within * within, bestTurretSqr = within * within;
        foreach (var building in Building.All)
        {
            if (building == null || building.TeamId == TeamId || !building.IsAlive)
                continue;
            var def = building.Definition;
            float reach = def != null ? Mathf.Max(def.footprint.x, def.footprint.y) * 0.5f : 2f;
            Vector3 flat = building.transform.position - transform.position;
            flat.y = 0f;
            float effective = Mathf.Max(0f, flat.magnitude - reach);
            float sqr = effective * effective;
            Vector3 aim = building.transform.position
                + Vector3.up * (def != null ? def.height * 0.45f : 1.5f);
            // "Turret" priority tier = anything that shoots back: photon
            // turrets and missile batteries die before the economy does.
            bool turret = def != null && (def.key == BuildingCatalog.Turret
                || def.key == BuildingCatalog.Missiles);
            if (turret && sqr < bestTurretSqr && CanSeePoint(building.transform, aim))
            {
                bestTurretSqr = sqr;
                bestTurret = building;
            }
            else if (!turret && sqr < bestSqr && CanSeePoint(building.transform, aim))
            {
                bestSqr = sqr;
                best = building;
            }
        }
        return bestTurret != null ? bestTurret : best;
    }

    CommanderUnit NearestEnemy(float within)
    {
        CommanderUnit best = null;
        float bestSqr = within * within;
        foreach (var unit in All)
        {
            if (unit == null || unit == this || unit.TeamId == TeamId || !unit.IsAlive)
                continue;
            float sqr = (unit.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr && CanSee(unit))
            {
                bestSqr = sqr;
                best = unit;
            }
        }
        return best;
    }

    /// <summary>
    /// Chest-height line of sight. The linecast starts inside this unit's own
    /// capsule, which Unity raycasts ignore, so no self-hit case exists; a
    /// teammate in the way counts as blocked, which the chase logic turns
    /// into sidestepping rather than firing through them.
    /// </summary>
    bool CanSee(CommanderUnit target)
    {
        return CanSeePoint(target.transform,
            target.transform.position + Vector3.up * 1.1f);
    }

    /// <summary>Same sightline test against any victim — a building's wall counts as seeing it.</summary>
    bool CanSeePoint(Transform victimRoot, Vector3 aimPoint)
    {
        Vector3 from = transform.position + Vector3.up * 1.1f;
        return !Physics.Linecast(from, aimPoint, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
            || hit.transform.root == victimRoot.root;
    }

    protected bool Arrived()
    {
        if (!_agent.enabled || !_agent.isOnNavMesh)
            return true;
        return !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.25f;
    }

    protected void SetAgentDestination(Vector3 destination)
    {
        if (!_agent.enabled || !_agent.isOnNavMesh)
            return;
        _agent.isStopped = false;
        _agent.SetDestination(destination);
    }

    // ------------------------------------------------------------- death

    void HandleDeRez()
    {
        if (!_dying)
            StartCoroutine(DeathRoutine());
    }

    /// <summary>
    /// The one-way version of the de-rez: fold into light and stay gone.
    /// Same fiction, no re-materialize.
    /// </summary>
    IEnumerator DeathRoutine()
    {
        _dying = true;
        All.Remove(this);
        SetSelected(false);
        // A death mid-transformation stops the transformation; the shrink
        // takes whatever form was showing. A death mid-FLIGHT stops the
        // flight — the shrink happens in the sky, which a missile earned.
        if (_morphRoutine != null)
        {
            StopCoroutine(_morphRoutine);
            _morphRoutine = null;
        }
        if (_jetRoutine != null)
        {
            StopCoroutine(_jetRoutine);
            _jetRoutine = null;
        }
        // Losing a collector is a strategic event worth the feed; losing a
        // soldier is a statistic the army count already tells.
        if (this is CommanderCollector)
            CommanderOps.Log(TeamId, "COLLECTOR LOST");

        var collider = GetComponent<CapsuleCollider>();
        if (collider != null)
            collider.enabled = false;
        if (_agent.enabled && _agent.isOnNavMesh)
            _agent.isStopped = true;
        _agent.enabled = false;

        VfxUtil.Explosion(transform.position + Vector3.up * 1f,
            MatchAnnouncer.TeamColor(TeamId), 1.1f);

        Vector3 startScale = _body != null ? _body.localScale : Vector3.one;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            if (_body != null)
                _body.localScale = startScale * (1f - t);
            yield return null;
        }
        Destroy(gameObject);
    }
}

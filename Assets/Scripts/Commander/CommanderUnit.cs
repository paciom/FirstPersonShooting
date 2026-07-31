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
    Coroutine _morphRoutine;

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

    /// <summary>Currently moonlighting in the mines (and interruptible by anything).</summary>
    public bool IsWorking =>
        _order == OrderKind.Idle && (_workCarrying > 0f || _workField != null);

    OrderKind _order = OrderKind.Idle;
    Vector3 _destination;
    Vector3 _leashOrigin;
    CommanderUnit _target;
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
    public bool InCombat => _order == OrderKind.Attack && _target != null;

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
        GameObject[] transformStages = null, float paintAnchorHue = -1f)
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

        if (armed)
            BuildGun(root, body, tint, secondaryWeapon);

        // Selection ring: white so it reads as "yours, selected" against both
        // team colours, flat on the ground like the team rings.
        var unit = root.AddComponent<T>();
        unit._selectRing = GlowQuad(root.transform, "SelectRing", "VFX/ring",
            Color.white, 1.2f, 2.2f, 0.06f);
        unit._selectRing.SetActive(false);
        unit._modelPrefab = modelPrefab;
        unit._vehiclePrefab = vehiclePrefab;
        unit._stages = transformStages;
        unit._tint = tint;
        unit._paintAnchorHue = paintAnchorHue;

        return unit;
    }

    /// <summary>
    /// A stub blaster: one dark box and a muzzle. Built inactive so the weapon
    /// component's Awake — which caches muzzle, ownerRoot and team — runs only
    /// after those fields are assigned, then switched on. The optional second
    /// gun shares the muzzle: it is added AFTER activation, when Awake's
    /// defaults (muzzle = its own transform, owner = root) are already right.
    /// </summary>
    static void BuildGun(GameObject root, Transform body, Color tint, string secondaryWeapon)
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
    /// Fight back — or, when the attacker is nothing a robot can duel (a
    /// turret), break contact toward home. The Collector overrides this to
    /// always run: it has no gun to answer with.
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
            return;
        }

        // Shot by something un-duel-able while idle: step out of its range.
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

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + ThinkInterval;
            Think();
        }

        if (_order == OrderKind.Attack)
            TickAttack();
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
                else if (Arrived())
                {
                    _order = OrderKind.Idle;
                    _hasResume = false;
                }
                break;

            case OrderKind.Attack:
                if (_target == null || !_target.IsAlive)
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
        if (_workField == null)
            return;   // map mined dry — stand down for real

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

    /// <summary>Per-frame attack behaviour: chase, face, fire.</summary>
    void TickAttack()
    {
        if (_target == null || !_target.IsAlive)
            return;   // Think() resolves what happens next

        Vector3 toTarget = _target.transform.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        // Out of range OR occluded: keep moving. Without the line-of-sight
        // half, two units 20 m apart through a ridge both stop and pour fire
        // into the rock forever — neither dies, neither moves, permanent
        // deadlock at exactly the chokepoints where armies actually meet.
        if (distance > attackRange || !CanSee(_target))
        {
            // Chase — with a repath throttle so a hundred pursuers don't all
            // recompute paths every frame.
            if (Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + 0.5f;
                SetAgentDestination(_target.transform.position);
            }
            return;
        }

        // In range: stand, face, shoot.
        if (_agent.enabled && _agent.isOnNavMesh && !_agent.isStopped)
            _agent.isStopped = true;

        Quaternion face = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, face, 540f * Time.deltaTime);

        if (_weapon != null && Quaternion.Angle(transform.rotation, face) < 15f)
        {
            Vector3 aim = _target.transform.position + Vector3.up * 1.1f - _weapon.muzzle.position;
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
        if (_vehiclePrefab == null && !HasStages)
            return;

        bool wantVehicle = false;
        Vector3 goal = transform.position;
        if (_order == OrderKind.Move || _order == OrderKind.AttackMove)
            goal = _destination;
        else if (_order == OrderKind.Attack && _target != null)
            goal = _target.transform.position;
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        wantVehicle = flat.magnitude > TransformDistance;

        if (wantVehicle != _vehicleForm)
            Morph(wantVehicle);
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
                    GroundAlignedInstance(_stages[i], 2.0f);
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

    /// <summary>
    /// Stage and vehicle models carry no RobotLocomotion, so the factory's
    /// normalizer would centre them mid-air; ground them by bounds instead,
    /// nose along +Z with the unit's facing.
    /// </summary>
    void GroundAlignedInstance(GameObject prefab, float targetSize)
    {
        if (prefab == null)
            return;
        var instance = Instantiate(prefab, _body);
        instance.name = "Model";
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

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
        if (_hasResume)
            IssueAttackMove(_resumeDestination);
        else
            _order = OrderKind.Idle;
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
        Vector3 from = transform.position + Vector3.up * 1.1f;
        Vector3 to = target.transform.position + Vector3.up * 1.1f;
        return !Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
            || hit.transform.root == target.transform.root;
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
        // takes whatever form was showing.
        if (_morphRoutine != null)
        {
            StopCoroutine(_morphRoutine);
            _morphRoutine = null;
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

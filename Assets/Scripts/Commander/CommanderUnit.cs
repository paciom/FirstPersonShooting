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
    Transform _body;
    GameObject _selectRing;

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
            BuildGun(root, body, tint);

        // Selection ring: white so it reads as "yours, selected" against both
        // team colours, flat on the ground like the team rings.
        var unit = root.AddComponent<T>();
        unit._selectRing = GlowQuad(root.transform, "SelectRing", "VFX/ring",
            Color.white, 1.2f, 2.2f, 0.06f);
        unit._selectRing.SetActive(false);

        return unit;
    }

    /// <summary>
    /// A stub blaster: one dark box and a muzzle. Built inactive so the weapon
    /// component's Awake — which caches muzzle, ownerRoot and team — runs only
    /// after those fields are assigned, then switched on.
    /// </summary>
    static void BuildGun(GameObject root, Transform body, Color tint)
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
    }

    /// <summary>
    /// Every call site passes constants, so quads share cached materials —
    /// a fresh Material per ring across hundreds of units and sessions would
    /// pile up on the heap forever (materials do not die with GameObjects).
    /// </summary>
    static readonly Dictionary<string, Material> QuadMaterials = new Dictionary<string, Material>();

    protected static GameObject GlowQuad(Transform parent, string name, string texturePath,
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
        _weapon = GetComponentInChildren<Weapon>();
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
        }
    }

    void OnDisable()
    {
        All.Remove(this);
        if (_shield != null)
            _shield.OnDeRezzed -= HandleDeRez;
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
    /// trouble; the Collector overrides this with its harvest cycle. Runs
    /// only while no order is active, so a player Move always wins.
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
        }
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

        if (Quaternion.Angle(transform.rotation, face) < 15f)
        {
            Vector3 aim = _target.transform.position + Vector3.up * 1.1f - _weapon.muzzle.position;
            _weapon.TryFire(aim.normalized);
        }
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

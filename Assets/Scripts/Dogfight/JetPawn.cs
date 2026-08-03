using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One jet in DOGFIGHT — and, for the opening seconds, the robot it unfolds
/// from. It carries the whole transformation: every stop-motion stage is
/// instantiated up front and shown one at a time, so the intro morph is a
/// renderer toggle and never an Instantiate mid-ceremony.
///
/// EVERY PAWN LIVES AT THE SCENE ROOT. Bolts resolve their victim through
/// <c>hit.transform.root.GetComponent&lt;EnergyShield&gt;()</c>, so a jet
/// parented under the stage root is a jet nothing in this project can shoot.
/// <see cref="All"/> and <see cref="DespawnAll"/> stand in for the parent the
/// hierarchy would otherwise have given us — the TankPawn arrangement, for the
/// TankPawn reason.
///
/// FLIGHT IS ARCADE AND KINEMATIC. The jet always moves along its nose; the
/// driver supplies <see cref="Steer"/> (yaw right, pitch up), a
/// <see cref="Throttle"/> bias and <see cref="Firing"/>, all cleared after
/// they are read so a driver that stops writing stops steering. Bank is
/// cosmetic — a kid's mental model is "point where you want to go", and rudder
/// physics would only argue with it. The fly volume steers the jet back from
/// its edges instead of walling it: the assists act on the same steer values
/// the drivers write, so the player and the AI obey the sky the same way.
///
/// Built inside an INACTIVE GameObject and switched on at the end, because
/// <see cref="EnergyShield"/> latches Current from maxShield in Awake and
/// <see cref="Weapon"/> latches its team from the shield — the TankPawn birth
/// ritual, unchanged.
/// </summary>
public class JetPawn : MonoBehaviour
{
    // ------------------------------------------------------------- the registry

    // Plain static list, populated from OnEnable: a script recompile during
    // Play wipes statics, and a registry that only ever filled in Spawn would
    // come back from the reload empty while the sky is still full of jets.
    static readonly List<JetPawn> Live = new List<JetPawn>();

    public static IReadOnlyList<JetPawn> All => Live;

    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    /// <summary>Nearest live jet on another team, or null.</summary>
    public static JetPawn NearestEnemy(Vector3 from, int teamId, float maxRange = float.MaxValue)
    {
        JetPawn best = null;
        float bestSqr = maxRange * maxRange;
        foreach (var pawn in Live)
        {
            if (pawn == null || pawn.Team == teamId || pawn.IsDown)
                continue;
            float sqr = (pawn.transform.position - from).sqrMagnitude;
            if (sqr > bestSqr)
                continue;
            bestSqr = sqr;
            best = pawn;
        }
        return best;
    }

    // ------------------------------------------------------------------ tuning

    /// <summary>Metres across a jet's largest span is fitted to. Every stage is
    /// fitted to the same number on its own largest dimension — the rule
    /// Tools/stopmotion.py uses — because the fold passes through shapes that
    /// are tall, then round, then wide, and any single-axis rule inflates one
    /// of them.</summary>
    public const float JetSize = 3.4f;

    public const float SpeedMin = 16f;
    public const float SpeedCruise = 26f;
    public const float SpeedBoost = 40f;
    const float Acceleration = 14f;

    const float PitchRate = 80f;
    const float YawRate = 70f;

    /// <summary>Cosmetic bank at full yaw. Cosmetic only — see the class note.</summary>
    const float BankDegrees = 55f;

    /// <summary>Nose can never point more than this off the horizon. A loop is
    /// a simulator's trick; a kid chasing the cursor to the top of the screen
    /// should climb hard, not flip over their own tail.</summary>
    const float PitchLimit = 62f;

    /// <summary>
    /// Extra yaw that turns a generated stage nose-forward — the knob
    /// VehicleSkin.stageYawOffset is for the tank stages, but MEASURED rather
    /// than inherited. VehicleSkin's "longest horizontal axis forward" rule is
    /// deliberately not used: this delta wing is 1.9 wide by 1.7 long, so that
    /// rule faces it sideways by construction.
    ///
    /// The measurement (Tools-side vertex probe, 2026-08-03): the jet's nose
    /// points -X in the GLB, and the tank stage8s prove glTFast keeps the X
    /// sign for this content (their glTF barrel vector is -X, and TankPawn's
    /// in-Unity record says the barrels are -X too). -X onto +Z is +90. One
    /// constant covers the set because every jet clip frames its subject the
    /// same way.
    /// </summary>
    const float StageYaw = 90f;

    // The gun, in TankArsenal's vocabulary: coloured by TEAM, because "whose
    // shot is that" has to be answerable at a glance in a two-jet furball.
    const float GunDamage = 12f;
    const float GunRate = 6f;
    const float GunBoltSpeed = 90f;
    const float GunRange = 130f;

    /// <summary>Cone inside which the gun quietly aims at the target's future
    /// position instead of dead ahead. This is how eight-year-old aim lands
    /// hits without an aimbot: the player still has to get behind and point
    /// roughly right, the cone forgives the last few degrees.</summary>
    public const float AssistDegrees = 6f;
    public const float AssistRange = 90f;

    // ------------------------------------------------------------------- state

    public int Team { get; private set; }
    public EnergyShield Shield { get; private set; }
    public bool IsDown => Shield == null || Shield.IsDown;

    /// <summary>Yaw right / pitch up, each -1..1. Cleared after use.</summary>
    public Vector2 Steer { get; set; }

    /// <summary>-1 brake toward <see cref="SpeedMin"/>, 0 cruise, +1 boost.
    /// Cleared after use.</summary>
    public float Throttle { get; set; }

    public bool Firing { get; set; }

    /// <summary>Metres per second along the nose, for leading this jet.</summary>
    public float Speed { get; private set; } = SpeedCruise;

    public Vector3 Velocity => transform.forward * Speed;

    /// <summary>Aim height — the middle of the airframe.</summary>
    public Vector3 Center => transform.position;

    /// <summary>Where the chase camera's cockpit view sits.</summary>
    public Vector3 CockpitAnchor =>
        transform.position + transform.forward * (_halfLength * 0.35f) + transform.up * 0.5f;

    /// <summary>The enemy the aim assist is holding this frame, for the HUD.</summary>
    public JetPawn AssistTarget { get; private set; }

    /// <summary>While false the pawn stands still and flies nothing — the
    /// robot-on-the-pad phase. The mode flips it at launch.</summary>
    public bool FlightOn { get; set; }

    public System.Action<JetPawn> OnWrecked;

    float _yaw;
    float _pitch;
    float _roll;
    bool _down;
    float _downSince;
    bool _visible = true;
    Transform _model;
    GameObject[] _stages;
    Renderer[][] _stageRenderers;
    int _shownStage = -1;
    float _halfLength = JetSize * 0.5f;
    LaserBlaster _gun;
    Transform _muzzle;
    TrailRenderer[] _trails;
    bool _morphBurst;

    void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    void OnDisable()
    {
        Live.Remove(this);
    }

    // -------------------------------------------------------------------- birth

    /// <summary>
    /// Build a jet and stand its robot form at <paramref name="position"/>.
    ///
    /// <paramref name="stages"/> is the stop-motion set this pawn plays —
    /// resolved by the mode rather than read off the entry, because a robot
    /// that has not been through the jet pipeline yet borrows the ranger's
    /// airframe in its own team paint. A null or one-stage set skips the
    /// ceremony and is born already a jet (or, with no models at all, a lit
    /// block with wings — a missing model must never blank a fighter).
    /// </summary>
    public static JetPawn Spawn(RobotRoster.Entry entry, GameObject[] stages, int teamId,
        Vector3 position, float yaw, float maxShield)
    {
        // Born at IDENTITY rotation and turned onto its spawn yaw only after
        // the body is built: the stages are measured through world-space
        // renderer bounds, and a root already yawed to face its enemy would
        // smear wingspan into length in every box measured under it.
        var go = new GameObject($"JetPawn_{teamId}");
        go.transform.position = position;

        // Built while the object is live: fitting a generated model means
        // measuring it, and renderer bounds on a deactivated hierarchy are not
        // numbers to bet an airframe's scale on.
        Color tint = MatchAnnouncer.TeamColor(teamId);
        BuildStages(go.transform, stages, tint, entry.paintAnchorHue,
            out var model, out var stageInstances, out var box);

        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.SetActive(false);

        var jet = go.AddComponent<JetPawn>();
        jet.Team = teamId;
        jet._yaw = yaw;
        jet._model = model;
        jet._stages = stageInstances;
        jet._stageRenderers = new Renderer[stageInstances.Length][];
        for (int i = 0; i < stageInstances.Length; i++)
            jet._stageRenderers[i] = stageInstances[i].GetComponentsInChildren<Renderer>(true);
        jet._halfLength = Mathf.Max(1f, box.extents.z);

        // What bullets hit: a capsule along the fuselage, radius just over half
        // the wingspan's middle — sized so shots that visibly pass a wingtip
        // miss, on the Default layer and non-trigger because that is what every
        // bolt in this project raycasts against.
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.direction = 2;
        capsule.radius = Mathf.Clamp(box.extents.x * 0.55f, 0.8f, 1.4f);
        capsule.height = Mathf.Max(box.size.z * 1.1f, capsule.radius * 2.2f);
        capsule.center = Vector3.zero;

        jet.Shield = go.AddComponent<EnergyShield>();
        jet.Shield.maxShield = maxShield;
        jet.Shield.teamId = teamId;
        jet.Shield.regenDelay = 4f;
        jet.Shield.regenPerSecond = 8f;

        // The muzzle rides the nose; the gun is TankArsenal's cannon idea wound
        // up to a fighter's cadence, set before Awake ever runs (the host is
        // inactive) so the weapon latches team and muzzle correctly.
        jet._muzzle = new GameObject("JetMuzzle").transform;
        jet._muzzle.SetParent(go.transform, false);
        jet._muzzle.localPosition = new Vector3(0f, 0f, box.extents.z + 0.25f);
        jet._gun = go.AddComponent<LaserBlaster>();
        jet._gun.weaponName = "Photon Cannon";
        jet._gun.muzzle = jet._muzzle;
        jet._gun.ownerRoot = go.transform;
        jet._gun.color = tint;
        jet._gun.damage = GunDamage;
        jet._gun.shotsPerSecond = GunRate;
        jet._gun.boltSpeed = GunBoltSpeed;
        jet._gun.range = GunRange;

        jet._trails = BuildTrails(go.transform, box, tint);

        go.SetActive(true);
        jet.Shield.OnDeRezzed += jet.HandleDeRez;
        jet.ShowStage(0);
        return jet;
    }

    void OnDestroy()
    {
        if (Shield != null)
            Shield.OnDeRezzed -= HandleDeRez;
    }

    /// <summary>
    /// Instantiate and fit every stage under one Model holder.
    ///
    /// Each stage is fitted to <see cref="JetSize"/> on its own largest
    /// dimension and centred on its bounds — the flight pivot — with one
    /// <see cref="StageYaw"/> turn applied BEFORE anything is measured, because
    /// a rotation after the fit moves the mesh off the centring solved for it.
    /// Returns the JET stage's bounds (the last one): the collider, muzzle and
    /// trails all describe the jet, not the robot it starts as.
    /// </summary>
    static void BuildStages(Transform root, GameObject[] stages, Color tint, float anchorHue,
        out Transform model, out GameObject[] instances, out Bounds jetBox)
    {
        model = new GameObject("Model").transform;
        model.SetParent(root, false);

        if (stages == null || stages.Length == 0)
        {
            instances = new[] { BuildBlockJet(model, tint) };
            jetBox = new Bounds(Vector3.zero, new Vector3(JetSize, 1f, JetSize * 0.9f));
            return;
        }

        instances = new GameObject[stages.Length];
        jetBox = new Bounds(Vector3.zero, Vector3.one * JetSize);
        for (int i = 0; i < stages.Length; i++)
        {
            var holder = new GameObject($"Stage{i + 1}").transform;
            holder.SetParent(model, false);
            var instance = Object.Instantiate(stages[i], holder);
            instance.transform.localRotation = Quaternion.Euler(0f, StageYaw, 0f);

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds raw = RobotFactory.MeasureWorldBounds(renderers);
                float largest = Mathf.Max(raw.size.x, raw.size.y, raw.size.z);
                instance.transform.localScale *= JetSize / Mathf.Max(0.01f, largest);

                Bounds fitted = RobotFactory.MeasureWorldBounds(renderers);
                instance.transform.position += holder.position - fitted.center;

                TeamPaint.Apply(renderers, tint, 0, false, anchorHue);
                // World box == local box here: the root is still at identity
                // while bodies are built (see Spawn).
                if (i == stages.Length - 1)
                    jetBox = new Bounds(Vector3.zero, fitted.size);
            }
            instances[i] = holder.gameObject;
        }
    }

    /// <summary>Last resort: a lit slab with wings, so a roster with no jet
    /// models anywhere still dogfights.</summary>
    static GameObject BuildBlockJet(Transform model, Color tint)
    {
        var holder = new GameObject("Stage1").transform;
        holder.SetParent(model, false);
        var material = ArenaMaterials.Lit(
            $"jet-block-{ColorUtility.ToHtmlStringRGB(tint)}", tint * 0.7f, 0.4f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(body.GetComponent<Collider>());
        body.name = "Fuselage";
        body.transform.SetParent(holder, false);
        body.transform.localScale = new Vector3(0.7f, 0.55f, JetSize * 0.9f);
        body.GetComponent<MeshRenderer>().sharedMaterial = material;

        var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(wing.GetComponent<Collider>());
        wing.name = "Wing";
        wing.transform.SetParent(holder, false);
        wing.transform.localScale = new Vector3(JetSize, 0.12f, 1.1f);
        wing.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        wing.GetComponent<MeshRenderer>().sharedMaterial = material;
        return holder.gameObject;
    }

    /// <summary>Wingtip contrails in the team's colour. What makes a turning
    /// fight readable from the broadcast camera — and from the cockpit mirror
    /// of whoever is being chased.</summary>
    static TrailRenderer[] BuildTrails(Transform root, Bounds box, Color tint)
    {
        var trails = new TrailRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject(i == 0 ? "TrailL" : "TrailR");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(
                (i == 0 ? -1f : 1f) * box.extents.x * 0.85f, 0f, -box.extents.z * 0.7f);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.startWidth = 0.2f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.4f;
            trail.material = VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, tint, 1.6f);
            trail.emitting = false;
            trails[i] = trail;
        }
        return trails;
    }

    // ------------------------------------------------------------ the ceremony

    /// <summary>
    /// Drive the stop-motion fold: 0 shows the standing robot, 1 the jet, with
    /// the swap burst at TransformMode's 42% — the shared vocabulary every
    /// morph in this project speaks.
    /// </summary>
    public void ShowMorph(float progress)
    {
        int last = _stages.Length - 1;
        ShowStage(Mathf.Clamp(Mathf.FloorToInt(progress * (last + 1)), 0, last));
        if (!_morphBurst && progress >= 0.42f)
        {
            _morphBurst = true;
            VfxUtil.Explosion(Center, MatchAnnouncer.TeamColor(Team), 1.1f);
        }
    }

    public void ShowStage(int index)
    {
        index = Mathf.Clamp(index, 0, _stages.Length - 1);
        if (index == _shownStage)
            return;
        _shownStage = index;
        for (int i = 0; i < _stageRenderers.Length; i++)
        {
            bool on = i == index && _visible;
            foreach (var renderer in _stageRenderers[i])
                if (renderer != null)
                    renderer.enabled = on;
        }
    }

    /// <summary>Jump straight to jet form — pawns whose robot never got a jet
    /// pipeline run, and every respawn.</summary>
    public void ShowJet() => ShowStage(_stages.Length - 1);

    public bool HasMorph => _stages != null && _stages.Length > 1;

    /// <summary>Hidden in the cockpit view (a canopy over the camera is a
    /// windscreen made of your own head) and blinked by the spawn grace.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        int shown = _shownStage;
        _shownStage = -1;
        ShowStage(shown);
    }

    // -------------------------------------------------------------- every frame

    void Update()
    {
        if (_down)
        {
            Tumble();
            return;
        }
        if (!FlightOn)
        {
            Steer = Vector2.zero;
            Throttle = 0f;
            Firing = false;
            return;
        }

        float dt = Time.deltaTime;
        Vector2 steer = Vector2.ClampMagnitude(Steer, 1.4f);
        float throttle = Mathf.Clamp(Throttle, -1f, 1f);
        Steer = Vector2.zero;                       // see the property docs
        Throttle = 0f;

        steer = DogfightSky.SteerAssist(transform.position, transform.forward, steer);

        _yaw += steer.x * YawRate * dt;
        _pitch = Mathf.Clamp(_pitch + steer.y * PitchRate * dt, -PitchLimit, PitchLimit);
        // With the stick centred the nose eases back toward the horizon — the
        // auto-level that makes "let go to fly straight" true.
        if (Mathf.Abs(steer.y) < 0.05f)
            _pitch = Mathf.MoveTowards(_pitch, 0f, 14f * dt);
        _roll = Mathf.Lerp(_roll, -steer.x * BankDegrees, 1f - Mathf.Exp(-6f * dt));

        transform.rotation = Quaternion.Euler(-_pitch, _yaw, _roll);

        float wanted = throttle >= 0f
            ? Mathf.Lerp(SpeedCruise, SpeedBoost, throttle)
            : Mathf.Lerp(SpeedCruise, SpeedMin, -throttle);
        Speed = Mathf.MoveTowards(Speed, wanted, Acceleration * dt);
        transform.position += transform.forward * (Speed * dt);
        transform.position = DogfightSky.KeepOffProps(transform.position, 1.2f);

        foreach (var trail in _trails)
            trail.emitting = true;

        PullTrigger();
        Firing = false;
    }

    /// <summary>
    /// Fire along the nose — through the assist when an enemy sits inside its
    /// cone. The assist aims at the target's LED future position, so landing
    /// hits still means being behind someone, pointed the right way.
    /// </summary>
    void PullTrigger()
    {
        AssistTarget = null;
        var target = NearestEnemy(transform.position, Team, AssistRange);
        Vector3 direction = transform.forward;
        if (target != null)
        {
            Vector3 gap = target.Center - _muzzle.position;
            float seconds = gap.magnitude / GunBoltSpeed;
            Vector3 predicted = target.Center + target.Velocity * seconds - _muzzle.position;
            if (Vector3.Angle(transform.forward, predicted) <= AssistDegrees)
            {
                direction = predicted.normalized;
                AssistTarget = target;
            }
        }

        if (Firing)
            _gun.TryFire(direction);
    }

    // ------------------------------------------------------------------- death

    void HandleDeRez()
    {
        if (_down)
            return;
        _down = true;
        _downSince = Time.time;
        Firing = false;
        foreach (var trail in _trails)
            trail.emitting = false;
        VfxUtil.Explosion(Center, MatchAnnouncer.TeamColor(Team), 1.6f);
        OnWrecked?.Invoke(this);
    }

    /// <summary>A dying jet falls out of the fight rather than blinking away —
    /// the spiral is the broadcast shot, and the beat the respawn clock buys.</summary>
    void Tumble()
    {
        float dt = Time.deltaTime;
        float falling = Time.time - _downSince;
        Speed = Mathf.MoveTowards(Speed, 9f, 10f * dt);
        _pitch = Mathf.MoveTowards(_pitch, -55f, 40f * dt);
        _roll += 260f * dt;
        transform.rotation = Quaternion.Euler(-_pitch, _yaw, _roll);
        transform.position += transform.forward * (Speed * dt)
                              + Vector3.down * (falling * 6f * dt);
        if (falling > 0.35f && Random.value < 12f * dt)
            VfxUtil.SpawnBurst(Center, new Color(1f, 0.6f, 0.25f), 4, 3f, 0.14f);
    }

    /// <summary>Back on the spawn ring: airframe whole, gun live, nose level.</summary>
    public void Respawn(Vector3 position, float yaw)
    {
        _down = false;
        _yaw = yaw;
        _pitch = 0f;
        _roll = 0f;
        Speed = SpeedCruise;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (var trail in _trails)
            trail.Clear();
        ShowJet();
        if (Shield != null)
            Shield.Rematerialize();
        VfxUtil.SpawnBurst(Center, MatchAnnouncer.TeamColor(Team), 24, 7f, 0.16f);
    }
}

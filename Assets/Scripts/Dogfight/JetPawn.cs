using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One combatant in DOGFIGHT — a jet, until it decides not to be. The pawn
/// carries BOTH of its robot's stop-motion sets (the jet fold and the tank
/// fold, which share the standing robot at one end), so it can play any leg
/// of the triangle: jet unfolds to a robot under a glide chute, the robot
/// folds on into a tank on the deck, and a tank re-folds through the robot
/// back into the sky. Chained morphs are just the two ceremonies played
/// back to back.
///
/// THE FORMS ARE A TRIANGLE, NOT A COSTUME. A jet fights jets; a tank is
/// slow and grounded but carries the same guns and missiles up at the sky
/// and across the deck at the batteries; a robot under canopy falls slowly,
/// aims freely, and is a target the whole way down. Same shield, same
/// wrecks, same seams — <c>Steer</c>/<c>Throttle</c>/<c>Firing</c> plus
/// <see cref="AimAt"/> — whoever is driving.
///
/// EVERY PAWN LIVES AT THE SCENE ROOT (bolts resolve their victim through
/// <c>hit.transform.root</c>), with the plain-static registry standing in
/// for the parent the hierarchy would have given us. Built inside an
/// INACTIVE GameObject and switched on at the end, because EnergyShield and
/// Weapon latch their numbers in Awake — the TankPawn birth ritual.
///
/// Both stage sets are instantiated AT SPAWN, while the root still sits at
/// identity: stages are fitted through world-space renderer bounds, and a
/// root already yawed toward its enemy smears wingspan into length in every
/// box measured under it. Lazy-building the tank set mid-fight would repeat
/// that bug at a random rotation.
/// </summary>
public class JetPawn : MonoBehaviour
{
    public enum Form { Jet, Robot, Tank }

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

    /// <summary>Nearest live pawn on another team, whatever form it is
    /// wearing, or null.</summary>
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

    /// <summary>Metres across a stage's largest span is fitted to — every
    /// stage of both sets, on its own largest dimension (stopmotion.py's
    /// rule), because the folds pass through shapes that are tall, then
    /// round, then wide, and any single-axis rule inflates one of them.</summary>
    public const float JetSize = 3.4f;

    public const float SpeedMin = 16f;
    public const float SpeedCruise = 26f;
    public const float SpeedBoost = 40f;
    const float Acceleration = 14f;

    const float PitchRate = 80f;
    const float YawRate = 70f;
    const float BankDegrees = 55f;
    const float PitchLimit = 62f;

    /// <summary>
    /// A stop-motion set does NOT share one frame: Meshy normalizes each
    /// reconstruction to its own canonical front, and mid-set — at the frame
    /// where it stops reading the image as a creature and starts reading it
    /// as a craft — the canonical flips a quarter turn. Bounds cannot make
    /// this call (a half-folded robot lying flat measures like an aircraft
    /// and faces like a robot), so the splits are DATA, measured per robot
    /// per set by Tools/stageorient.py and tabled here. The fleet's tank
    /// sets probed 2026-08-04: four everywhere except racer and scout, whose
    /// stage five still faces like a robot. A robot missing from a table
    /// takes the default — which is also what a robot borrowing the ranger
    /// airframe should take, since the split belongs to the SET.
    /// </summary>
    static readonly Dictionary<string, int> JetRobotFrames = new Dictionary<string, int>
    {
        // The full fleet, probed 2026-08-05 as each set landed. Titan folds
        // into a craft frame by stage four; bolt, hawk and scout stay
        // creature-framed almost to the end.
        { "ranger", 5 }, { "bolt", 6 }, { "hawk", 6 }, { "knight", 4 },
        { "panther", 5 }, { "racer", 5 }, { "samurai", 5 }, { "scout", 6 },
        { "titan", 3 },
    };
    const int JetRobotFramesDefault = 5;

    static readonly Dictionary<string, int> TankRobotFrames = new Dictionary<string, int>
    {
        { "ranger", 4 }, { "bolt", 4 }, { "hawk", 4 }, { "knight", 4 },
        { "panther", 4 }, { "racer", 5 }, { "samurai", 4 }, { "scout", 5 },
        { "titan", 4 },
    };
    const int TankRobotFramesDefault = 4;

    static int FramesFor(Dictionary<string, int> table, string robotName, int fallback) =>
        robotName != null && table.TryGetValue(robotName.ToLowerInvariant(), out int frames)
            ? frames
            : fallback;

    /// <summary>Where the jet set's robot frames end for one robot. Public
    /// because the select cards replay the same stage set and have to turn
    /// the same stages the same way.</summary>
    public static int JetRobotFramesFor(string robotName) =>
        FramesFor(JetRobotFrames, robotName, JetRobotFramesDefault);

    /// <summary>Yaw for robot-framed stages: the Meshy-humanoid canonical
    /// through glTFast, probe-confirmed at ~0 across both sets' robot frames.</summary>
    const float RobotStageYaw = 0f;

    /// <summary>
    /// Yaw for the jet set's aircraft-framed stages. -90 is an IN-GAME
    /// measurement (a probe-derived +90 flew the set tail-first); the tank
    /// set does not use this — its vehicle yaw is measured live off the
    /// stage-8 barrel markers and shared by its whole vehicle frame group.
    /// If a future set flies backwards, flip 180 and trust the screenshot.
    /// Public for the same reason JetRobotFramesFor is.
    /// </summary>
    public const float StageYaw = -90f;

    // The gun, in TankArsenal's vocabulary: coloured by TEAM, because "whose
    // shot is that" has to be answerable at a glance in a two-jet furball.
    const float GunDamage = 12f;
    const float GunRate = 6f;
    const float GunBoltSpeed = 90f;
    const float GunRange = 130f;

    /// <summary>Cone inside which the gun quietly aims at the target's future
    /// position instead of dead ahead — how eight-year-old aim lands hits
    /// without an aimbot.</summary>
    public const float AssistDegrees = 6f;
    public const float AssistRange = 90f;

    public const float MissileCooldown = 5f;

    /// <summary>Scale on this pawn's missile reload — the difficulty picker's
    /// hand on the player's tube (public: survives a mid-Play recompile).</summary>
    public float missileCooldownScale = 1f;

    public const int FlareChargesMax = 3;
    const float FlareRechargeSeconds = 7f;

    /// <summary>One leg of a fold, mid-combat. Snappier than the intro's
    /// ceremony; a chained tank-to-jet spends two of these.</summary>
    const float MorphSeconds = 0.8f;

    /// <summary>The chute: how fast a robot under canopy falls and drifts.</summary>
    const float ChuteFallSpeed = 4.5f;
    const float ChuteDriftSpeed = 8f;

    /// <summary>The deck gait — CharacterMotor's walkSpeed/sprintSpeed, so a
    /// landed robot moves exactly like the gunfight game's.</summary>
    const float WalkSpeed = 6f;
    const float RunSpeed = 9f;

    /// <summary>The hop: launch speed and its own gravity. Apex ≈ 3.2 m,
    /// about the gunfight motor's body-height leap for these robots.</summary>
    const float JumpSpeed = 13f;
    const float JumpGravity = 26f;

    const float TankDriveSpeed = 9f;
    const float TankTurnSpeed = 110f;
    const float TankFallAcceleration = 28f;
    const float TankFallTerminal = 40f;

    // Where the deck is belongs to the MAP: flat tarmac, a curved worldlet
    // or floating station decks — every ground contact in this class asks
    // DogfightSky.GroundHeight rather than assuming a plane.

    // ------------------------------------------------------------------- state

    public int Team { get; private set; }
    public EnergyShield Shield { get; private set; }
    public bool IsDown => Shield == null || Shield.IsDown;

    public Form CurrentForm { get; private set; } = Form.Jet;

    /// <summary>Mid-fold: controls coast, guns are cold, the silhouette is
    /// nobody's. The commitment that makes transforming a decision.</summary>
    public bool Morphing => _morphQueue.Count > 0;

    /// <summary>Yaw right / pitch up in jet form; turn / drive in tank form;
    /// drift in robot form. Each -1..1, cleared after use.</summary>
    public Vector2 Steer { get; set; }

    /// <summary>-1 brake, 0 cruise, +1 boost. Jet form only. Cleared after use.</summary>
    public float Throttle { get; set; }

    public bool Firing { get; set; }

    /// <summary>Run instead of walk — the gunfight game's sprint, read by the
    /// grounded robot. Cleared after use like Steer.</summary>
    public bool Sprint { get; set; }

    /// <summary>Ask for a hop. Only a robot standing on the deck obliges —
    /// the gunfight motor's "a tank does not jump" rule; everyone else lets
    /// the request die at the end of the frame.</summary>
    public void RequestJump() => _jumpQueued = true;

    /// <summary>Metres per second along the nose, jet form.</summary>
    public float Speed { get; private set; } = SpeedCruise;

    public Vector3 Velocity
    {
        get
        {
            switch (CurrentForm)
            {
                case Form.Jet: return transform.forward * Speed;
                case Form.Robot: return _airVelocity + Vector3.up * _verticalSpeed;
                default: return Grounded
                    ? transform.forward * _tankDrive * TankDriveSpeed
                    : Vector3.up * _verticalSpeed;
            }
        }
    }

    /// <summary>What the speed readout shows for this form.</summary>
    public float ReadoutSpeed => Velocity.magnitude;

    public Vector3 Center => transform.position;

    public Vector3 CockpitAnchor
    {
        get
        {
            switch (CurrentForm)
            {
                case Form.Robot:
                    return transform.position + transform.up * 1.1f + transform.forward * 0.3f;
                case Form.Tank:
                    return transform.position + Vector3.up * 1.2f + transform.forward * 0.4f;
                default:
                    return transform.position + transform.forward * (_halfLength * 0.35f)
                           + transform.up * 0.5f;
            }
        }
    }

    /// <summary>Where this pawn's nose — or its aim — points. What the lock
    /// cone and the brains measure against, form-agnostic.</summary>
    public Vector3 AimDirection
    {
        get
        {
            if (CurrentForm == Form.Jet || !_hasAim)
                return transform.forward;
            Vector3 to = _aimPoint - Center;
            return to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
        }
    }

    /// <summary>On the deck (or near enough that flares should eject upward).</summary>
    public bool Grounded { get; private set; }

    public JetPawn AssistTarget { get; private set; }

    /// <summary>True while the assist bent this frame's aim at anything —
    /// pawn or building. What the reticle warms on.</summary>
    public bool AssistEngaged { get; private set; }

    /// <summary>An explicitly chosen target — the touch player's tap-lock.
    /// The gun assist and the idle aims prefer it over whatever is merely
    /// nearest. Cleared by whoever set it.</summary>
    public Transform FocusTarget { get; set; }

    public bool MissileReady => Time.time >= _missileReadyAt;

    public float MissileReadyFraction =>
        Mathf.Clamp01(1f - (_missileReadyAt - Time.time)
                           / (MissileCooldown * missileCooldownScale));

    public int FlareCharges => _flareCharges;

    /// <summary>While false the pawn stands still and flies nothing — the
    /// robot-on-the-pad phase. The mode flips it at launch.</summary>
    public bool FlightOn { get; set; }

    public System.Action<JetPawn> OnWrecked;

    /// <summary>Fired when the falling wreck meets the deck (or immediately,
    /// for a pawn killed standing on it) — the moment of the big explosion.
    /// The mode schedules the respawn off this, so a long fall is a long
    /// funeral and a short one is a short one.</summary>
    public System.Action<JetPawn> OnWreckLanded;

    /// <summary>True from impact until respawn: the wreck is gone, the
    /// crash site is burning, the camera is allowed to look away.</summary>
    public bool WreckLanded { get; private set; }

    /// <summary>One stop-motion set, fitted and parked under the model.</summary>
    struct StageSet
    {
        public GameObject[] holders;
        public Renderer[][] renderers;
        public bool Exists => holders != null && holders.Length > 0;
        public int Last => holders.Length - 1;
    }

    /// <summary>One leg of a morph: which set plays, and which way.</summary>
    struct MorphLeg
    {
        public bool tankSet;
        public bool reverse;
    }

    float _yaw;
    float _pitch;
    float _roll;

    /// <summary>
    /// Flight attitude on the space maps, carried as a quaternion because the
    /// jet may end up any way round and Euler angles cannot say that. Excludes
    /// the cosmetic bank, so <c>_orientation * forward</c> is the direction the
    /// jet actually travels no matter how far it is leaning.
    /// </summary>
    Quaternion _orientation = Quaternion.identity;
    float _missileReadyAt;
    int _flareCharges = FlareChargesMax;
    float _flareRechargeAt;
    bool _down;
    float _downSince;
    bool _visible = true;
    Transform _model;
    StageSet _jetSet;
    StageSet _tankSet;
    bool _shownIsTank;
    int _shownStage = -1;
    float _halfLength = JetSize * 0.5f;
    float _jetCapsuleRadius = 1f;
    float _jetCapsuleHeight = 3.6f;
    CapsuleCollider _capsule;
    LaserBlaster _gun;
    Transform _muzzle;
    TankTurret _turret;
    TrailRenderer[] _trails;
    GameObject _chute;
    ParticleSystem _damageSmoke;
    ParticleSystem _burnFlame;
    bool _morphBurst;

    readonly List<MorphLeg> _morphQueue = new List<MorphLeg>();
    float _morphStart;
    Form _morphTarget;

    Vector3 _aimPoint;
    bool _hasAim;
    Vector3 _airVelocity;
    float _verticalSpeed;
    float _tankDrive;

    // The hop rides its own little arc over the ride height, so Grounded
    // stays true and the chute/landing logic never hears about it.
    bool _jumpQueued;
    float _hop;
    float _hopSpeed;

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
    /// Build a pawn and stand its robot form at <paramref name="position"/>.
    ///
    /// <paramref name="jetStages"/> is resolved by the mode (a robot that has
    /// not been through the jet pipeline borrows the ranger's airframe); the
    /// TANK set comes off the entry itself, because every robot in the fleet
    /// has been through the tank pipeline in its own body.
    /// </summary>
    public static JetPawn Spawn(RobotRoster.Entry entry, GameObject[] jetStages, int teamId,
        Vector3 position, float yaw, float maxShield)
    {
        // Born at IDENTITY rotation and turned onto its spawn yaw only after
        // the bodies are built — see the class note on world-space fitting.
        var go = new GameObject($"JetPawn_{teamId}");
        go.transform.position = position;

        Color tint = MatchAnnouncer.TeamColor(teamId);
        var jet = go.AddComponent<JetPawn>();
        jet._model = new GameObject("Model").transform;
        jet._model.SetParent(go.transform, false);

        jet._jetSet = BuildSet(jet._model, jetStages, tint, entry.paintAnchorHue,
            FramesFor(JetRobotFrames, entry.displayName, JetRobotFramesDefault),
            _ => StageYaw, out var jetBox);
        if (!jet._jetSet.Exists)
            jet._jetSet = BlockJetSet(jet._model, tint, out jetBox);
        // The tank set's vehicle yaw is measured off its stage8 barrel
        // markers — TankPawn's answer, for TankPawn's reason: nothing
        // generated agrees about forward, but a tank at rest points its gun
        // over its nose. Resolved once from the LAST stage and shared by the
        // whole vehicle frame group, because only stage8 carries markers and
        // the old per-stage fallback marched the mid-fold 180 off the tank.
        jet._tankSet = BuildSet(jet._model, entry.HasStages ? entry.transformStages : null,
            tint, entry.paintAnchorHue,
            FramesFor(TankRobotFrames, entry.displayName, TankRobotFramesDefault),
            last => TankPawn.NoseYaw(jet._model, last, -90f), out _);

        jet.Team = teamId;
        jet._yaw = yaw;
        jet._halfLength = Mathf.Max(1f, jetBox.extents.z);
        jet._jetCapsuleRadius = Mathf.Clamp(jetBox.extents.x * 0.55f, 0.8f, 1.4f);
        jet._jetCapsuleHeight = Mathf.Max(jetBox.size.z * 1.1f, jet._jetCapsuleRadius * 2.2f);

        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.SetActive(false);

        jet._capsule = go.AddComponent<CapsuleCollider>();

        jet.Shield = go.AddComponent<EnergyShield>();
        jet.Shield.maxShield = maxShield;
        jet.Shield.teamId = teamId;
        // NO regeneration: damage is a story the airframe keeps telling
        // until the wreck — smoke, then flame, then down. The only repair
        // in this sky is the respawn.
        jet.Shield.regenDelay = 4f;
        jet.Shield.regenPerSecond = 0f;

        jet._muzzle = new GameObject("JetMuzzle").transform;
        jet._muzzle.SetParent(go.transform, false);
        jet._muzzle.localPosition = new Vector3(0f, 0f, jetBox.extents.z + 0.25f);
        jet._gun = go.AddComponent<LaserBlaster>();
        jet._gun.weaponName = "Photon Cannon";
        jet._gun.muzzle = jet._muzzle;
        jet._gun.ownerRoot = go.transform;
        jet._gun.color = tint;
        jet._gun.damage = GunDamage;
        jet._gun.shotsPerSecond = GunRate;
        jet._gun.boltSpeed = GunBoltSpeed;
        jet._gun.range = GunRange;

        // The turret servo waits, disabled, for the tank fold to land —
        // TankTurret's own contract ("runs only while its character is
        // actually a tank").
        jet._turret = go.AddComponent<TankTurret>();
        jet._turret.enabled = false;

        jet._trails = BuildTrails(go.transform, jetBox, tint);
        jet.BuildChute(tint);

        go.SetActive(true);
        jet.Shield.OnDeRezzed += jet.HandleDeRez;
        jet.ApplyFormFit(Form.Jet);
        jet.ShowStage(false, 0);
        return jet;
    }

    void OnDestroy()
    {
        if (Shield != null)
            Shield.OnDeRezzed -= HandleDeRez;
    }

    /// <summary>
    /// Instantiate and fit one stop-motion set. The first
    /// <paramref name="robotFrames"/> stages take <see cref="RobotStageYaw"/>;
    /// the rest share one craft yaw, resolved from the LAST stage (the
    /// finished jet or tank) by <paramref name="craftYawFrom"/> — which is
    /// why the whole set is instantiated before anything is turned. Each
    /// stage is fitted to <see cref="JetSize"/> on its own largest dimension
    /// and centred on its bounds — the flight pivot — with its yaw applied
    /// BEFORE anything is measured, because a rotation after the fit moves
    /// the mesh off the centring solved for it. Returns the LAST stage's
    /// bounds through <paramref name="finalBox"/> (the fighting shape).
    /// </summary>
    static StageSet BuildSet(Transform model, GameObject[] stages, Color tint, float anchorHue,
        int robotFrames, System.Func<Transform, float> craftYawFrom, out Bounds finalBox)
    {
        finalBox = new Bounds(Vector3.zero, Vector3.one * JetSize);
        if (stages == null || stages.Length == 0)
            return default;

        var set = new StageSet
        {
            holders = new GameObject[stages.Length],
            renderers = new Renderer[stages.Length][],
        };
        for (int i = 0; i < stages.Length; i++)
        {
            var holder = new GameObject($"Stage{i + 1}").transform;
            holder.SetParent(model, false);
            var instance = Object.Instantiate(stages[i], holder);
            set.holders[i] = holder.gameObject;
            set.renderers[i] = instance.GetComponentsInChildren<Renderer>(true);
        }

        float craftYaw = craftYawFrom(set.holders[stages.Length - 1].transform.GetChild(0));

        for (int i = 0; i < stages.Length; i++)
        {
            var holder = set.holders[i].transform;
            var instance = holder.GetChild(0);
            var renderers = set.renderers[i];
            if (renderers.Length > 0)
            {
                instance.localRotation = Quaternion.Euler(0f,
                    i < robotFrames ? RobotStageYaw : craftYaw, 0f);

                Bounds raw = RobotFactory.MeasureWorldBounds(renderers);
                float largest = Mathf.Max(raw.size.x, raw.size.y, raw.size.z);
                instance.localScale *= JetSize / Mathf.Max(0.01f, largest);

                Bounds fitted = RobotFactory.MeasureWorldBounds(renderers);
                instance.position += holder.position - fitted.center;

                TeamPaint.Apply(renderers, tint, 0, false, anchorHue);
                if (i == stages.Length - 1)
                    finalBox = new Bounds(Vector3.zero, fitted.size);
            }
            // Start dark; ShowStage lights exactly one.
            foreach (var renderer in renderers)
                renderer.enabled = false;
        }
        return set;
    }

    /// <summary>Last resort: a one-stage set holding a lit slab with wings,
    /// so a roster with no jet models anywhere still dogfights.</summary>
    static StageSet BlockJetSet(Transform model, Color tint, out Bounds box)
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

        box = new Bounds(Vector3.zero, new Vector3(JetSize, 0.6f, JetSize * 0.9f));
        return new StageSet
        {
            holders = new[] { holder.gameObject },
            renderers = new[] { holder.GetComponentsInChildren<Renderer>(true) },
        };
    }

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

    /// <summary>The glide canopy: a squashed dome on four cords, in team
    /// colour, hidden until a robot is actually hanging from it.</summary>
    void BuildChute(Color tint)
    {
        _chute = new GameObject("Chute");
        _chute.transform.SetParent(transform, false);
        _chute.transform.localPosition = new Vector3(0f, 2.6f, 0f);

        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(canopy.GetComponent<Collider>());
        canopy.name = "Canopy";
        canopy.transform.SetParent(_chute.transform, false);
        canopy.transform.localScale = new Vector3(3.6f, 1.2f, 3.6f);
        canopy.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Lit(
            $"dogfight-chute-{ColorUtility.ToHtmlStringRGB(tint)}",
            Color.Lerp(tint, Color.white, 0.35f), 0.3f);

        var cordMaterial = ArenaMaterials.Lit("dogfight-chute-cord",
            new Color(0.85f, 0.88f, 0.92f), 0.2f);
        for (int i = 0; i < 4; i++)
        {
            var cord = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(cord.GetComponent<Collider>());
            cord.name = "Cord";
            cord.transform.SetParent(_chute.transform, false);
            float x = (i % 2 == 0 ? -1f : 1f) * 1.1f;
            float z = (i < 2 ? -1f : 1f) * 1.1f;
            cord.transform.localScale = new Vector3(0.03f, 1.3f, 0.03f);
            cord.transform.localPosition = new Vector3(x * 0.55f, -1.3f, z * 0.55f);
            cord.transform.localRotation = Quaternion.FromToRotation(
                Vector3.up, new Vector3(x * 0.4f, 2.4f, z * 0.4f).normalized);
            cord.GetComponent<MeshRenderer>().sharedMaterial = cordMaterial;
        }
        _chute.SetActive(false);
    }

    // ---------------------------------------------------------------- the forms

    /// <summary>The player's T: the triangle in one direction. Jet unfolds to
    /// the chute; the robot folds down into the tank; the tank re-folds all
    /// the way back into the sky.</summary>
    public void RequestNextForm()
    {
        switch (CurrentForm)
        {
            case Form.Jet: RequestForm(Form.Robot); break;
            case Form.Robot: RequestForm(Form.Tank); break;
            default: RequestForm(Form.Jet); break;
        }
    }

    /// <summary>Queue the fold(s) to <paramref name="target"/>. Refused while
    /// down, already folding, or before launch; a target with no stage set to
    /// express it (a roster entry with no tank fold) is refused the same way.</summary>
    public bool RequestForm(Form target)
    {
        if (_down || !FlightOn || Morphing || target == CurrentForm)
            return false;
        if ((target == Form.Tank || CurrentForm == Form.Tank) && !_tankSet.Exists)
            return false;
        if ((target == Form.Jet || CurrentForm == Form.Jet) && !_jetSet.Exists)
            return false;

        _morphQueue.Clear();
        switch (CurrentForm)
        {
            case Form.Jet:
                _morphQueue.Add(new MorphLeg { tankSet = false, reverse = true });
                if (target == Form.Tank)
                    _morphQueue.Add(new MorphLeg { tankSet = true, reverse = false });
                break;
            case Form.Robot:
                _morphQueue.Add(target == Form.Tank
                    ? new MorphLeg { tankSet = true, reverse = false }
                    : new MorphLeg { tankSet = false, reverse = false });
                break;
            default:
                _morphQueue.Add(new MorphLeg { tankSet = true, reverse = true });
                if (target == Form.Jet)
                    _morphQueue.Add(new MorphLeg { tankSet = false, reverse = false });
                break;
        }
        _morphTarget = target;
        _morphStart = Time.time;
        _morphBurst = false;

        // Leaving a form puts its furniture away before the fold plays.
        _turret.enabled = false;
        _gun.muzzle = _muzzle;
        SetChuteVisible(false);
        foreach (var trail in _trails)
            trail.emitting = false;
        // The fold coasts on whatever motion it had; seed the fall state from
        // the form being left so a jet that folds mid-air starts dropping.
        if (CurrentForm == Form.Jet)
        {
            _airVelocity = new Vector3(Velocity.x, 0f, Velocity.z) * 0.5f;
            _verticalSpeed = Mathf.Min(0f, Velocity.y);
        }
        return true;
    }

    void AdvanceMorph(float dt)
    {
        var leg = _morphQueue[0];
        var set = leg.tankSet ? _tankSet : _jetSet;
        float progress = Mathf.Clamp01((Time.time - _morphStart) / MorphSeconds);
        float shown = leg.reverse ? 1f - progress : progress;
        ShowStage(leg.tankSet,
            Mathf.Clamp(Mathf.FloorToInt(shown * (set.Last + 1)), 0, set.Last));

        if (!_morphBurst && progress >= 0.42f)
        {
            _morphBurst = true;
            VfxUtil.EnergyBurst(Center, MatchAnnouncer.TeamColor(Team), 1.1f);
        }

        if (progress < 1f)
            return;

        _morphQueue.RemoveAt(0);
        _morphStart = Time.time;
        _morphBurst = false;
        if (_morphQueue.Count > 0)
            return;

        ArriveIn(_morphTarget);
    }

    /// <summary>The fold has landed: dress the new form and hand it its
    /// physics state.</summary>
    void ArriveIn(Form form)
    {
        CurrentForm = form;
        ApplyFormFit(form);
        switch (form)
        {
            case Form.Jet:
                ShowStage(false, _jetSet.Last);
                Speed = Grounded ? SpeedMin : Mathf.Max(SpeedMin, _airVelocity.magnitude);
                _pitch = 0f;
                _roll = 0f;
                // Take up the attitude the jet already has rather than snapping
                // to identity: a transform mid-fight is a continuous move, and
                // on a space map there is no upright to snap to anyway.
                _orientation = transform.rotation;
                break;
            case Form.Robot:
                ShowStage(_tankSet.Exists, 0);
                SetChuteVisible(!Grounded);
                break;
            default:
                ShowStage(true, _tankSet.Last);
                _turret.enabled = true;
                _gun.muzzle = _turret.Muzzle;
                break;
        }
    }

    /// <summary>Collider per form: a fuselage, a standing figure, a hull.</summary>
    void ApplyFormFit(Form form)
    {
        switch (form)
        {
            case Form.Robot:
                _capsule.direction = 1;
                _capsule.radius = 0.8f;
                _capsule.height = 3.2f;
                break;
            case Form.Tank:
                _capsule.direction = 2;
                _capsule.radius = 1.1f;
                _capsule.height = 3.2f;
                break;
            default:
                _capsule.direction = 2;
                _capsule.radius = _jetCapsuleRadius;
                _capsule.height = _jetCapsuleHeight;
                break;
        }
        _capsule.center = Vector3.zero;
    }

    void SetChuteVisible(bool visible)
    {
        if (_chute == null || _chute.activeSelf == visible)
            return;
        if (!visible && _chute.activeSelf)
            VfxUtil.SpawnBurst(_chute.transform.position, new Color(0.8f, 0.9f, 1f), 8, 3f, 0.1f);
        _chute.SetActive(visible);
    }

    // ------------------------------------------------------------ the ceremony

    /// <summary>The intro fold (jet set, forward), driven by the mode.</summary>
    public void ShowMorph(float progress)
    {
        int last = _jetSet.Last;
        ShowStage(false, Mathf.Clamp(Mathf.FloorToInt(progress * (last + 1)), 0, last));
        if (!_morphBurst && progress >= 0.42f)
        {
            _morphBurst = true;
            VfxUtil.EnergyBurst(Center, MatchAnnouncer.TeamColor(Team), 1.1f);
        }
    }

    void ShowStage(bool tankSet, int index)
    {
        var set = tankSet ? _tankSet : _jetSet;
        if (!set.Exists)
            return;
        index = Mathf.Clamp(index, 0, set.Last);
        if (index == _shownStage && tankSet == _shownIsTank)
            return;

        var old = _shownIsTank ? _tankSet : _jetSet;
        if (old.Exists && _shownStage >= 0)
            foreach (var renderer in old.renderers[_shownStage])
                if (renderer != null)
                    renderer.enabled = false;

        _shownIsTank = tankSet;
        _shownStage = index;
        foreach (var renderer in set.renderers[index])
            if (renderer != null)
                renderer.enabled = _visible;
    }

    /// <summary>Jump straight to jet form — the intro's skip for stage-less
    /// pawns, and every respawn.</summary>
    public void ShowJet() => ShowStage(false, _jetSet.Exists ? _jetSet.Last : 0);

    public bool HasMorph => _jetSet.Exists && _jetSet.holders.Length > 1;

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        var set = _shownIsTank ? _tankSet : _jetSet;
        if (set.Exists && _shownStage >= 0)
            foreach (var renderer in set.renderers[_shownStage])
                if (renderer != null)
                    renderer.enabled = visible;
    }

    // -------------------------------------------------------------- every frame

    /// <summary>Track a world point — the free aim of the robot and tank
    /// forms, TankPawn's own contract. Jets ignore it; their gun is their nose.</summary>
    public void AimAt(Vector3 worldPoint)
    {
        _aimPoint = worldPoint;
        _hasAim = true;
    }

    void Update()
    {
        // Runs even while down: a wreck spiralling in should be the
        // smokiest thing in the sky, not the cleanest.
        UpdateDamageState();

        if (_down)
        {
            if (!Grounded)
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

        if (_flareCharges < FlareChargesMax && Time.time >= _flareRechargeAt)
        {
            _flareCharges++;
            _flareRechargeAt = Time.time + FlareRechargeSeconds;
        }

        if (Morphing)
        {
            Steer = Vector2.zero;
            Throttle = 0f;
            Firing = false;
            AdvanceMorph(dt);
            CoastFall(dt);
            return;
        }

        switch (CurrentForm)
        {
            case Form.Jet: FlyJet(dt); break;
            case Form.Robot: MoveRobot(dt); break;
            default: MoveTank(dt); break;
        }

        Separate(dt);
        PullTrigger();
        Firing = false;
        Sprint = false;
        _jumpQueued = false;
        _hasAim = false;
    }

    /// <summary>
    /// Keep pawns out of each other — TankPawn's push, lifted into the air
    /// for the squadron sizes. Everything here moves by writing its own
    /// transform, so Unity's collision response never runs, and a formation
    /// that could occupy one point would fly as a blob and read as one jet.
    /// </summary>
    void Separate(float dt)
    {
        Vector3 push = Vector3.zero;
        foreach (var other in Live)
        {
            if (other == null || other == this)
                continue;
            Vector3 gap = transform.position - other.transform.position;
            const float want = 4.5f;
            float distance = gap.magnitude;
            if (distance >= want || distance < 1e-4f)
                continue;
            push += gap / distance * (want - distance);
        }
        if (push.sqrMagnitude < 1e-6f)
            return;
        // Grounded forms hold their ride height; the deck owns their y.
        if (Grounded)
            push.y = 0f;
        transform.position += Vector3.ClampMagnitude(push, 3f) * (5f * dt);
    }

    /// <summary>Mid-fold physics: fall, gently, and land if the deck arrives
    /// before the new shape does.</summary>
    void CoastFall(float dt)
    {
        if (Grounded)
            return;
        _verticalSpeed = Mathf.MoveTowards(_verticalSpeed, -9f, 16f * dt);
        _airVelocity = Vector3.MoveTowards(_airVelocity, Vector3.zero, 6f * dt);
        transform.position += (_airVelocity + Vector3.up * _verticalSpeed) * dt;
        TouchDownCheck(1.2f);
    }

    void FlyJet(float dt)
    {
        Grounded = false;
        Vector2 steer = Vector2.ClampMagnitude(Steer, 1.4f);
        float throttle = Mathf.Clamp(Throttle, -1f, 1f);
        Steer = Vector2.zero;
        Throttle = 0f;

        steer = DogfightSky.SteerAssist(
            transform.position, transform.forward, transform.up, steer);

        if (DogfightSky.FreeOrientation)
        {
            // Space: integrate the stick as rotation RATES in the jet's own
            // frame. Absolute Euler angles cannot express this — pitch would
            // need clamping to stay out of gimbal lock, which is exactly the
            // clamp that stops a jet looping over the top of the planet or
            // rolling under the station's ring.
            //
            // Nothing levels itself here. There is no horizon to level to, and
            // an auto-level would fight the player every time they settled into
            // an orbit that is not the world's idea of upright.
            _orientation *= Quaternion.Euler(
                -steer.y * PitchRate * dt, steer.x * YawRate * dt, 0f);

            // Bank stays cosmetic and rides on top: a rotation about the jet's
            // OWN forward axis leaves that axis alone, so the flight direction
            // is untouched and the model still leans into its turns.
            _roll = Mathf.Lerp(_roll, -steer.x * BankDegrees, 1f - Mathf.Exp(-6f * dt));
            transform.rotation = _orientation * Quaternion.Euler(0f, 0f, _roll);
        }
        else
        {
            _yaw += steer.x * YawRate * dt;
            _pitch = Mathf.Clamp(_pitch + steer.y * PitchRate * dt, -PitchLimit, PitchLimit);
            if (Mathf.Abs(steer.y) < 0.05f)
                _pitch = Mathf.MoveTowards(_pitch, 0f, 14f * dt);
            _roll = Mathf.Lerp(_roll, -steer.x * BankDegrees, 1f - Mathf.Exp(-6f * dt));

            transform.rotation = Quaternion.Euler(-_pitch, _yaw, _roll);
        }

        float wanted = throttle >= 0f
            ? Mathf.Lerp(SpeedCruise, SpeedBoost, throttle)
            : Mathf.Lerp(SpeedCruise, SpeedMin, -throttle);
        Speed = Mathf.MoveTowards(Speed, wanted, Acceleration * dt);
        transform.position += transform.forward * (Speed * dt);
        transform.position = DogfightSky.KeepOffProps(transform.position, 1.2f);
        // The floor assist steers a jet up long before this matters; the
        // clamp is the net under the net (a jet re-folding off the deck
        // starts far below the assist's whole band). DeckUnder, not
        // GroundHeight: a jet threading beneath the station's walkway is over
        // the void net, and must not be snapped up through the furniture.
        float deck = DogfightSky.DeckUnder(transform.position);
        if (transform.position.y < deck + 1f)
            transform.position = new Vector3(transform.position.x, deck + 1f,
                transform.position.z);

        foreach (var trail in _trails)
            trail.emitting = true;
    }

    /// <summary>The robot: under canopy it drifts where it is steered and
    /// falls at a walk; on the deck it walks. Either way it faces its aim —
    /// the whole point of being the fragile form is the free gun.</summary>
    void MoveRobot(float dt)
    {
        Vector2 steer = Vector2.ClampMagnitude(Steer, 1f);
        Steer = Vector2.zero;
        Throttle = 0f;

        // Face the aim, not the walk — a gunner backpedals.
        Vector3 face = AimDirection;
        face.y = 0f;
        if (face.sqrMagnitude > 1e-4f)
        {
            float wantYaw = Quaternion.LookRotation(face.normalized, Vector3.up).eulerAngles.y;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, 220f * dt);
        }
        _pitch = Mathf.MoveTowards(_pitch, 0f, 90f * dt);
        _roll = Mathf.MoveTowards(_roll, 0f, 120f * dt);
        transform.rotation = Quaternion.Euler(-_pitch, _yaw, _roll);

        Vector3 drift = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(steer.x, 0f, steer.y);
        if (!Grounded)
        {
            _verticalSpeed = Mathf.MoveTowards(_verticalSpeed, -ChuteFallSpeed, 8f * dt);
            _airVelocity = Vector3.MoveTowards(_airVelocity, drift * ChuteDriftSpeed, 10f * dt);
            transform.position += (_airVelocity + Vector3.up * _verticalSpeed) * dt;
            SwayChute(dt);
            if (TouchDownCheck(1.7f))
                SetChuteVisible(false);
        }
        else
        {
            // The gunfight gait: walk, run when told to, hop on request.
            if (_jumpQueued && _hop <= 0f)
                _hopSpeed = JumpSpeed;
            if (_hop > 0f || _hopSpeed > 0f)
            {
                _hopSpeed -= JumpGravity * dt;
                _hop = Mathf.Max(0f, _hop + _hopSpeed * dt);
                if (_hop <= 0f)
                    _hopSpeed = 0f;
            }
            // The step is refused at deck edges and cliff-grade slopes —
            // walking off the station's walkway or down the planet's flank
            // is a fall, not a stroll.
            Vector3 before = transform.position;
            transform.position += drift * ((Sprint ? RunSpeed : WalkSpeed) * dt);
            transform.position = DogfightSky.HoldOnDeck(before, transform.position);
            transform.position = new Vector3(transform.position.x,
                DogfightSky.DeckUnder(transform.position) + 1.7f + _hop,
                transform.position.z);
        }
        transform.position = DogfightSky.KeepOffProps(transform.position, 0.9f, Grounded);
    }

    /// <summary>The tank: ballistic until the deck, then a hull that turns
    /// and drives while the turret argues with the sky on its own.</summary>
    void MoveTank(float dt)
    {
        Vector2 steer = Vector2.ClampMagnitude(Steer, 1f);
        Steer = Vector2.zero;
        Throttle = 0f;
        _tankDrive = steer.y;

        _pitch = Mathf.MoveTowards(_pitch, 0f, 120f * dt);
        _roll = Mathf.MoveTowards(_roll, 0f, 120f * dt);

        if (!Grounded)
        {
            // A falling tank is a promise, not a vehicle.
            _verticalSpeed = Mathf.Max(-TankFallTerminal,
                _verticalSpeed - TankFallAcceleration * dt);
            transform.position += (_airVelocity + Vector3.up * _verticalSpeed) * dt;
            _airVelocity = Vector3.MoveTowards(_airVelocity, Vector3.zero, 4f * dt);
            transform.rotation = Quaternion.Euler(-_pitch, _yaw, _roll);
            if (TouchDownCheck(1.1f))
            {
                VfxUtil.SpawnBurst(transform.position + Vector3.down * 0.6f,
                    new Color(0.7f, 0.65f, 0.55f), 18, 5f, 0.16f);
            }
            return;
        }

        _yaw += steer.x * TankTurnSpeed * dt;
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        // Same edge rule as the robot's walk: the deck is followed, ledges
        // and cliff-grade slopes are walls.
        Vector3 wasAt = transform.position;
        transform.position += transform.forward * (_tankDrive * TankDriveSpeed * dt);
        transform.position = DogfightSky.HoldOnDeck(wasAt, transform.position);
        transform.position = new Vector3(transform.position.x,
            DogfightSky.DeckUnder(transform.position) + 1.1f,
            transform.position.z);
        transform.position = DogfightSky.KeepOffProps(transform.position, 1.4f, true);

        // Hold the deck: the fence is for jets; a tank just runs out of road.
        Vector3 flat = new Vector3(transform.position.x, 0f, transform.position.z);
        float reach = DogfightSky.Radius - 6f;
        if (flat.magnitude > reach)
        {
            flat = flat.normalized * reach;
            transform.position = new Vector3(flat.x, transform.position.y, flat.z);
        }

        if (_hasAim)
            _turret.AimAt(_aimPoint);
    }

    /// <summary>
    /// The airframe tells its shield's story: past half damage a smoke
    /// trail, critical adds open flame. With no regeneration these only
    /// ever escalate — the respawn is what clears them. The smoke is a
    /// world-space emitter, so it hangs in the air behind the flight path
    /// the way every cartoon says it should.
    /// </summary>
    void UpdateDamageState()
    {
        float health = Shield != null && !IsDown ? Shield.Normalized : 0f;

        bool smoking = health < 0.55f || _down;
        if (smoking && _damageSmoke == null)
            _damageSmoke = BuildDamageSmoke();
        if (_damageSmoke != null)
        {
            // PER METRE, not per second: the trail is a line drawn along the
            // flight path — uniform at any speed, denser as the damage gets
            // worse, densest on the way down, and never a blob around a slow
            // or parked airframe.
            var emission = _damageSmoke.emission;
            emission.rateOverDistance = !smoking || WreckLanded ? 0f
                : _down ? 2.6f
                : Mathf.Lerp(2.2f, 0.8f, health / 0.55f);
        }

        bool critical = health < 0.22f;
        if ((critical || _down) && _burnFlame == null)
            _burnFlame = BuildBurnFlame();
        if (_burnFlame != null)
        {
            // The flame is a WORLD-SPACE emitter: flakes of fire are dropped
            // at the tail and left behind, so motion itself paints the burn
            // and nothing has to be aimed. It stays SHORT on purpose — the
            // long read belongs to the smoke; a long glowing ribbon looks
            // like a light effect, not a wound.
            var emission = _burnFlame.emission;
            emission.rateOverTime = _down && !WreckLanded ? 90f
                : WreckLanded ? 0f
                : critical ? 55f : 0f;
        }
    }

    /// <summary>Short-lived orange flakes, dropped and left like the smoke
    /// — at speed a fire streak, at rest a flicker. Kept under the
    /// bloom-whiteout line.</summary>
    ParticleSystem BuildBurnFlame()
    {
        var go = new GameObject("BurnFlame");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.1f, -_halfLength * 0.45f);

        var flame = go.AddComponent<ParticleSystem>();
        var main = flame.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        // Fewer, larger licks: each card carries the pack's flame sprite, so
        // the burn reads as fire rather than as a bead chain of glows.
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.85f, 0.6f, 0.95f), new Color(1f, 0.6f, 0.3f, 0.9f));
        main.maxParticles = 160;

        var emission = flame.emission;
        emission.rateOverTime = 0f;

        var shape = flame.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        var size = flame.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
        var velocity = flame.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Fire clings; it does not climb. The rise that made the burn read
        // as "up" is all but gone.
        velocity.y = new ParticleSystem.MinMaxCurve(0.12f);

        // The pack's burning-flame material, exactly as its own fire wears
        // it; the glow blob stays only as the no-pack fallback.
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = WarFx.FlameMaterial() != null
            ? WarFx.FlameMaterial()
            : VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture,
                new Color(1f, 0.5f, 0.18f), 2.0f);
        return flame;
    }

    ParticleSystem BuildDamageSmoke()
    {
        var go = new GameObject("DamageSmoke");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.1f, -_halfLength * 0.5f);

        var smoke = go.AddComponent<ParticleSystem>();
        var main = smoke.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Long-lived, near-motionless puffs: dropped in place and LEFT
        // there, so minutes of dogfight stay written across the sky as
        // lines. Mid-grey rather than near-black — black smoke on a navy
        // void is a trail nobody can see.
        main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.32f, 0.32f, 0.34f, 0.7f), new Color(0.45f, 0.44f, 0.46f, 0.6f));
        main.maxParticles = 700;

        var emission = smoke.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        var shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        // Real smoke tumbles: every puff born at its own angle, turning
        // slowly — identical upright sprites are what read as "dots".
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var spin = smoke.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        // Gentle swell only — ballooning puffs merge a line into a cloud.
        var size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.25f)));
        var velocity = smoke.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // All but still: a trail that climbs is a trail that stops being a
        // trail — this was the "smoke goes up" bug, twice.
        velocity.y = new ParticleSystem.MinMaxCurve(0.06f);

        var fade = smoke.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f),
            });
        fade.color = gradient;

        // The pack's own smoke sprite — a textured, soft-edged puff, which is
        // the difference between smoke and a row of grey dots. Fallback to a
        // hand-built blend of the project's puff texture if the pack is gone.
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var packPuff = WarFx.SmokePuffMaterial();
        if (packPuff != null)
        {
            renderer.material = packPuff;
        }
        else
        {
            var blend = Shader.Find("WFX/Alpha Blended (No Soft Particles)");
            Material material = blend != null
                ? new Material(blend)
                : VfxUtil.MakeAdditiveMaterial(VfxUtil.PuffTexture, new Color(0.1f, 0.1f, 0.1f), 0.5f);
            material.name = "DamageSmoke";
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", VfxUtil.PuffTexture);
            renderer.material = material;
        }
        return smoke;
    }

    /// <summary>Common landing test. True on the frame the deck arrives.
    /// DeckUnder, not GroundHeight: a faller that has already dropped past a
    /// floating walkway belongs to the net below it, never snapped up.</summary>
    bool TouchDownCheck(float rideHeight)
    {
        float deck = DogfightSky.DeckUnder(transform.position);
        if (transform.position.y > deck + rideHeight)
            return false;
        transform.position = new Vector3(transform.position.x, deck + rideHeight,
            transform.position.z);
        _verticalSpeed = 0f;
        _airVelocity = Vector3.zero;
        Grounded = true;
        return true;
    }

    void SwayChute(float dt)
    {
        if (_chute == null || !_chute.activeSelf)
            return;
        Vector3 lean = transform.InverseTransformVector(_airVelocity);
        _chute.transform.localRotation = Quaternion.Slerp(_chute.transform.localRotation,
            Quaternion.Euler(lean.z * 3.5f + Mathf.Sin(Time.time * 1.7f) * 4f, 0f,
                -lean.x * 3.5f + Mathf.Cos(Time.time * 1.3f) * 4f),
            1f - Mathf.Exp(-4f * dt));
    }

    // ------------------------------------------------------------------ the gun

    /// <summary>
    /// Fire this form's gun. Jets shoot along the nose through the assist
    /// cone; robots and tanks shoot at their aim point, with the same lead
    /// assist when an enemy sits near that line — and a tank's shot waits for
    /// its turret to actually arrive, because a hull firing across its own
    /// barrel reads as a bug (TankTurret's gate, TankTurret's reason).
    /// </summary>
    void PullTrigger()
    {
        AssistTarget = null;
        AssistEngaged = false;
        Vector3 origin = _gun.muzzle != null ? _gun.muzzle.position : Center;
        Vector3 direction;
        if (CurrentForm == Form.Jet)
        {
            direction = transform.forward;
        }
        else if (_hasAim)
        {
            direction = (_aimPoint - origin).normalized;
        }
        else
        {
            return;
        }

        // The chosen one first — a tapped lock is a promise the guns keep —
        // then whatever is merely nearest. Buildings hold still; pawns get
        // led.
        Vector3 assistCenter = Vector3.zero;
        Vector3 assistVelocity = Vector3.zero;
        bool haveAssist = false;
        if (FocusTarget != null)
        {
            var focusShield = FocusTarget.GetComponent<EnergyShield>();
            if (focusShield != null && !focusShield.IsDown)
            {
                var focusPawn = FocusTarget.GetComponent<JetPawn>();
                var focusTurret = FocusTarget.GetComponent<DogfightTurret>();
                Vector3 center = focusPawn != null ? focusPawn.Center
                    : focusTurret != null ? focusTurret.Center
                    : WeaponUtil.Center(focusShield);
                if ((center - origin).sqrMagnitude <= AssistRange * AssistRange)
                {
                    assistCenter = center;
                    assistVelocity = focusPawn != null ? focusPawn.Velocity : Vector3.zero;
                    AssistTarget = focusPawn;
                    haveAssist = true;
                }
            }
        }
        if (!haveAssist)
        {
            var target = NearestEnemy(transform.position, Team, AssistRange);
            if (target != null)
            {
                assistCenter = target.Center;
                assistVelocity = target.Velocity;
                AssistTarget = target;
                haveAssist = true;
            }
        }

        if (haveAssist)
        {
            Vector3 gap = assistCenter - origin;
            float seconds = gap.magnitude / GunBoltSpeed;
            Vector3 predicted = assistCenter + assistVelocity * seconds - origin;
            if (Vector3.Angle(direction, predicted) <= AssistDegrees)
            {
                direction = predicted.normalized;
                AssistEngaged = true;
            }
            else
            {
                AssistTarget = null;
            }
        }

        if (!Firing)
            return;
        if (CurrentForm == Form.Tank && _turret.HasTurret && !_turret.OnTarget)
            return;
        _gun.TryFire(direction);
    }

    // ---------------------------------------------------------------- ordnance

    /// <summary>Put a locked missile in the air — from any form; the tank's
    /// rise off the turret is the whole anti-air promise. Aimed at the lock
    /// itself so the homing only has to finish the job.</summary>
    public bool TryFireMissile(Transform lockRoot)
    {
        if (!MissileReady || IsDown || !FlightOn || Morphing || lockRoot == null)
            return false;
        _missileReadyAt = Time.time + MissileCooldown * missileCooldownScale;
        Vector3 from = CurrentForm == Form.Jet
            ? Center - transform.up * 0.45f + transform.forward * 1.0f
            : Center + Vector3.up * 1.4f;
        var shield = lockRoot.GetComponent<EnergyShield>();
        Vector3 at = shield != null ? WeaponUtil.Center(shield) : lockRoot.position;
        Vector3 direction = (at - from).sqrMagnitude > 1e-4f
            ? (at - from).normalized
            : AimDirection;
        DogfightMissile.Launch(from, direction, Team, transform, lockRoot,
            DogfightMissile.Flavor.Jet);
        // The launch is an EVENT: a spark burst off the rail, so a missile
        // leaving reads from the far side of the arena, not just up close.
        VfxUtil.SpawnBurst(from, MatchAnnouncer.TeamColor(Team), 12, 6f, 0.14f);
        return true;
    }

    /// <summary>Spend one burst of flares. Grounded forms eject upward —
    /// magnesium into the deck helps nobody.</summary>
    public bool TryPopFlares()
    {
        if (IsDown || !FlightOn || _flareCharges <= 0)
            return false;
        if (_flareCharges == FlareChargesMax)
            _flareRechargeAt = Time.time + FlareRechargeSeconds;
        _flareCharges--;
        DogfightFlare.Pop(this);
        return true;
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
        SetChuteVisible(false);
        _turret.enabled = false;
        // The kill shot is a POP, not the payoff — the payoff is the ground.
        VfxUtil.EnergyBurst(Center, MatchAnnouncer.TeamColor(Team), 1.4f);
        WarFx.Spawn(WarFx.Kind.Small, Center, 1.2f);
        OnWrecked?.Invoke(this);
        // A pawn killed standing on the deck has no fall to fall.
        if (Grounded)
            LandWreck();
    }

    /// <summary>A dying flier loses control and rides its wreck all the way
    /// down — the fall IS the shot. It ends where falls end.</summary>
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

        if (transform.position.y <= DogfightSky.DeckUnder(transform.position) + 0.9f)
            LandWreck();
    }

    /// <summary>
    /// The impact: the big explosion, a burning scar that outlives the
    /// wreck, and the airframe itself gone — what is left on the field is
    /// fire, which is the honest remainder of a jet. The mode hears about
    /// it and starts the respawn clock from HERE, so the camera always gets
    /// its crash before it gets its cut.
    /// </summary>
    void LandWreck()
    {
        if (WreckLanded)
            return;
        WreckLanded = true;
        Grounded = true;
        float rest = DogfightSky.DeckUnder(transform.position);
        // A crater burns only on real furniture: fire standing on the
        // station's energy net or hanging off the planet's flank reads as a
        // bug, not a crash site. The blast is honest anywhere.
        bool onDeck = !DogfightSky.OverVoid(transform.position)
            && rest >= DogfightSky.GroundHeight(transform.position) - 0.5f;
        transform.position = new Vector3(transform.position.x, rest + 0.9f,
            transform.position.z);

        VfxUtil.EnergyBurst(transform.position, MatchAnnouncer.TeamColor(Team), 2.4f);
        WarFx.Spawn(WarFx.Kind.Big, transform.position, 1.8f);
        VfxUtil.SpawnBurst(transform.position, new Color(0.75f, 0.7f, 0.6f), 26, 7f, 0.2f);
        if (onDeck)
            WarFx.SpawnFire(transform.position, 1.3f, 6f);

        SetVisible(false);
        foreach (var trail in _trails)
            trail.Clear();
        if (_damageSmoke != null)
        {
            var emission = _damageSmoke.emission;
            emission.rateOverDistance = 0f;
        }
        OnWreckLanded?.Invoke(this);
    }

    /// <summary>Back on the spawn ring, always as a jet: airframe whole, gun
    /// live, nose level, pocket full, tube warming.</summary>
    public void Respawn(Vector3 position, float yaw)
    {
        _down = false;
        WreckLanded = false;
        _yaw = yaw;
        _pitch = 0f;
        _roll = 0f;
        // A respawn is the one place a jet SHOULD be levelled: it arrives on a
        // spawn ring facing along the yaw it was given, whichever map it is on.
        _orientation = Quaternion.Euler(0f, yaw, 0f);
        SetVisible(true);
        if (_burnFlame != null)
        {
            var flameEmission = _burnFlame.emission;
            flameEmission.rateOverTime = 0f;
            _burnFlame.Clear();
        }
        if (_damageSmoke != null)
        {
            var emission = _damageSmoke.emission;
            emission.rateOverDistance = 0f;
            _damageSmoke.Clear();
        }
        _flareCharges = FlareChargesMax;
        _missileReadyAt = Time.time + 1.5f;
        _morphQueue.Clear();
        CurrentForm = Form.Jet;
        Grounded = false;
        _verticalSpeed = 0f;
        _airVelocity = Vector3.zero;
        _turret.enabled = false;
        _gun.muzzle = _muzzle;
        SetChuteVisible(false);
        ApplyFormFit(Form.Jet);
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

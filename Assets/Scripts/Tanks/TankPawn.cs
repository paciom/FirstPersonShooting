using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One fighter in TANK RAID: a tank, or a robot walking beside it.
///
/// WHY ONE CLASS FOR BOTH. The two chassis differ in exactly two places — how
/// they turn, and what carries the gun — and share everything else: the shield,
/// the arsenal, the separation, the fence, the wreck. Splitting them would
/// duplicate all of that to express a difference that fits in two branches.
///
///  * A TANK turns its hull onto the heading and drives along it, so pointing
///    the wrong way costs time. Its gun rides <see cref="TankTurret"/>, which
///    steers the turret of the stage-8 mesh independently of the hull.
///  * A WALKER turns to face what it is shooting at and moves sideways freely,
///    because legs can. Its gun rides the body.
///
/// EVERY PAWN LIVES AT THE SCENE ROOT. Bolts resolve their victim through
/// <c>hit.transform.root.GetComponent&lt;EnergyShield&gt;()</c>, so a pawn
/// parented under a stage root is a pawn that nothing in this project can shoot.
/// <see cref="All"/> and <see cref="DespawnAll"/> stand in for the parent object
/// the hierarchy would otherwise have given us — the same arrangement Commander
/// units and buildings run on.
///
/// Built inside an INACTIVE GameObject and switched on at the end. Awake runs at
/// AddComponent for an active object, and both <see cref="EnergyShield"/> (which
/// latches Current from maxShield) and <see cref="Weapon"/> (which latches its
/// team from the shield, and its muzzle) would run before any of it is set.
/// </summary>
public class TankPawn : MonoBehaviour
{
    public enum Chassis
    {
        /// <summary>Stage-8 hull with a turret that turns on its own.</summary>
        Tank,
        /// <summary>A roster walker, gun on the body.</summary>
        Walker,
        /// <summary>
        /// A Commander building, bolted to the ground. It never drives and never
        /// turns — see <see cref="OnTarget"/> for why its guns bear anyway.
        /// </summary>
        Structure,
    }

    // ------------------------------------------------------------- the registry

    // Plain static list, populated from OnEnable: a script recompile during Play
    // wipes statics, and a registry that only ever filled in Spawn would come
    // back from the reload empty while the field is still full of tanks.
    static readonly List<TankPawn> Live = new List<TankPawn>();

    public static IReadOnlyList<TankPawn> All => Live;

    /// <summary>Clear the field. The mode's one-shot teardown, as in Commander.</summary>
    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    /// <summary>Nearest live pawn on another team, or null.</summary>
    public static TankPawn NearestEnemy(Vector3 from, int teamId, float maxRange = float.MaxValue)
    {
        TankPawn best = null;
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

    // ----------------------------------------------------------------- the pawn

    /// <summary>Metres tall a tank is fitted to. Bigger than the FPS vehicle form
    /// (~1 m) because the camera in this mode is 18 m up and everything below
    /// about two metres reads as gravel.</summary>
    public const float TankHeight = 2.2f;

    /// <summary>And a walker beside it, in the same money.</summary>
    public const float WalkerHeight = 2.6f;

    /// <summary>
    /// An outpost's widest ground dimension. Big enough to be a landmark from
    /// the top of the screen and to be worth driving around, small enough that
    /// it never spans the strip — a structure that blocked the field would turn
    /// a driving game into a door.
    /// </summary>
    public const float StructureWidth = 9f;

    public Chassis Kind { get; private set; }
    public int Team { get; private set; }
    public EnergyShield Shield { get; private set; }
    public WeaponLoadout Loadout { get; private set; }

    /// <summary>Null on a walker, and on a tank whose mesh has no turret ring.</summary>
    public TankTurret Turret { get; private set; }

    /// <summary>Where the guns come out. Rides the barrel on a turreted tank.</summary>
    public Transform Muzzle { get; private set; }

    /// <summary>Body radius, for the field fence and for aiming.</summary>
    public float Radius { get; private set; } = 1.4f;

    // The hull's actual footprint, for contact. One circle has to choose
    // between the width (nose and tail hang out and clip through crates) and
    // the length (everything is held a hull-length off a tank it passes);
    // circles of the half-width, walked along the hull, trace the real shape.
    float _probeRadius = 1f;    // each circle: the hull's half-width
    float _probeReach;          // nose/tail circle offset from centre; 0 on a round body

    /// <summary>Aim height — the middle of the hull, not its feet.</summary>
    public Vector3 Center => transform.position + Vector3.up * (Radius * 0.8f);

    public bool IsDown => Shield == null || Shield.IsDown;

    /// <summary>
    /// Where to go this frame, as a world XZ heading; length 0–1 is the throttle.
    /// Written every frame by whatever is driving — the player's sticks, or
    /// <see cref="TankBrain"/> — and read here. Cleared after it is used, so a
    /// driver that stops writing stops the pawn rather than leaving it running.
    /// </summary>
    public Vector2 Drive { get; set; }

    /// <summary>Hold the trigger. Constant fire is the mode, so this is normally true.</summary>
    public bool Firing { get; set; }

    /// <summary>
    /// True on the hero alone: held at the field's trail line so the scroll can
    /// push it. Everyone else may fall off the bottom and be swept — see
    /// <see cref="TankField.Clamp"/> for why holding them there instead piles
    /// the army up at the bottom of the screen.
    /// </summary>
    public bool HeldAtTrail { get; set; }

    /// <summary>
    /// A temporary forward fence, in world z. The pawn drives at full power
    /// but cannot ADVANCE past this line while it is set — the AI pilot's way
    /// of waiting for a pinned recruit without touching anyone's speed.
    /// Infinity when clear (the default, and the player's permanent state).
    /// </summary>
    public float MaxAdvanceZ { get; set; } = float.PositiveInfinity;

    /// <summary>Top speed in metres per second.</summary>
    public float speed = 9f;

    /// <summary>Degrees per second the hull (tank) or the body (walker) comes about.</summary>
    public float turnSpeed = 150f;

    /// <summary>Fired when this pawn's shield empties — the mode counts wrecks and drops loot.</summary>
    public System.Action<TankPawn> OnWrecked;

    Vector3 _aimPoint;
    bool _hasAim;
    Transform _model;
    Weapon[] _guns;
    float[] _baseDamage;
    bool _wrecked;

    /// <summary>
    /// Remember what every gun was built to hit for. See <see cref="RestampGuns"/>.
    /// </summary>
    void BankBaseDamage()
    {
        if (_guns == null)
            return;
        _baseDamage = new float[_guns.Length];
        for (int i = 0; i < _guns.Length; i++)
            _baseDamage[i] = _guns[i] != null ? _guns[i].damage : 0f;
    }

    /// <summary>
    /// Rewrite every gun from its BUILD value times the boons taken so far.
    ///
    /// From base, never from the live number, because this is re-run after every
    /// capture and after every continue: multiplying in place would compound one
    /// outpost's reward once per respawn. Rate reaches the cannon only — it is
    /// the one gun in the rack with a cadence this code can name, and the eleven
    /// pods all count their own time in their own fields.
    /// </summary>
    public void RestampGuns(float damageScale, float rateScale)
    {
        if (_guns == null || _baseDamage == null)
            return;
        for (int i = 0; i < _guns.Length && i < _baseDamage.Length; i++)
            if (_guns[i] != null)
                _guns[i].damage = _baseDamage[i] * damageScale;

        if (_guns.Length > 0 && _guns[0] is LaserBlaster cannon)
            cannon.shotsPerSecond = TankArsenal.HeroCannonRate * rateScale;
    }

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
    /// Build a pawn and put it on the field.
    ///
    /// <paramref name="entry"/> supplies both bodies: its last transform stage is
    /// the tank (that is the mesh Tools/tankturret.py cut a turret out of), and
    /// its model prefab is the walker. A roster entry with no stages falls back
    /// to its separately generated vehicle model, and then to a plain hull — a
    /// missing model must never be able to blank a fighter off the field.
    /// </summary>
    public static TankPawn Spawn(RobotRoster.Entry entry, Chassis kind, int teamId,
        Vector3 position, float yaw, float maxShield, TankArsenal.Role role,
        string buildingKey = null)
    {
        var go = new GameObject($"TankPawn_{kind}_{teamId}");
        go.transform.position = new Vector3(position.x, 0f, position.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // THE BODY IS BUILT WHILE THE OBJECT IS LIVE. Fitting a generated model
        // means measuring it, the tank meshes are plain MeshRenderers, and a
        // MeshRenderer's world box on a deactivated hierarchy is not a number to
        // bet a tank's scale and ride height on.
        Color tint = MatchAnnouncer.TeamColor(teamId);
        float height = kind == Chassis.Tank ? TankHeight
            : kind == Chassis.Walker ? WalkerHeight : StructureWidth * 0.75f;
        var model = kind == Chassis.Tank ? BuildTankBody(go.transform, entry, tint, height)
            : kind == Chassis.Walker ? BuildWalkerBody(go.transform, entry, tint, height)
            : BuildStructureBody(go.transform, buildingKey, tint);
        var box = MeasureLocal(model);

        // From here the object goes dark; see the class note on why. SetActive
        // at the end runs every Awake at once, in an object that is by then
        // completely described.
        go.SetActive(false);

        var pawn = go.AddComponent<TankPawn>();
        pawn.Kind = kind;
        pawn.Team = teamId;
        pawn._model = model;
        // The FOOTPRINT, for keeping pawns off each other and out of the rocks.
        // The mean of the two ground extents rather than the larger: a tank is
        // twice as long as it is wide, and a circle drawn round its length would
        // hold it a whole hull-length off anything it drives past.
        pawn.Radius = Mathf.Max(0.9f, (box.extents.x + box.extents.z) * 0.5f);

        // What bullets hit, which is a different question — and answered off the
        // WIDTH, because a round hitbox sized to the length would swallow shots
        // that visibly went by the nose. On the DEFAULT layer, because that is
        // what every bolt in this project raycasts against, and non-trigger for
        // the same reason: WeaponUtil treats triggers as scenery to be ignored.
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.radius = Mathf.Max(0.75f, box.extents.x * 1.05f);
        capsule.height = Mathf.Max(height, capsule.radius * 2f);
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

        pawn._probeRadius = capsule.radius;
        // After the nose-forward turn the length lies along Z, so this is how
        // far past the centre circle the hull actually reaches.
        pawn._probeReach = Mathf.Max(0f, box.extents.z - capsule.radius);

        pawn.Shield = go.AddComponent<EnergyShield>();
        pawn.Shield.maxShield = maxShield;
        pawn.Shield.teamId = teamId;
        // No regeneration on the raiders — a mode where the answer to a tank is
        // "keep shooting it" must not have the tank healing between volleys. The
        // hero's own regen is set by the mode, which does want a little.
        pawn.Shield.regenDelay = 4f;
        pawn.Shield.regenPerSecond = 0f;

        pawn.Muzzle = BuildMuzzle(go.transform, box, kind);

        if (kind == Chassis.Tank)
        {
            var turret = go.AddComponent<TankTurret>();
            turret.turnSpeed = role == TankArsenal.Role.Hero ? 210f
                : role == TankArsenal.Role.Ally ? 170f : 120f;
            pawn.Turret = turret;
        }

        // Only the hero can ever pick up a pod, so only the hero carries a rack
        // to grant one out of. Bolts take the team's colour so the player can
        // tell their own fire from the army's.
        bool hero = role == TankArsenal.Role.Hero;
        pawn._guns = TankArsenal.Attach(go, pawn.Muzzle, go.transform, kind, role, tint);
        pawn.Loadout = go.AddComponent<WeaponLoadout>();
        pawn.Loadout.all = pawn._guns;
        pawn.Loadout.basicIndices = new[] { 0 };
        pawn.Loadout.grantDuration = TankArsenal.PodSeconds;

        go.SetActive(true);
        // Only now: the pod guns write their own damage in Awake, which has just
        // this moment run. See TankArsenal.AmplifyPods.
        if (hero)
            TankArsenal.AmplifyPods(pawn._guns);
        // Captured AFTER that, so the numbers banked here are the ones the pawn
        // is meant to fight at. TankBoons rescales from these on every capture
        // and every respawn; see RestampGuns.
        pawn.BankBaseDamage();
        pawn.Shield.OnDeRezzed += pawn.HandleWrecked;
        return pawn;
    }

    void OnDestroy()
    {
        if (Shield != null)
            Shield.OnDeRezzed -= HandleWrecked;
    }

    /// <summary>
    /// The stage-8 tank.
    ///
    /// TURNED BY ITS OWN BARREL, not by a convention. Every generated model in
    /// this project disagrees about which way is forward, and the usual answer —
    /// <see cref="VehicleSkin"/>'s "longest horizontal axis down +Z, then 180
    /// more" — is a knob somebody set by looking at a robot standing in an
    /// arena. This mode looks down on the hull from above and drives it, so
    /// backwards is not something that can hide here.
    ///
    /// The models answer the question themselves: a tank at rest points its gun
    /// over its nose, and Tools/tankturret.py wrote a marker at the barrel tip
    /// of every one of them. Turning that marker's direction onto +Z lands every
    /// chassis nose-forward whatever it shipped as — the same reasoning
    /// <see cref="TankTurret"/> uses to steer the turret without an opinion about
    /// the model. (Measured for the record: all nine hulls are longest along X
    /// with the barrel down -X, which the convention would have driven in
    /// reverse.) A model with no marker falls back to the convention.
    ///
    /// The turn is applied BEFORE anything is measured — a rotation after the
    /// fit moves the mesh off the centring solved for it.
    /// </summary>
    static Transform BuildTankBody(Transform root, RobotRoster.Entry entry, Color tint,
        float height)
    {
        GameObject prefab = null;
        float fallbackYaw = 180f;
        if (entry.HasStages)
            prefab = entry.transformStages[entry.transformStages.Length - 1];
        if (prefab == null)
        {
            // The separately generated vehicle models were never turned by the
            // stage pipeline, so they do not want its 180.
            prefab = entry.vehiclePrefab;
            fallbackYaw = 0f;
        }
        if (prefab == null)
            return BuildBlockBody(root, tint, height, 2.6f);

        var holder = new GameObject("Model").transform;
        holder.SetParent(root, false);
        var instance = Object.Instantiate(prefab, holder);
        instance.name = "Hull";

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Object.Destroy(holder.gameObject);
            return BuildBlockBody(root, tint, height, 2.6f);
        }

        Bounds raw = RobotFactory.MeasureWorldBounds(renderers);
        instance.transform.localRotation = Quaternion.Euler(0f, NoseYaw(holder, instance.transform,
            raw.size.x > raw.size.z ? 90f + fallbackYaw : fallbackYaw), 0f);
        raw = RobotFactory.MeasureWorldBounds(renderers);

        // Multiply, never replace — glTF roots carry their own unit scale.
        instance.transform.localScale *= height / Mathf.Max(0.01f, raw.size.y);

        Bounds fitted = RobotFactory.MeasureWorldBounds(renderers);
        instance.transform.position += new Vector3(
            root.position.x - fitted.center.x,
            root.position.y - fitted.min.y,
            root.position.z - fitted.center.z);

        TeamPaint.Apply(renderers, tint, 0, false, entry.paintAnchorHue);
        return holder;
    }

    /// <summary>
    /// The yaw that puts this hull's barrel down +Z in its holder's frame, or
    /// <paramref name="fallback"/> for a model that carries no barrel marker.
    /// Measured in holder space rather than world space so the answer does not
    /// depend on which way the pawn happened to be spawned facing.
    /// </summary>
    public static float NoseYaw(Transform holder, Transform instance, float fallback)
    {
        Transform pivot = null, tip = null;
        foreach (var node in instance.GetComponentsInChildren<Transform>(true))
        {
            // StartsWith, not equality: glTFast appends a number to make node
            // names unique within their parent when it has to.
            if (pivot == null && node.name.StartsWith(TankTurret.PivotName))
                pivot = node;
            else if (tip == null && node.name.StartsWith(TankTurret.TipName))
                tip = node;
        }
        if (pivot == null || tip == null)
            return fallback;

        Vector3 barrel = holder.InverseTransformPoint(tip.position)
                         - holder.InverseTransformPoint(pivot.position);
        barrel.y = 0f;
        if (barrel.sqrMagnitude < 1e-6f)
            return fallback;                    // a turret that points straight up
        return Vector3.SignedAngle(barrel, Vector3.forward, Vector3.up);
    }

    /// <summary>
    /// A roster walker at the mode's scale. Through RobotFactory so it lands
    /// with the same normalization, grounding and repaint every other mode's
    /// robots get — including the RobotLocomotion that animates its legs off the
    /// root's own position delta, which means it walks correctly under movement
    /// code that knows nothing about it.
    /// </summary>
    static Transform BuildWalkerBody(Transform root, RobotRoster.Entry entry, Color tint,
        float height)
    {
        if (entry.modelPrefab == null)
            return BuildBlockBody(root, tint, height, 1.1f);

        var holder = new GameObject("Model").transform;
        holder.SetParent(root, false);
        // InstantiateNormalized fits every robot in the roster to 1.6 units.
        holder.localScale = Vector3.one * (height / 1.6f);
        RobotFactory.InstantiateNormalized(entry.modelPrefab, holder, tint, entry.paintAnchorHue);
        return holder;
    }

    /// <summary>
    /// A Commander building, fitted so its widest ground dimension is
    /// <see cref="StructureWidth"/> and grounded where it stands.
    ///
    /// Scaled by FOOTPRINT rather than by height, unlike everything else here:
    /// the six differ wildly in how tall they are (a turret against a command
    /// center), and matching their heights would leave the small ones sprawling
    /// across the strip. Matching footprints keeps every outpost the same thing
    /// to drive around and lets the tall ones stay tall.
    ///
    /// Falls back to a lit block, so a missing GLB can never put an invisible
    /// wall on the field.
    /// </summary>
    static Transform BuildStructureBody(Transform root, string buildingKey, Color tint)
    {
        var holder = new GameObject("Model").transform;
        holder.SetParent(root, false);

        var prefab = Resources.Load<GameObject>($"Buildings/{buildingKey}-building");
        if (prefab == null)
            return BuildBlockBody(root, tint, StructureWidth * 0.7f, 1f);

        var instance = Object.Instantiate(prefab, holder);
        instance.name = "Structure";
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Object.Destroy(holder.gameObject);
            return BuildBlockBody(root, tint, StructureWidth * 0.7f, 1f);
        }

        Bounds raw = RobotFactory.MeasureWorldBounds(renderers);
        float widest = Mathf.Max(raw.size.x, raw.size.z, 0.01f);
        // Multiply, never replace — glTF roots carry their own unit scale.
        instance.transform.localScale *= StructureWidth / widest;

        Bounds fitted = RobotFactory.MeasureWorldBounds(renderers);
        instance.transform.position += new Vector3(
            root.position.x - fitted.center.x,
            root.position.y - fitted.min.y,
            root.position.z - fitted.center.z);

        // A ring of the owner's colour underneath. The Meshy texture carries the
        // building's identity but says nothing about whose it is — the same
        // reason Commander rings its own buildings.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "TeamRing";
        Object.Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(holder, false);
        ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        ring.transform.localScale = new Vector3(StructureWidth * 1.15f, 0.02f, StructureWidth * 1.15f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Emissive(
            $"tank-outpost-ring-{ColorUtility.ToHtmlStringRGB(tint)}", tint, 1.6f);

        return holder;
    }

    /// <summary>Last resort: a lit block, so a roster with no models still fights.</summary>
    static Transform BuildBlockBody(Transform root, Color tint, float height, float lengthRatio)
    {
        var holder = new GameObject("Model").transform;
        holder.SetParent(root, false);
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Hull";
        Object.Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(holder, false);
        box.transform.localScale = new Vector3(height * 0.9f, height, height * lengthRatio);
        box.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        box.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit($"tank-block-{ColorUtility.ToHtmlStringRGB(tint)}", tint * 0.7f, 0.4f);
        return holder;
    }

    /// <summary>
    /// The stable muzzle.
    ///
    /// Named for <see cref="TankTurret.AnchorName"/> and built up front rather
    /// than left to the turret: the guns are attached before the first frame and
    /// need something to point at, and the turret would otherwise create a
    /// second one behind their backs. On a turreted tank it is overwritten every
    /// LateUpdate to ride the barrel tip; on a walker — or a tank whose mesh has
    /// no turret ring — it stays here, at the front of the body, and follows the
    /// pawn because it is parented to it.
    /// </summary>
    static Transform BuildMuzzle(Transform root, Bounds body, Chassis kind)
    {
        var muzzle = new GameObject(TankTurret.AnchorName).transform;
        muzzle.SetParent(root, false);
        muzzle.localPosition = new Vector3(
            0f,
            Mathf.Max(0.6f, body.size.y * (kind == Chassis.Tank ? 0.7f : 0.62f)),
            body.extents.z + 0.35f);
        return muzzle;
    }

    static Bounds MeasureLocal(Transform model)
    {
        if (model == null)
            return new Bounds(Vector3.zero, new Vector3(2f, 2f, 3f));
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, new Vector3(2f, 2f, 3f));
        var world = RobotFactory.MeasureWorldBounds(renderers);
        return new Bounds(world.center - model.root.position, world.size);
    }

    // ------------------------------------------------------------------- aiming

    /// <summary>Point the gun at a world position, for this frame.</summary>
    public void AimAt(Vector3 worldPoint)
    {
        _aimPoint = worldPoint;
        _hasAim = true;
    }

    /// <summary>Point the gun along a world direction, for callers holding a stick.</summary>
    public void AimAlong(Vector3 direction)
    {
        if (direction.sqrMagnitude > 1e-6f)
            AimAt(transform.position + direction.normalized * 60f);
    }

    /// <summary>
    /// Barrel close enough to the aim to be worth firing. A tank that shoots
    /// ninety degrees off where its gun is pointing reads as a bug, and the
    /// turret is deliberately slow enough for that gap to exist.
    /// </summary>
    public bool OnTarget
    {
        get
        {
            if (!_hasAim)
                return false;
            // A bunker has embrasures on every side. It does not turn, so gating
            // its guns on a facing it can never change would be gating them shut.
            if (Kind == Chassis.Structure)
                return true;
            Vector3 wanted = Flat(_aimPoint - transform.position);
            if (wanted.sqrMagnitude < 1e-6f)
                return false;
            Vector3 barrel = Turret != null && Turret.HasTurret
                ? Flat(Muzzle.forward)
                : Flat(transform.forward);
            if (barrel.sqrMagnitude < 1e-6f)
                return false;
            return Vector3.Angle(barrel, wanted) <= 14f;
        }
    }

    // -------------------------------------------------------------- every frame

    void Update()
    {
        if (_wrecked)
            return;

        float dt = Time.deltaTime;
        Vector2 drive = Vector2.ClampMagnitude(Drive, 1f);
        Drive = Vector2.zero;                       // see the Drive doc comment

        // A structure is bolted down: no drive, no turn, no shove, and no fence
        // — an outpost pushed off a rock or nudged out of another tank's way
        // would be a building sliding across the battlefield. It still aims and
        // still fires; that is all it does.
        if (Kind != Chassis.Structure)
        {
            var heading = new Vector3(drive.x, 0f, drive.y);
            if (Kind == Chassis.Tank)
                DriveHull(heading, dt);
            else
                DriveLegs(heading, dt);

            transform.position = TankField.Clamp(transform.position, Radius, HeldAtTrail);
            if (transform.position.z > MaxAdvanceZ)
            {
                var held = transform.position;
                held.z = MaxAdvanceZ;
                transform.position = held;
            }
            Separate();
            AvoidScenery();
            // Flat ground: the field is a deck at y = 0 and nothing in this mode
            // climbs, so there is no surface query worth making.
            var grounded = transform.position;
            grounded.y = 0f;
            transform.position = grounded;
        }

        PointTheGun();
        PullTrigger();
        _hasAim = false;
    }

    /// <summary>
    /// A hull comes about, then drives. Speed is scaled by how far off the
    /// heading it still is, so a tank asked to reverse turns almost on the spot
    /// instead of skating there sideways — the whole reason the left stick is
    /// worth having a right stick beside it.
    /// </summary>
    void DriveHull(Vector3 heading, float dt)
    {
        if (heading.sqrMagnitude < 1e-4f)
            return;

        Quaternion wanted = Quaternion.LookRotation(heading.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * dt);

        float alignment = Vector3.Dot(transform.forward, heading.normalized);
        if (alignment <= 0f)
            return;
        // Squared, so a tank three-quarters turned around barely creeps.
        transform.position += transform.forward * (speed * heading.magnitude * alignment * alignment * dt);
    }

    /// <summary>
    /// Legs go where they are sent and face where they are shooting. Strafing is
    /// what makes the walkers read as a different threat from the tanks rather
    /// than as small ones.
    /// </summary>
    void DriveLegs(Vector3 heading, float dt)
    {
        if (heading.sqrMagnitude > 1e-4f)
            transform.position += heading.normalized * (speed * heading.magnitude * dt);

        Vector3 face = _hasAim ? Flat(_aimPoint - transform.position) : heading;
        if (face.sqrMagnitude < 1e-4f)
            return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(face.normalized, Vector3.up), turnSpeed * dt);
    }

    void PointTheGun()
    {
        if (Turret == null)
            return;
        // With no aim this frame the turret walks back to dead ahead by itself,
        // which is where a tank carries its gun when nothing is worth pointing
        // it at. Nothing to do here but stay out of its way.
        if (_hasAim)
            Turret.AimAt(_aimPoint);
    }

    /// <summary>
    /// Shoot whatever is in the top slot: the pod weapon while one is granted,
    /// the cannon otherwise. One gun rather than the whole rack, because a pod
    /// should read as an upgrade and not as an extra barrel nobody noticed.
    /// </summary>
    void PullTrigger()
    {
        if (!Firing || !_hasAim || IsDown || !OnTarget)
            return;

        var gun = CurrentGun;
        if (gun == null)
            return;
        Vector3 direction = _aimPoint - Muzzle.position;
        if (direction.sqrMagnitude < 1e-4f)
            return;
        gun.TryFire(direction.normalized);
    }

    /// <summary>The gun the pawn is actually shooting — last slot wins.</summary>
    public Weapon CurrentGun
    {
        get
        {
            if (Loadout == null)
                return _guns != null && _guns.Length > 0 ? _guns[0] : null;
            var available = Loadout.Available;
            return available.Length > 0 ? available[available.Length - 1] : null;
        }
    }

    static readonly Vector3[] MyCircles = new Vector3[3];
    static readonly Vector3[] TheirCircles = new Vector3[3];

    /// <summary>
    /// This hull's footprint circles, written into <paramref name="into"/>:
    /// the centre, plus nose and tail for a body meaningfully longer than it
    /// is wide. Returns how many were written.
    /// </summary>
    int FootprintCircles(Vector3[] into)
    {
        into[0] = transform.position;
        if (_probeReach < 0.05f)
            return 1;
        Vector3 along = transform.forward * _probeReach;
        into[1] = transform.position + along;
        into[2] = transform.position - along;
        return 3;
    }

    /// <summary>
    /// Keep pawns out of each other. Everything here moves by writing its own
    /// transform, so Unity's collision response never runs and two tanks
    /// driving into the same square would simply occupy it.
    ///
    /// Resolved POSITIONALLY, not as a spring: the deepest circle-pair overlap
    /// with each neighbour is undone outright — the WHOLE depth, by whichever
    /// pawn measures it. Not split half-and-half: Updates run sequentially, so
    /// the first pawn to look clears the contact and the second finds nothing
    /// left to do. A split sounds fairer but leaves a standing half-overlap
    /// whenever the neighbour cannot honour its share — pinned against the
    /// fence (whose clamp runs before this and undoes any yield), or pressed
    /// from behind in a column jam. The old velocity-style push was worse
    /// still: tuned soft enough that anything driving at full speed simply
    /// out-ran it. Capped per frame so a pile that spawns overlapped spreads
    /// over a few frames instead of detonating.
    /// </summary>
    void Separate()
    {
        Vector3 resolve = Vector3.zero;
        int mine = FootprintCircles(MyCircles);
        foreach (var other in Live)
        {
            // A wrecked pawn still in the list is the hero's faded-out body
            // waiting on a continue — not something live traffic should be
            // shoved around by.
            if (other == null || other == this || other._wrecked)
                continue;
            int theirs = other.FootprintCircles(TheirCircles);
            float want = _probeRadius + other._probeRadius;
            float deepest = 0f;
            Vector3 direction = Vector3.zero;
            for (int i = 0; i < mine; i++)
                for (int j = 0; j < theirs; j++)
                {
                    Vector3 gap = MyCircles[i] - TheirCircles[j];
                    gap.y = 0f;
                    float distance = gap.magnitude;
                    if (distance >= want)
                        continue;
                    float depth = want - distance;
                    if (depth <= deepest)
                        continue;
                    deepest = depth;
                    if (distance > 1e-4f)
                        direction = gap / distance;
                    else
                    {
                        // Dead centre on top of each other: any flat way out
                        // beats none, and sideways clears a head-on fastest.
                        Vector3 apart = Flat(transform.position - other.transform.position);
                        direction = apart.sqrMagnitude > 1e-6f ? apart.normalized
                            : Flat(transform.right).normalized;
                    }
                }
            if (deepest <= 0f)
                continue;
            resolve += direction * deepest;
        }
        if (resolve.sqrMagnitude > 1e-8f)
            transform.position += Vector3.ClampMagnitude(resolve, 1.5f);
    }

    static readonly Collider[] SceneryProbe = new Collider[16];

    /// <summary>
    /// Stay out of the rocks.
    ///
    /// Every pawn moves by writing its own transform, so Unity's own collision
    /// response never runs and a tank would otherwise drive straight through the
    /// boulders its shells are stopping against — cover you can be shot through
    /// but not driven through is a rule nobody can learn.
    ///
    /// Pushed out along the shortest way out, measured from the nearest point on
    /// the obstacle. That measurement gives no direction at all for a pawn whose
    /// centre is already INSIDE something, which is why this runs every frame
    /// from first contact: a hull that is never allowed one frame of overlap can
    /// never reach the state with no answer.
    /// </summary>
    void AvoidScenery()
    {
        // One overlap query per footprint circle, so the nose and tail meet a
        // crate where the mesh does instead of sailing through until the hull's
        // centre arrives. Circles are re-derived each pass because resolving
        // one contact moves the pawn every other circle hangs off.
        for (int c = 0; c < 3; c++)
        {
            if (c >= FootprintCircles(MyCircles))
                break;
            Vector3 probe = MyCircles[c] + Vector3.up * 0.6f;
            int count = Physics.OverlapSphereNonAlloc(probe, _probeRadius, SceneryProbe, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = SceneryProbe[i];
                if (collider == null || collider.transform.root == transform)
                    continue;
                if (collider.GetComponentInParent<TankPawn>() != null)
                    continue;                   // another pawn: Separate owns that
                Vector3 away = probe - collider.ClosestPoint(probe);
                away.y = 0f;
                float distance = away.magnitude;
                if (distance < 1e-4f || distance >= _probeRadius)
                    continue;
                Vector3 resolve = away / distance * (_probeRadius - distance);

                // Street blocks split the contact instead of winning it: the
                // block takes its material's share by moving, the hull takes the
                // rest. A crate parts around a driving tank, masonry grinds
                // aside, stone still mostly says no — and berms, spires and
                // outposts keep saying it entirely.
                var block = collider.GetComponentInParent<TankBlock>();
                if (block != null)
                {
                    block.Push(-resolve * block.PushShare);
                    resolve *= 1f - block.PushShare;
                }
                transform.position += resolve;
                probe += resolve;
            }
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ------------------------------------------------------------------- death

    void HandleWrecked()
    {
        if (_wrecked)
            return;
        _wrecked = true;
        Firing = false;
        OnWrecked?.Invoke(this);
    }

    /// <summary>
    /// Back on the field: shield full, guns live, and the pawn is a target
    /// again. The hero's continue; nothing else in the mode uses it.
    /// </summary>
    public void Revive()
    {
        _wrecked = false;
        if (Shield != null)
            Shield.Rematerialize();
    }

    /// <summary>Blow up and leave the field. The mode calls this on every raider it kills.</summary>
    public void Wreck()
    {
        Color color = MatchAnnouncer.TeamColor(Team);
        if (Kind == Chassis.Structure)
        {
            // A building comes down in more than one bang, spread across its
            // footprint: one burst at the centre of a nine-metre structure reads
            // as something small exploding behind it.
            VfxUtil.Explosion(Center, color, 2.4f);
            for (int i = 0; i < 5; i++)
                VfxUtil.Explosion(transform.position + new Vector3(
                    Random.Range(-1f, 1f) * Radius, Random.Range(0.4f, 2.2f),
                    Random.Range(-1f, 1f) * Radius), color, Random.Range(0.9f, 1.5f));
        }
        else
        {
            VfxUtil.Explosion(Center, color, Kind == Chassis.Tank ? 1.3f : 0.9f);
        }
        Destroy(gameObject);
    }

    /// <summary>Fade the body out over a beat — the hero, on the way to a continue.</summary>
    public void SetVisible(bool visible)
    {
        if (_model == null)
            return;
        foreach (var renderer in _model.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
    }
}

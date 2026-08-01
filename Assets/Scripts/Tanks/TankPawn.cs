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

    public Chassis Kind { get; private set; }
    public int Team { get; private set; }
    public EnergyShield Shield { get; private set; }
    public WeaponLoadout Loadout { get; private set; }

    /// <summary>Null on a walker, and on a tank whose mesh has no turret ring.</summary>
    public TankTurret Turret { get; private set; }

    /// <summary>Where the guns come out. Rides the barrel on a turreted tank.</summary>
    public Transform Muzzle { get; private set; }

    /// <summary>Body radius, for keeping pawns out of each other and for aiming.</summary>
    public float Radius { get; private set; } = 1.4f;

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
    bool _wrecked;

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
        Vector3 position, float yaw, float maxShield)
    {
        var go = new GameObject($"TankPawn_{kind}_{teamId}");
        go.transform.position = new Vector3(position.x, 0f, position.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // THE BODY IS BUILT WHILE THE OBJECT IS LIVE. Fitting a generated model
        // means measuring it, the tank meshes are plain MeshRenderers, and a
        // MeshRenderer's world box on a deactivated hierarchy is not a number to
        // bet a tank's scale and ride height on.
        Color tint = MatchAnnouncer.TeamColor(teamId);
        float height = kind == Chassis.Tank ? TankHeight : WalkerHeight;
        var model = kind == Chassis.Tank
            ? BuildTankBody(go.transform, entry, tint, height)
            : BuildWalkerBody(go.transform, entry, tint, height);
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
            turret.turnSpeed = teamId == 0 ? 210f : 120f;
            pawn.Turret = turret;
        }

        // Only the hero can ever pick up a pod, so only the hero carries a rack
        // to grant one out of. Bolts take the team's colour so the player can
        // tell their own fire from the army's.
        bool hero = teamId == 0;
        pawn._guns = TankArsenal.Attach(go, pawn.Muzzle, go.transform, kind, hero, tint);
        pawn.Loadout = go.AddComponent<WeaponLoadout>();
        pawn.Loadout.all = pawn._guns;
        pawn.Loadout.basicIndices = new[] { 0 };
        pawn.Loadout.grantDuration = TankArsenal.PodSeconds;

        go.SetActive(true);
        // Only now: the pod guns write their own damage in Awake, which has just
        // this moment run. See TankArsenal.AmplifyPods.
        if (hero)
            TankArsenal.AmplifyPods(pawn._guns);
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

        var heading = new Vector3(drive.x, 0f, drive.y);
        if (Kind == Chassis.Tank)
            DriveHull(heading, dt);
        else
            DriveLegs(heading, dt);

        transform.position = TankField.Clamp(transform.position, Radius);
        Separate(dt);
        AvoidScenery();
        // Flat ground: the field is a deck at y = 0 and nothing in this mode
        // climbs, so there is no surface query worth making.
        var grounded = transform.position;
        grounded.y = 0f;
        transform.position = grounded;

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

    /// <summary>
    /// Keep pawns out of each other. A push, not a collision: everything here
    /// moves by writing its own transform, so two tanks driving into the same
    /// square would simply occupy it. Cheap enough to run unconditionally with a
    /// couple of dozen pawns on the field.
    /// </summary>
    void Separate(float dt)
    {
        Vector3 push = Vector3.zero;
        foreach (var other in Live)
        {
            if (other == null || other == this)
                continue;
            Vector3 gap = transform.position - other.transform.position;
            gap.y = 0f;
            float want = Radius + other.Radius;
            float distance = gap.magnitude;
            if (distance >= want || distance < 1e-4f)
                continue;
            push += gap / distance * (want - distance);
        }
        if (push.sqrMagnitude > 1e-6f)
            transform.position += Vector3.ClampMagnitude(push, 3f) * (6f * dt);
    }

    static readonly Collider[] SceneryProbe = new Collider[12];

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
        Vector3 probe = transform.position + Vector3.up * 0.6f;
        int count = Physics.OverlapSphereNonAlloc(probe, Radius, SceneryProbe, ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = SceneryProbe[i];
            if (collider == null || collider.transform.root == transform)
                continue;
            if (collider.GetComponentInParent<TankPawn>() != null)
                continue;                       // another pawn: Separate owns that
            Vector3 away = probe - collider.ClosestPoint(probe);
            away.y = 0f;
            float distance = away.magnitude;
            if (distance < 1e-4f)
                continue;
            transform.position += away / distance * (Radius - distance);
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
        VfxUtil.Explosion(Center, MatchAnnouncer.TeamColor(Team), Kind == Chassis.Tank ? 1.3f : 0.9f);
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

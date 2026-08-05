using UnityEngine;

/// <summary>
/// Turns the tank's turret toward whatever the character is shooting at, while
/// the hull keeps facing wherever it is driving.
///
/// The two halves come out of the model itself: Tools/tankturret.py cuts the
/// vehicle mesh at the turret ring and rebuilds it as a Hull node plus a
/// TurretPivot node with a TurretMuzzle marker at the barrel tip. Everything
/// here hangs off finding those by name.
///
/// WHY IT AIMS BY THE BARREL RATHER THAN BY AN ANGLE. Nothing in this project
/// agrees on which way a generated model faces: the meshes come out of
/// image-to-3D nose-down whichever axis they please, VehicleSkin yaws them onto
/// +Z by measuring, and stages take another 180 on top. Rather than add a tenth
/// convention, the turret is steered like a servo — take the direction the
/// barrel currently points, take the direction it should point, and close the
/// gap by up to <see cref="turnSpeed"/> degrees this frame. Both directions are
/// read from live transforms, so the loop is right whatever the model shipped as.
///
/// Two axes, on two parts. The turret yaws on its ring; the GUN rises on its
/// trunnion, cut free of the turret by the same tool so it can elevate inside
/// its mantlet the way a real one does. Panther is the exception the rig reports
/// rather than hides: its turret is a solid visored wedge with nothing to
/// separate, so it tips as one block, and only as far as the deck allows.
///
/// <see cref="Muzzle"/> is a stable transform on the character that rides the
/// barrel tip, so the vehicle's guns can fire out of the barrel without holding
/// a reference into a model that a reskin will destroy.
///
/// Runs only while its character is actually a tank — TransformMode switches it
/// on as a fold lands. A robot has no turret to turn, and looking for one every
/// frame on every character in the arena is a search that can never succeed.
/// </summary>
public class TankTurret : MonoBehaviour
{
    /// <summary>Node names written by Tools/tankturret.py.</summary>
    public const string PivotName = "TurretPivot";
    public const string GunName = "GunPivot";
    public const string TipName = "TurretMuzzle";

    /// <summary>Name of the stable muzzle on the character; see <see cref="Muzzle"/>.</summary>
    public const string AnchorName = "TurretMuzzleAnchor";

    [Tooltip("How fast the turret slews, in degrees per second. Fast enough to " +
             "answer a target that moves, slow enough to be worth watching.")]
    public float turnSpeed = 160f;

    [Tooltip("How fast the gun rises and falls, in degrees per second.")]
    public float elevationSpeed = 90f;

    [Tooltip("How far the gun can rise. High enough to answer a jet overhead, " +
             "which is what a tank in Dogfight spends its life doing — the " +
             "breeches were rendered at 80 and still sit inside their mantlets.")]
    [Range(0f, 85f)] public float maxElevation = 75f;

    [Tooltip("How far it can depress — a little, for a target down a slope.")]
    [Range(0f, 30f)] public float maxDepression = 8f;

    /// <summary>
    /// Ceiling for a turret with no separable gun, which has to tip the whole
    /// block to aim up. Past this its back half is inside the deck: Panther's
    /// turret is a solid wedge and there is nothing on it to elevate alone.
    /// The SHOTS still take the full angle — the gun is an emitter, not a tube.
    /// </summary>
    const float BlockElevation = 14f;

    [Tooltip("How close the barrel has to be before the guns are allowed to " +
             "fire — a tank that shoots sideways reads as a bug.")]
    public float onTargetDegrees = 10f;

    /// <summary>Barrel tip, as a transform that outlives the model it came from.</summary>
    public Transform Muzzle { get { EnsureAnchor(); return _anchor; } }

    /// <summary>False for a robot whose vehicle form is one unrigged mesh.</summary>
    public bool HasTurret => ResolvePivot() != null;

    /// <summary>
    /// Barrel roughly on the aim. True with no turret at all, so a fire gate
    /// written against this never silences a robot that has no turret to wait for.
    /// </summary>
    public bool OnTarget => !HasTurret || _offTarget <= onTargetDegrees;

    /// <summary>Seconds between attempts to find a turret that isn't there.</summary>
    const float SearchInterval = 0.25f;

    Transform _pivot, _gun, _tip, _anchor;
    Quaternion _pivotRest = Quaternion.identity;
    Quaternion _gunRest = Quaternion.identity;
    float _yaw;
    Vector3 _aimPoint;
    bool _hasAim;
    bool _warnedNoTip;
    float _offTarget;
    float _elevation;
    float _nextSearch;

    /// <summary>The part that rises: the gun where the rig found one, the whole
    /// turret where it did not.</summary>
    Transform Riser => _gun != null ? _gun : _pivot;

    /// <summary>How far this tank is allowed to raise its gun.</summary>
    float ElevationCeiling => _gun != null ? maxElevation : Mathf.Min(maxElevation, BlockElevation);

    /// <summary>Track a world position — the target's chest, usually.</summary>
    public void AimAt(Vector3 worldPoint)
    {
        _aimPoint = worldPoint;
        _hasAim = true;
    }

    /// <summary>Track a direction, for callers holding an aim vector rather than a target.</summary>
    public void AimAlong(Vector3 direction)
    {
        if (direction.sqrMagnitude > 1e-6f)
            AimAt(transform.position + direction.normalized * 50f);
    }

    /// <summary>A fold has just landed. Look for the turret straight away
    /// rather than waiting out a throttle that was counting down against the
    /// previous one, and report the gun as off target until it has been
    /// measured — a fire gate must not open on a turret nothing has aimed yet.
    /// </summary>
    void OnEnable()
    {
        _nextSearch = 0f;
        _offTarget = 180f;
    }

    /// <summary>Centre it on the way out, so the tank arrives out of the next
    /// fold with its gun forward rather than wherever the last fight left it.
    /// </summary>
    void OnDisable()
    {
        _yaw = 0f;
        _elevation = 0f;
        if (_pivot != null)
            _pivot.localRotation = _pivotRest;
        if (_gun != null)
            _gun.localRotation = _gunRest;
    }

    /// <summary>
    /// After the Animator and after the brains, so the turret is turned against
    /// the pose the hull actually ended the frame in.
    /// </summary>
    void LateUpdate()
    {
        var pivot = ResolvePivot();
        bool aiming = _hasAim;
        _hasAim = false;

        if (pivot == null)
            return;

        // Everything is measured against the DECK, never against the world.
        // A turret is bolted to a ring: it can only spin in the plane of the
        // hull it sits on, whatever that hull is doing. Turning it about the
        // world's up instead works right up until the hull stops being level —
        // and then the turret rolls off its own mounting, which is exactly what
        // a banking jet in Dogfight showed.
        Vector3 up = Deck(pivot).up;
        // With no target this frame the gun walks back to dead ahead and level,
        // which is how a tank carries it when nothing is worth pointing it at.
        Vector3 toAim = aiming ? _aimPoint - Riser.position : transform.forward;

        Vector3 barrel = Vector3.ProjectOnPlane(RawBarrel(), up);
        Vector3 wanted = Vector3.ProjectOnPlane(toAim, up);
        if (barrel.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
            return;

        // The turret's own up IS the deck's up and stays the deck's up, so a
        // bearing held as a plain angle about it can never tilt the turret off
        // its ring however long the fight runs or however the hull is flying.
        // Kept as an angle rather than nudged into the transform because the
        // gun's elevation is composed on top, and that has to start from a
        // known pose every frame.
        float delta = Vector3.SignedAngle(barrel, wanted, up);
        _yaw += Mathf.Clamp(delta, -turnSpeed * Time.deltaTime, turnSpeed * Time.deltaTime);
        pivot.localRotation = _pivotRest * Quaternion.Euler(0f, _yaw, 0f);

        float rise = Elevate(up, toAim);

        // Off target counts BOTH axes: a gun that has the bearing but is still
        // coming up is not on the jet yet.
        _offTarget = Mathf.Sqrt(delta * delta + rise * rise);

        RideBarrel();
    }

    /// <summary>
    /// Raise or lower the gun toward the aim, and report how far short it still
    /// is. Returns degrees remaining, signed away from zero.
    ///
    /// APPLIED FRESH FROM REST EVERY FRAME rather than nudged. The elevation
    /// axis is the trunnion — horizontal, across the barrel — and that axis
    /// swings round with the turret, so an incremental rotation would be
    /// composing this frame's tilt onto last frame's about a DIFFERENT axis,
    /// which is precisely the drift that rolled the turret off its ring in
    /// Dogfight. One stored angle, one rotation, no memory.
    /// </summary>
    float Elevate(Vector3 up, Vector3 toAim)
    {
        var riser = Riser;
        if (riser == null)
            return 0f;

        // The gun starts from its own rest; the turret has just been rebuilt
        // from its bearing, so it is already at a known pose.
        if (riser == _gun)
            riser.localRotation = _gunRest;

        Vector3 flat = Vector3.ProjectOnPlane(toAim, up);
        float wanted = Mathf.Clamp(
            Mathf.Atan2(Vector3.Dot(toAim, up), flat.magnitude) * Mathf.Rad2Deg,
            -maxDepression, maxElevation);

        float remaining = wanted - _elevation;
        _elevation = Mathf.MoveTowards(_elevation, wanted, elevationSpeed * Time.deltaTime);

        // Trunnion: horizontal, square across the barrel as it now lies. Read
        // after the yaw, so the gun rises in the plane it is actually pointing.
        Vector3 barrel = Vector3.ProjectOnPlane(RawBarrel(), up);
        if (barrel.sqrMagnitude < 1e-6f)
            return remaining;

        // What the MESH can show is not always the whole angle: a turret with
        // no separable gun tips as one block and runs out of room early. The
        // aim keeps the full angle regardless — see BarrelDirection.
        float shown = Mathf.Clamp(_elevation, -maxDepression, ElevationCeiling);
        riser.rotation = Quaternion.AngleAxis(-shown, Vector3.Cross(up, barrel).normalized)
                         * riser.rotation;
        return remaining;
    }

    /// <summary>
    /// The hull the turret is bolted to. Its siblings are the hull mesh — the
    /// rig puts Hull and TurretPivot side by side — so its axes ARE the deck's,
    /// including whatever pitch and bank the vehicle is carrying.
    /// </summary>
    Transform Deck(Transform pivot) => pivot.parent != null ? pivot.parent : transform;

    /// <summary>
    /// Which way the gun is actually pointing right now, in three dimensions —
    /// what the vehicle's weapons fire along, so the shots and the barrel can
    /// never disagree, up or across. Zero when there is no turret to read.
    ///
    /// Read from the two live transforms rather than from the pivot's forward:
    /// the pivot inherits the quarter turn VehicleSkin puts on the model to
    /// bring its long axis onto +Z, so its forward is 90 degrees off the gun.
    /// </summary>
    public Vector3 BarrelDirection
    {
        get
        {
            if (ResolvePivot() == null)
                return Vector3.zero;
            if (_gun != null)
                return RawBarrel().normalized;

            // A turret with no separable gun cannot tip far enough to show the
            // whole angle, but it is an emitter behind a visor rather than a
            // tube — there is no barrel to disagree with. Bearing from the
            // mesh, elevation from the aim, so it can still answer something
            // overhead instead of being the one tank that cannot look up.
            Vector3 up = Deck(_pivot).up;
            Vector3 flat = Vector3.ProjectOnPlane(RawBarrel(), up).normalized;
            float rise = _elevation * Mathf.Deg2Rad;
            return (flat * Mathf.Cos(rise) + up * Mathf.Sin(rise)).normalized;
        }
    }

    /// <summary>Trunnion (or ring) to muzzle: the barrel as it currently lies.</summary>
    Vector3 RawBarrel() => _tip.position - Riser.position;

    /// <summary>
    /// Park the stable muzzle on the barrel tip. Position and aim are copied
    /// rather than parented: parenting into the model would put the anchor —
    /// and the muzzle light every weapon shares — inside something that a robot
    /// swap destroys.
    /// </summary>
    void RideBarrel()
    {
        EnsureAnchor();
        Vector3 direction = RawBarrel();
        _anchor.position = _tip.position;
        if (direction.sqrMagnitude > 1e-6f)
            _anchor.rotation = Quaternion.LookRotation(direction);
    }

    /// <summary>
    /// Found by name before it is built, the way TransformMode adopts its
    /// wheels: a bought reinforcement is a CLONE of a live robot, and it
    /// arrives carrying a copy of the anchor its template had — but not the
    /// field pointing at it. Building a second one would leave the clone with
    /// two, and its guns firing out of whichever the copy happened to be.
    /// </summary>
    void EnsureAnchor()
    {
        if (_anchor != null)
            return;
        _anchor = transform.Find(AnchorName);
        if (_anchor != null)
            return;
        _anchor = new GameObject(AnchorName).transform;
        _anchor.SetParent(transform, false);
    }

    /// <summary>
    /// The turret of the vehicle mesh that is showing right now, or null when
    /// this character is not wearing a rigged tank.
    ///
    /// Re-found rather than cached for good: VehicleSkin keeps every stage of
    /// the transformation instantiated and switches between them by activating
    /// one at a time, and a robot swap throws the whole set away and builds
    /// another. Only the mesh a character is actually wearing is active, which
    /// is exactly the test used here.
    ///
    /// THE PIVOT AND THE TIP ARE FOUND TOGETHER OR NOT AT ALL. They used to be
    /// separate steps, and a search that ran while the tank was hidden — which
    /// the fold does constantly, popping through eight stages in both
    /// directions — cleared the tip while leaving the pivot cached. The next
    /// frame the tank came back, the cache was still good, so the tip was never
    /// looked for again: every transformation after the first aimed the turret
    /// by the pivot's own forward, which sits exactly 90 degrees off the barrel
    /// (the model carries the fit's quarter turn). Hence a tank whose gun points
    /// across its own line of fire, forever, from the second fold onward.
    /// </summary>
    Transform ResolvePivot()
    {
        if (_pivot != null && _tip != null && _pivot.gameObject.activeInHierarchy)
            return _pivot;

        // Throttled, because the search fails every time for a robot whose
        // vehicle form is one unrigged mesh, and that is a search over the whole
        // character every frame it drives.
        if (Time.time < _nextSearch)
            return null;
        _nextSearch = Time.time + SearchInterval;

        Transform found = null;
        foreach (var candidate in GetComponentsInChildren<Transform>(false))
        {
            // StartsWith, not equality: glTFast makes node names unique within
            // their parent by appending a number when it has to.
            if (!candidate.name.StartsWith(PivotName))
                continue;
            found = candidate;
            break;
        }
        if (found == null)
            return null;   // hidden, or an unrigged vehicle mesh — leave the cache alone

        Transform tip = null, gun = null;
        foreach (var child in found.GetComponentsInChildren<Transform>(true))
        {
            if (tip == null && child.name.StartsWith(TipName))
                tip = child;
            else if (gun == null && child.name.StartsWith(GunName))
                gun = child;
        }

        if (tip == null)
        {
            // No barrel marker means no way to know which way the gun points,
            // and guessing is what produced the 90-degree turret. Report no
            // turret instead: the brains then turn the whole hull to aim, which
            // is how a tank with no rig has always fought.
            if (!_warnedNoTip)
            {
                _warnedNoTip = true;
                Debug.LogWarning($"[TankTurret] {name}: '{found.name}' has no '{TipName}' " +
                    "child — re-run Tools/tankturret.py over the vehicle stages.");
            }
            return null;
        }

        // Rest poses captured before anything is ever written to them, so
        // centring on the way out puts the rig back exactly as the model shipped
        // — and so the elevation always composes onto a known pose.
        _pivot = found;
        _tip = tip;
        _gun = gun;
        _pivotRest = found.localRotation;
        _gunRest = gun != null ? gun.localRotation : Quaternion.identity;
        _yaw = 0f;
        _elevation = 0f;
        return _pivot;
    }
}

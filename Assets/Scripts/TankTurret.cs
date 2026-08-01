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
/// Yaw only. A real turret elevates its gun inside a fixed mantlet; pitching
/// this one would tip the whole turret block and drive its back edge through the
/// deck, and these barrels are cast into the turret mesh rather than hinged.
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
    public const string TipName = "TurretMuzzle";

    /// <summary>Name of the stable muzzle on the character; see <see cref="Muzzle"/>.</summary>
    public const string AnchorName = "TurretMuzzleAnchor";

    [Tooltip("How fast the turret slews, in degrees per second. Fast enough to " +
             "answer a target that moves, slow enough to be worth watching.")]
    public float turnSpeed = 160f;

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

    Transform _pivot, _tip, _anchor;
    Vector3 _aimPoint;
    bool _hasAim;
    float _offTarget;
    float _nextSearch;

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
        if (_pivot != null)
            _pivot.localRotation = Quaternion.identity;
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

        Vector3 barrel = Flat(BarrelDirection(pivot));
        // With no target this frame the turret walks back to dead ahead, which
        // is where a tank carries its gun when nothing is worth pointing it at.
        Vector3 wanted = Flat(aiming ? _aimPoint - pivot.position : transform.forward);
        if (barrel.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
            return;

        float delta = Vector3.SignedAngle(barrel, wanted, Vector3.up);
        _offTarget = Mathf.Abs(delta);

        float step = turnSpeed * Time.deltaTime;
        pivot.Rotate(Vector3.up, Mathf.Clamp(delta, -step, step), Space.World);

        RideBarrel(pivot);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>
    /// Which way the gun points, measured from the pivot to the barrel tip.
    /// Falls back to the pivot's own forward for a model that was rigged without
    /// a tip marker.
    /// </summary>
    Vector3 BarrelDirection(Transform pivot) =>
        _tip != null ? _tip.position - pivot.position : pivot.forward;

    /// <summary>
    /// Park the stable muzzle on the barrel tip. Position and aim are copied
    /// rather than parented: parenting into the model would put the anchor —
    /// and the muzzle light every weapon shares — inside something that a robot
    /// swap destroys.
    /// </summary>
    void RideBarrel(Transform pivot)
    {
        EnsureAnchor();
        Vector3 direction = BarrelDirection(pivot);
        _anchor.position = _tip != null ? _tip.position : pivot.position;
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
    /// The turret of the vehicle mesh that is showing right now.
    ///
    /// Re-found rather than cached for good: VehicleSkin keeps every stage of
    /// the transformation instantiated and switches between them by activating
    /// one at a time, and a robot swap throws the whole set away and builds
    /// another. Only the mesh a character is actually wearing is active, which
    /// is exactly the test used here.
    /// </summary>
    Transform ResolvePivot()
    {
        if (_pivot != null && _pivot.gameObject.activeInHierarchy)
            return _pivot;

        // Throttled, because the search fails every time for a robot whose
        // vehicle form is one unrigged mesh, and that is a search over the whole
        // character every frame it drives.
        if (Time.time < _nextSearch)
            return null;
        _nextSearch = Time.time + SearchInterval;

        _tip = null;
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

        if (found != null)
            foreach (var child in found.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith(TipName))
                {
                    _tip = child;
                    break;
                }

        // Only adopt a live turret; a null result leaves the previous one in
        // _pivot so LateUpdate can still centre it on the way out.
        if (found != null)
            _pivot = found;
        return found;
    }
}

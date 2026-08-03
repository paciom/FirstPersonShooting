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
    bool _warnedNoTip;
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

        // Everything is measured against the DECK, never against the world.
        // A turret is bolted to a ring: it can only spin in the plane of the
        // hull it sits on, whatever that hull is doing. Turning it about the
        // world's up instead works right up until the hull stops being level —
        // and then the turret rolls off its own mounting, which is exactly what
        // a banking jet in Dogfight showed.
        Vector3 axis = Deck(pivot).up;
        Vector3 barrel = Vector3.ProjectOnPlane(RawBarrel(pivot), axis);
        // With no target this frame the turret walks back to dead ahead, which
        // is where a tank carries its gun when nothing is worth pointing it at.
        Vector3 wanted = Vector3.ProjectOnPlane(
            aiming ? _aimPoint - pivot.position : transform.forward, axis);
        if (barrel.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
            return;

        float delta = Vector3.SignedAngle(barrel, wanted, axis);
        _offTarget = Mathf.Abs(delta);

        // Space.Self about the turret's OWN up, which is the deck's up and
        // stays the deck's up: every turn is a spin about an axis the turn
        // itself leaves untouched, so the seating can never drift no matter how
        // long the fight runs or how the hull is flying.
        float step = turnSpeed * Time.deltaTime;
        pivot.Rotate(Vector3.up, Mathf.Clamp(delta, -step, step), Space.Self);

        RideBarrel(pivot);
    }

    /// <summary>
    /// The hull the turret is bolted to. Its siblings are the hull mesh — the
    /// rig puts Hull and TurretPivot side by side — so its axes ARE the deck's,
    /// including whatever pitch and bank the vehicle is carrying.
    /// </summary>
    Transform Deck(Transform pivot) => pivot.parent != null ? pivot.parent : transform;

    /// <summary>
    /// Which way the gun is actually pointing right now, normalized and flat in
    /// the deck's plane — what the vehicle's weapons fire along, so the shots
    /// and the barrel can never disagree. Zero when there is no turret to read.
    ///
    /// Read from the two live transforms rather than from the pivot's forward:
    /// the pivot inherits the quarter turn VehicleSkin puts on the model to
    /// bring its long axis onto +Z, so its forward is 90 degrees off the gun.
    /// </summary>
    public Vector3 BarrelDirection
    {
        get
        {
            var pivot = ResolvePivot();
            if (pivot == null)
                return Vector3.zero;
            return Vector3.ProjectOnPlane(RawBarrel(pivot), Deck(pivot).up).normalized;
        }
    }

    Vector3 RawBarrel(Transform pivot) => _tip.position - pivot.position;

    /// <summary>
    /// Park the stable muzzle on the barrel tip. Position and aim are copied
    /// rather than parented: parenting into the model would put the anchor —
    /// and the muzzle light every weapon shares — inside something that a robot
    /// swap destroys.
    /// </summary>
    void RideBarrel(Transform pivot)
    {
        EnsureAnchor();
        Vector3 direction = RawBarrel(pivot);
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

        Transform tip = null;
        foreach (var child in found.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith(TipName))
            {
                tip = child;
                break;
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

        _pivot = found;
        _tip = tip;
        return _pivot;
    }
}

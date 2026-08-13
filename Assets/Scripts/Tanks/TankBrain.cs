using UnityEngine;

/// <summary>
/// What a tank that isn't the player does.
///
/// The whole behaviour is one rule — HOLD YOUR RANGE — and everything else falls
/// out of it. Too far, close in. Too near, back off. At range, circle. The gun
/// tracks its target the entire time, independently of where the chassis is
/// going, which is the same freedom the player has and the reason the fight
/// reads as a fight rather than as a queue of things driving at you.
///
/// ONE BRAIN FOR BOTH SIDES. A recruit escorting the hero and a raider hunting
/// it want exactly the same thing — pick the nearest enemy, hold a range, keep
/// shooting — and differ only in what they are anchored to and how far they will
/// stray. <see cref="anchor"/> and <see cref="leash"/> are that difference:
/// raiders have none and roam, allies are tied to the hero so they cannot wander
/// off the bottom of the screen chasing a straggler.
///
/// It deliberately does NOT lead its shots. A raider that predicted the hero's
/// movement would be unmissable at these ranges, and this is a mode an eight
/// year old is supposed to win.
///
/// Nothing here paths. There is no NavMesh in this mode — the field is rebuilt
/// under the player every few seconds and re-baking that would cost more than
/// the raiders are worth — so obstacles are handled by driving at a target and
/// letting <see cref="TankPawn"/> shove the hull off anything it meets. The one
/// concession is <see cref="_skirt"/>: a tank that has stopped making progress
/// picks a side and slides along whatever is in the way.
/// </summary>
[DefaultExecutionOrder(-50)]
public class TankBrain : MonoBehaviour
{
    /// <summary>
    /// Preferred over anything else within reach. The raiders are pointed at the
    /// hero: without it a screen full of recruits would soak the whole army and
    /// the player would be watching their own escort fight.
    /// </summary>
    public TankPawn favourite;

    /// <summary>
    /// How much closer another target has to be before it beats
    /// <see cref="favourite"/>. Under 1, so the favourite wins ties comfortably
    /// but a recruit parked in a raider's face still gets shot at.
    /// </summary>
    public float favouriteBias = 0.55f;

    /// <summary>Who it stays near, or null to roam. The hero, for a recruit.</summary>
    public Transform anchor;

    /// <summary>Metres it may stray from <see cref="anchor"/> before coming back.</summary>
    public float leash = 24f;

    /// <summary>Metres it wants between itself and whatever it is shooting.</summary>
    public float standoff = 18f;

    /// <summary>Slack around <see cref="standoff"/> where it circles rather than
    /// shuffling in and out on the spot.</summary>
    public float deadband = 4f;

    /// <summary>Which way it circles. Rolled once at spawn so a pair arriving
    /// together split rather than orbit nose to tail.</summary>
    public float orbit = 1f;

    /// <summary>Where it drifts with nothing to fight. Raiders come down the
    /// field; recruits hold station on the hero, so theirs is zero.</summary>
    public float idleDrift = -0.6f;

    /// <summary>
    /// How far it will shoot. Effectively unlimited for anything that can drive
    /// to its target; an outpost cannot, so it is the one thing here that needs
    /// a real number — a building blazing away at a tank sixty metres up the
    /// field lands nothing and looks broken doing it.
    /// </summary>
    public float engageRange = float.MaxValue;

    /// <summary>How often it re-picks a target. Not every frame: a brain that
    /// re-chose continuously would jitter between two equidistant enemies and
    /// never close on either.</summary>
    const float RetargetInterval = 0.6f;

    /// <summary>Seconds of no progress before it decides something is in the way.</summary>
    const float StuckSeconds = 0.7f;
    const float SkirtSeconds = 1.2f;

    TankPawn _self;
    TankPawn _target;
    float _nextRetarget;
    float _stuckFor;
    float _skirt;
    Vector3 _wasAt;

    void Awake()
    {
        _self = GetComponent<TankPawn>();
        _wasAt = transform.position;
    }

    void Update()
    {
        if (_self == null || _self.IsDown)
            return;

        if (Time.time >= _nextRetarget || _target == null || _target.IsDown)
        {
            _nextRetarget = Time.time + RetargetInterval;
            _target = PickTarget();
        }

        if (_target == null)
        {
            Idle();
            return;
        }

        // Off the bottom of the screen: guns silent. A pawn out there is either
        // seconds from the sweep or racing to rejoin a brawl the hero slowed
        // down for — either way, fire arriving from off camera reads as shots
        // from nowhere. It still drives (rejoining is the point), it just does
        // not shoot until it is back in the picture.
        bool inPlay = !TankField.FallenBehind(transform.position, 2f);

        // A bolted-down gun has no manoeuvre to make. Everything below this is
        // about closing, holding and circling, none of which an outpost can do.
        if (_self.Kind == TankPawn.Chassis.Structure)
        {
            _self.AimAt(_target.Center);
            _self.Firing = inPlay;
            return;
        }

        Vector3 toTarget = _target.transform.position - transform.position;
        toTarget.y = 0f;
        float range = toTarget.magnitude;
        Vector3 forward = range > 1e-4f ? toTarget / range : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward) * orbit;

        Vector3 heading;
        if (range > standoff + deadband)
            heading = forward + side * 0.35f;            // close, but not head on
        else if (range < standoff - deadband)
            heading = -forward + side * 0.5f;            // peel off
        else
            heading = side;                              // hold and circle

        heading += Unstick(side);
        heading += HomeToAnchor();

        _self.Drive = new Vector2(heading.x, heading.z).normalized;
        // Aim at the hull's middle, not its feet: a shot at the floor line clips
        // the deck in front of a tank at this camera's angle.
        _self.AimAt(_target.Center);
        // Held down whenever on screen. TankPawn will not let a shot out until
        // the barrel has arrived, so the gate is the turret's slew and not this
        // decision.
        _self.Firing = inPlay;
    }

    /// <summary>
    /// Nearest live enemy, with <see cref="favourite"/> given a head start.
    ///
    /// Structures are skipped unless nothing else is standing: an outpost cannot
    /// chase anybody, so a raider that stopped to guard one would take itself out
    /// of the fight, and a recruit that stopped to chew through 1400 shield would
    /// leave the hero alone for a minute. The player decides whether an outpost
    /// is worth attacking — that decision is the whole reason it is there.
    /// </summary>
    TankPawn PickTarget()
    {
        TankPawn best = null;
        float bestScore = float.MaxValue;
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn == _self || pawn.Team == _self.Team || pawn.IsDown)
                continue;
            if (pawn.Kind == TankPawn.Chassis.Structure)
                continue;
            float score = Vector3.Distance(pawn.transform.position, transform.position);
            if (score > engageRange)
                continue;
            if (pawn == favourite)
                score *= favouriteBias;
            if (score >= bestScore)
                continue;
            bestScore = score;
            best = pawn;
        }
        return best;
    }

    /// <summary>Nothing to fight: drift, or go back to whoever we are escorting.</summary>
    void Idle()
    {
        Vector3 heading = new Vector3(0f, 0f, idleDrift);
        heading += HomeToAnchor();
        _self.Drive = new Vector2(heading.x, heading.z);
        _self.Firing = false;
    }

    /// <summary>
    /// The leash. Beyond it the pull grows until it dominates whatever the brain
    /// wanted to do, so a recruit can fight freely inside its radius and cannot
    /// be led away from the hero by a raider that keeps retreating.
    /// </summary>
    Vector3 HomeToAnchor()
    {
        if (anchor == null)
            return Vector3.zero;
        Vector3 home = anchor.position - transform.position;
        home.y = 0f;
        float distance = home.magnitude;
        if (distance <= leash || distance < 1e-4f)
            return Vector3.zero;
        return home / distance * Mathf.Min(3f, (distance - leash) / leash * 2f + 0.6f);
    }

    /// <summary>
    /// Progress, measured rather than assumed. A tank pinned against a boulder is
    /// still being told to drive at its target every frame and would sit there
    /// grinding forever; when the distance actually covered stops matching the
    /// effort, it commits to sliding one way for a beat instead.
    /// </summary>
    Vector3 Unstick(Vector3 side)
    {
        float dt = Time.deltaTime;
        float moved = (transform.position - _wasAt).magnitude;
        _wasAt = transform.position;

        if (_skirt > 0f)
        {
            _skirt -= dt;
            return side * 1.4f;
        }

        // A tenth of the distance a pawn at this speed covers in a frame: below
        // that it is turning on the spot or held by something.
        _stuckFor = moved < _self.speed * dt * 0.1f ? _stuckFor + dt : 0f;
        if (_stuckFor >= StuckSeconds)
        {
            _stuckFor = 0f;
            _skirt = SkirtSeconds;
        }
        return Vector3.zero;
    }
}

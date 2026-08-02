using UnityEngine;

/// <summary>
/// What a raider does.
///
/// The whole behaviour is one rule — HOLD YOUR RANGE — and everything else falls
/// out of it. Too far, close in. Too near, back off. At range, circle. The gun
/// tracks the hero the entire time, independently of where the chassis is going,
/// which is the same freedom the player has and the reason the fight reads as a
/// fight rather than as a queue of things driving at you.
///
/// It deliberately does NOT lead its shots. A raider that predicted the hero's
/// movement would be unmissable at these ranges, and this is a mode an eight
/// year old is supposed to win sometimes.
///
/// Nothing here paths. There is no NavMesh in this mode — the field is rebuilt
/// under the player every few seconds and re-baking that would cost more than
/// the raiders are worth — so obstacles are handled by driving at a target and
/// letting <see cref="TankPawn"/> shove the hull off anything it meets. The one
/// concession is <see cref="_skirt"/>: a raider that has stopped making progress
/// picks a side and slides along whatever is in the way.
/// </summary>
[DefaultExecutionOrder(-50)]
public class TankBrain : MonoBehaviour
{
    /// <summary>Who it is hunting. Set by the mode; always the hero.</summary>
    public TankPawn quarry;

    /// <summary>Metres it wants between itself and the hero.</summary>
    public float standoff = 18f;

    /// <summary>Slack around <see cref="standoff"/> where it just circles rather
    /// than shuffling in and out on the spot.</summary>
    public float deadband = 4f;

    /// <summary>Which way it circles. Rolled once at spawn so a pair of raiders
    /// arriving together split rather than orbit nose to tail.</summary>
    public float orbit = 1f;

    /// <summary>Seconds of no progress before it decides something is in the way.</summary>
    const float StuckSeconds = 0.7f;
    const float SkirtSeconds = 1.2f;

    TankPawn _self;
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

        // A raider with nothing to hunt still drives down the field, so a hero
        // continuing after a de-rez does not find the enemy standing still.
        if (quarry == null || quarry.IsDown)
        {
            _self.Drive = new Vector2(0f, -0.6f);
            _self.Firing = false;
            return;
        }

        Vector3 toQuarry = quarry.transform.position - transform.position;
        toQuarry.y = 0f;
        float range = toQuarry.magnitude;
        Vector3 forward = range > 1e-4f ? toQuarry / range : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward) * orbit;

        Vector3 heading;
        if (range > standoff + deadband)
            heading = forward + side * 0.35f;            // close, but not head on
        else if (range < standoff - deadband)
            heading = -forward + side * 0.5f;            // peel off
        else
            heading = side;                              // hold and circle

        heading += Unstick(forward, side);

        _self.Drive = new Vector2(heading.x, heading.z).normalized;
        // Aim at the hull's middle, not its feet: a shot at the floor line
        // clips the deck in front of a tank at this camera's angle.
        _self.AimAt(quarry.Center);
        // Held down always. TankPawn will not let a shot out until the barrel
        // has arrived, so the gate is the turret's slew and not this decision.
        _self.Firing = true;
    }

    /// <summary>
    /// Progress, measured rather than assumed. A raider pinned against a boulder
    /// is still being told to drive at the hero every frame and would sit there
    /// grinding forever; when the distance actually covered stops matching the
    /// effort, it commits to sliding one way for a beat instead.
    /// </summary>
    Vector3 Unstick(Vector3 forward, Vector3 side)
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

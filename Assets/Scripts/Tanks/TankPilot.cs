using UnityEngine;

/// <summary>
/// The machine at the hero's controls — TANK RAID: AI v AI.
///
/// It is deliberately NOT <see cref="TankBrain"/>. A raider's job is to hold a
/// range against one enemy; the hero's job is to get UP THE FIELD, which is a
/// different problem and the one the mode is actually about. A hero driven by
/// the raider brain would circle the first tank it met until the frontier caught
/// up and pushed it off the bottom of the screen.
///
/// So it drives forward by default and lets four pulls argue with that:
///
///  * PICKUPS. It will detour for a pod, a kit or a beacon within reach — the
///    strongest pull, because collecting them is most of what makes the run
///    interesting to watch.
///  * SPACE. Anything too close gets backed away from, so it does not simply
///    drive into the army and stop.
///  * LANES. It hunts the emptiest side of the strip, which is what makes it
///    look like it is picking its way through rather than ploughing.
///  * THE FENCE. A soft push off both walls, because everything above can
///    otherwise pin it against one.
///
/// The gun is separate and simple: hold the nearest enemy, always. That is the
/// same freedom the player has, and watching a hull drive one way while its
/// turret stays locked the other is the clearest possible advertisement for what
/// the two sticks do.
/// </summary>
[DefaultExecutionOrder(-50)]
public class TankPilot : MonoBehaviour
{
    /// <summary>How far it will go out of its way for a pickup.</summary>
    const float PickupReach = 30f;

    /// <summary>Closer than this and it gives ground.</summary>
    const float TooClose = 13f;

    /// <summary>How far ahead it reads the field when choosing a side.</summary>
    const float LaneLook = 40f;

    /// <summary>Where the fence starts pushing back, as a fraction of the strip.</summary>
    const float WallBite = 0.75f;

    /// <summary>
    /// Whether to take on an outpost. Structures are worth a great deal and are
    /// also the one thing that can kill an unattended AI, so it only commits
    /// when it is healthy — the same call a player makes.
    /// </summary>
    const float OutpostShieldFloor = 0.55f;

    TankPawn _self;

    void Awake() => _self = GetComponent<TankPawn>();

    void Update()
    {
        if (_self == null || _self.IsDown)
            return;

        Vector3 heading = Vector3.forward;                  // up the field, always
        heading += TowardPickup() * 1.6f;
        heading += AwayFromCrowding() * 1.2f;
        heading += TowardOpenLane() * 0.7f;
        heading += OffTheWalls() * 2f;

        _self.Drive = new Vector2(heading.x, heading.z).normalized;
        _self.Firing = true;

        var quarry = ChooseQuarry();
        if (quarry != null)
            _self.AimAt(quarry.Center);
        else
            _self.AimAlong(Vector3.forward);                // gun forward when idle
    }

    /// <summary>
    /// What to shoot. Nearest enemy, except that an outpost in range outranks it
    /// while the tank is healthy enough to trade with one — an AI that never
    /// captured anything would never show off the half of the mode that matters
    /// most.
    /// </summary>
    TankPawn ChooseQuarry()
    {
        TankPawn nearest = null, outpost = null;
        float nearestScore = float.MaxValue, outpostScore = float.MaxValue;

        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn == _self || pawn.Team == _self.Team || pawn.IsDown)
                continue;
            Vector3 gap = pawn.transform.position - transform.position;
            gap.y = 0f;
            // Behind and receding is not worth turning the turret for.
            if (gap.z < -18f)
                continue;
            float distance = gap.magnitude;

            if (pawn.Kind == TankPawn.Chassis.Structure)
            {
                if (distance < outpostScore) { outpostScore = distance; outpost = pawn; }
            }
            else if (distance < nearestScore)
            {
                nearestScore = distance; nearest = pawn;
            }
        }

        bool healthy = _self.Shield != null && _self.Shield.Normalized >= OutpostShieldFloor;
        if (outpost != null && healthy && outpostScore < 45f)
            return outpost;
        return nearest ?? outpost;
    }

    /// <summary>The nearest pickup worth a detour, as a pull toward it.</summary>
    Vector3 TowardPickup()
    {
        Vector3 best = Vector3.zero;
        float bestDistance = PickupReach;
        foreach (var pickup in TankPickup.All)
        {
            if (pickup == null)
                continue;
            Vector3 gap = pickup.transform.position - transform.position;
            gap.y = 0f;
            // Never chase one that is already behind: the frontier is coming and
            // reversing for a kit is how a run ends.
            if (gap.z < -4f)
                continue;
            float distance = gap.magnitude;
            if (distance >= bestDistance || distance < 1e-3f)
                continue;
            bestDistance = distance;
            best = gap / distance;
        }
        return best;
    }

    /// <summary>Give ground to anything that has got too close.</summary>
    Vector3 AwayFromCrowding()
    {
        Vector3 push = Vector3.zero;
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn == _self || pawn.Team == _self.Team || pawn.IsDown)
                continue;
            Vector3 gap = transform.position - pawn.transform.position;
            gap.y = 0f;
            float distance = gap.magnitude;
            float want = TooClose + pawn.Radius;
            if (distance >= want || distance < 1e-3f)
                continue;
            push += gap / distance * ((want - distance) / want);
        }
        return Vector3.ClampMagnitude(push, 1.5f);
    }

    /// <summary>
    /// Which side of the strip has less standing in it. Sampled as two buckets
    /// rather than anything cleverer — the strip is thirty metres wide and the
    /// question is only ever "left or right".
    /// </summary>
    Vector3 TowardOpenLane()
    {
        float left = 0f, right = 0f;
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn == _self || pawn.Team == _self.Team || pawn.IsDown)
                continue;
            float ahead = pawn.transform.position.z - transform.position.z;
            if (ahead < 0f || ahead > LaneLook)
                continue;
            // Nearer things count for more, so a wall of tanks at forty metres
            // does not outvote the one about to run into us.
            float weight = 1f - ahead / LaneLook;
            if (pawn.transform.position.x < transform.position.x) left += weight;
            else right += weight;
        }
        float bias = Mathf.Clamp(left - right, -1f, 1f);
        return new Vector3(bias, 0f, 0f);
    }

    /// <summary>A soft shove off both walls, before the hard clamp has to do it.</summary>
    Vector3 OffTheWalls()
    {
        float edge = TankField.HalfWidth * WallBite;
        float x = transform.position.x;
        if (Mathf.Abs(x) <= edge)
            return Vector3.zero;
        float over = (Mathf.Abs(x) - edge) / Mathf.Max(0.01f, TankField.HalfWidth - edge);
        return new Vector3(-Mathf.Sign(x) * Mathf.Clamp01(over), 0f, 0f);
    }
}

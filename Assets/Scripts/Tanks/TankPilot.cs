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
/// It drives at FULL SPEED, always — never throttled for balance (the field
/// balances by how many raiders it feeds, not by slowing anybody down) — and
/// lets four pulls argue about direction:
///
///  * PICKUPS. It will detour for a pod, a kit or a beacon within reach — a
///    strong pull, because collecting them is half of what there is to watch.
///  * OUTPOSTS. While healthy it drives at the outpost it has decided to crack,
///    not just shoots at it — captures are the other half.
///  * SPACE. Anything too close gets backed away from, so it does not simply
///    drive into the army and stop.
///  * LANES. It hunts the emptiest side of the strip, which is what makes it
///    look like it is picking its way through rather than ploughing.
///  * THE FENCE. A soft push off both walls, because everything above can
///    otherwise pin it against one.
///
/// Waiting for the escort is a GEO-FENCE, not a slowdown: while a recruit is
/// caught at the bottom edge the hero may fight and jink at full power but
/// cannot advance past where it stood — see <see cref="EscortFence"/>.
///
/// The gun is separate and simple: hold the nearest enemy, always. That is the
/// same freedom the player has, and watching a hull drive one way while its
/// turret stays locked the other is the clearest possible advertisement for what
/// the two sticks do.
/// </summary>
[DefaultExecutionOrder(-50)]
public class TankPilot : MonoBehaviour
{
    /// <summary>How far it will go out of its way for a pickup. Most of the
    /// strip's width: collecting is the show, so almost nothing on screen is
    /// too far to fetch.</summary>
    const float PickupReach = 45f;

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

        Vector3 heading = Vector3.forward;
        heading += TowardPickup() * 1.6f;
        heading += TowardOutpost() * 1.1f;
        heading += AwayFromCrowding() * 1.2f;
        heading += TowardOpenLane() * 0.7f;
        heading += OffTheWalls() * 2f;

        _self.Drive = new Vector2(heading.x, heading.z).normalized;
        _self.MaxAdvanceZ = EscortFence();
        _self.Firing = true;

        var quarry = ChooseQuarry();
        if (quarry != null)
            _self.AimAt(quarry.Center);
        else
            _self.AimAlong(Vector3.forward);                // gun forward when idle
    }

    /// <summary>
    /// The hold line that waits for the escort — a GEO-FENCE, not a slowdown.
    /// A recruit caught at the field's trail line is being dragged by the
    /// scroll, and a hero that keeps advancing turns that into a permanent
    /// state. While one is caught, the hero's forward progress is fenced at
    /// its current position: it still drives, fights and dodges at full
    /// power, it just gains no ground until the recruits (faster than the
    /// hero) are back in formation — usually seconds.
    ///
    /// The one thing that outranks waiting is running for its life: a hero at
    /// a third shield keeps fleeing, because a dead hero rescues nobody.
    /// </summary>
    float EscortFence()
    {
        if (_self.Shield != null && _self.Shield.Normalized < 0.35f)
            return float.PositiveInfinity;
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn == _self || pawn.Team != _self.Team || pawn.IsDown
                || pawn.Kind == TankPawn.Chassis.Structure)
                continue;
            if (pawn.transform.position.z < TankField.Frontier - TankField.Trail + 2.5f)
                return _self.transform.position.z + 0.5f;
        }
        return float.PositiveInfinity;
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

    /// <summary>
    /// Drive at the outpost worth cracking, while healthy enough to crack it —
    /// the same shield floor <see cref="ChooseQuarry"/> applies to shooting
    /// one. Pulls to just inside trading range and no further: parking on an
    /// outpost's doorstep is how an AI eats a full broadside.
    /// </summary>
    Vector3 TowardOutpost()
    {
        if (_self.Shield == null || _self.Shield.Normalized < OutpostShieldFloor)
            return Vector3.zero;

        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn.Team == _self.Team || pawn.IsDown
                || pawn.Kind != TankPawn.Chassis.Structure)
                continue;
            Vector3 gap = pawn.transform.position - transform.position;
            gap.y = 0f;
            if (gap.z < -6f)
                continue;                       // the frontier owns what is behind
            float distance = gap.magnitude;
            if (distance < 1e-3f || distance > 55f)
                continue;
            return gap / distance * Mathf.Clamp01((distance - 16f) / 24f);
        }
        return Vector3.zero;
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

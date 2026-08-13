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
/// So it drives forward by default and lets five pulls argue with that:
///
///  * THE THROTTLE. Full ahead on open road, eased right down when the army is
///    actually engaging. A hero that never slows outruns its own enemies —
///    every fight slides off the bottom of the screen before it happens, and
///    the broadcast is a tank driving alone up an empty road. The war is the
///    show; the throttle is what keeps it in shot.
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

    /// <summary>An enemy inside this range means a fight is on, and the hero
    /// stays for it instead of driving out of it.</summary>
    const float BrawlRange = 24f;

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

        // The throttle is the drive vector's LENGTH — TankPawn reads 0–1 as
        // speed — so it must survive to the end rather than being normalized
        // away. Errands override the brawl brake: a hero that crawled to its
        // pickups would be taking fire the whole way for no story at all.
        Vector3 pickupPull = TowardPickup();
        Vector3 outpostPull = TowardOutpost();
        float throttle = Advance();
        if (pickupPull != Vector3.zero)
            throttle = Mathf.Max(throttle, 0.75f);
        else if (outpostPull != Vector3.zero)
            throttle = Mathf.Max(throttle, 0.6f);

        Vector3 heading = Vector3.forward * throttle;
        heading += pickupPull * 1.6f;
        heading += outpostPull * 1.1f;
        heading += AwayFromCrowding() * 1.2f;
        heading += TowardOpenLane() * 0.7f;
        heading += OffTheWalls() * 2f;

        var drive = new Vector2(heading.x, heading.z);
        _self.Drive = drive.sqrMagnitude > 1e-6f ? drive.normalized * throttle : Vector2.zero;
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

    /// <summary>
    /// The throttle — and it never opens right up. This pilot exists for the
    /// broadcast, and a hero at full speed outruns its own 7 m/s enemies: the
    /// war slides off the bottom of the screen and the show is a tank driving
    /// alone. Cruise sits just under raider speed, so the army genuinely
    /// arrives, the escort keeps formation, and the drops get collected —
    /// distance still accumulates because the fights end.
    ///
    /// Eased right down when the army is actually engaging, so the hero
    /// stands and trades fire — the scroll still creeps the run forward
    /// underneath the brawl. Enemies already dropping off the bottom are not
    /// a fight, so they do not slow it.
    ///
    /// The exception is a hero at low shield, which runs THROUGH fights: the
    /// mode's own repair-drop logic hands medicine to a hurt hero, and going
    /// and finding it is both the survival play and a story to watch.
    /// </summary>
    float Advance()
    {
        if (_self.Shield != null && _self.Shield.Normalized < 0.35f)
            return 1f;

        float nearest = float.MaxValue;
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn.Team == _self.Team || pawn.IsDown
                || pawn.Kind == TankPawn.Chassis.Structure)
                continue;
            Vector3 gap = pawn.transform.position - transform.position;
            gap.y = 0f;
            if (gap.z < -10f)
                continue;                       // dropping behind: the scroll has it
            nearest = Mathf.Min(nearest, gap.magnitude);
        }

        if (nearest < BrawlRange)
            return 0.2f;
        if (nearest < BrawlRange * 1.6f)
            return 0.4f;                        // ease in rather than braking on a line
        return 0.55f;                           // cruise: the army can keep up
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

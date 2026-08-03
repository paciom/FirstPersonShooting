using UnityEngine;

/// <summary>
/// The CPU pilot. It writes exactly what a player writes — <c>Steer</c>,
/// <c>Throttle</c>, <c>Firing</c> — and nothing else, so the pawn cannot tell
/// who is flying it. That one property seam is what makes AI v AI and
/// Player v AI the same mode with a different card.
///
/// One rule, TankBrain's rule, lifted into the air: GET BEHIND AND STAY THERE.
/// Too far — chase the point behind the target's tail. In the saddle — track
/// the lead. Someone in MY saddle — break hard the way this jet always breaks
/// (rolled once at spawn) until the tail is clear. Head-on — step aside first;
/// a merge that trades shield for shield teaches nothing and looks like a
/// referee's mistake.
///
/// Deliberately fair rather than sharp: the brain re-decides at a human-ish
/// cadence and points a few degrees wrong on purpose. This is a mode an eight
/// year old is supposed to beat sometimes — the AI's edge is that it never
/// panics, not that it never misses.
/// </summary>
/// <remarks>Runs at the drivers' order, before the pawns, exactly as the mode
/// itself does — a driver that ran after its pawn would always be a frame
/// stale.</remarks>
[DefaultExecutionOrder(-50)]
public class JetBrain : MonoBehaviour
{
    /// <summary>Metres behind the quarry the pursuit aims for. Outside guns'
    /// best range on purpose: arriving AT the target is how you overshoot.</summary>
    const float SaddleBehind = 11f;

    /// <summary>Inside this, track the lead instead of the saddle point.</summary>
    const float GunRange = 34f;

    /// <summary>An enemy this close, behind me and pointed at me, is a tail.</summary>
    const float TailedRange = 38f;
    const float BreakSeconds = 1.6f;

    /// <summary>Seconds between decisions. The reaction delay that keeps the
    /// brain honest — AIBrain's fairness knob, at AIBrain's kind of number.</summary>
    const float ThinkSeconds = 0.25f;

    const float AimJitterDegrees = 2.5f;

    /// <summary>Fire only this close to on-target. Wider than the pawn's own
    /// assist cone: the brain squeezes early and lets the assist finish.</summary>
    const float FireCone = 9f;
    const float FireRange = 85f;

    public JetPawn pawn;
    public JetPawn quarry;

    /// <summary>+1 or -1, rolled once at spawn: which way this pilot always
    /// breaks. Two jets that break mirrored ways make a shape; two that share
    /// one make a queue.</summary>
    public float breakSign = 1f;

    float _nextThink;
    float _breakingUntil;
    Vector3 _goal;
    bool _goalIsLead;

    void Update()
    {
        if (pawn == null || pawn.IsDown || !pawn.FlightOn)
            return;

        if (quarry == null || quarry.IsDown)
            quarry = JetPawn.NearestEnemy(pawn.transform.position, pawn.Team);

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + ThinkSeconds;
            Think();
        }

        Fly();
    }

    /// <summary>Pick this beat's goal point. Runs at the reaction cadence, so a
    /// target that reverses mid-beat enjoys a moment of being wrong about.</summary>
    void Think()
    {
        if (quarry == null)
        {
            // Nobody to fight: a wide lap of the spawn ring until there is.
            Vector3 flat = new Vector3(pawn.transform.position.x, 0f, pawn.transform.position.z);
            if (flat.sqrMagnitude < 1f)
                flat = Vector3.forward;
            _goal = Quaternion.Euler(0f, 40f, 0f) * flat.normalized * DogfightSky.SpawnRing;
            _goal.y = DogfightSky.SpawnAltitude;
            _goalIsLead = false;
            return;
        }

        Vector3 me = pawn.transform.position;
        Vector3 them = quarry.transform.position;
        Vector3 gap = them - me;
        float distance = gap.magnitude;

        // Someone on my six: break. Hard turn my way, with a pitch weave the
        // pursuer's assist cone has to keep re-earning.
        bool tailed = distance < TailedRange
                      && Vector3.Dot(quarry.transform.forward, -gap.normalized) > 0.75f
                      && Vector3.Dot(pawn.transform.forward, gap.normalized) < 0.1f;
        if (tailed && Time.time >= _breakingUntil)
            _breakingUntil = Time.time + BreakSeconds;

        // Head-on merge closing fast: step aside rather than joust.
        bool headOn = distance < 26f
                      && Vector3.Dot(pawn.transform.forward, quarry.transform.forward) < -0.6f
                      && Vector3.Dot(pawn.transform.forward, gap.normalized) > 0.6f;

        if (headOn)
        {
            _goal = me + pawn.transform.right * (breakSign * 30f) + Vector3.up * 6f;
            _goalIsLead = false;
        }
        else if (distance > GunRange)
        {
            // The saddle: behind the target, on its own line.
            _goal = them - quarry.transform.forward * SaddleBehind;
            _goalIsLead = false;
        }
        else
        {
            // In guns: the lead, smeared by the pilot's honest couple of
            // degrees. Jitter scales with distance so it stays angular.
            Vector3 lead = them + quarry.Velocity * (distance / 90f);
            _goal = lead + Random.insideUnitSphere
                    * (distance * Mathf.Tan(AimJitterDegrees * Mathf.Deg2Rad));
            _goalIsLead = true;
        }
    }

    /// <summary>Every frame: steer at the goal, set the throttle by the
    /// geometry, squeeze when close to on-target.</summary>
    void Fly()
    {
        Vector3 me = pawn.transform.position;
        bool breaking = Time.time < _breakingUntil;

        if (breaking)
        {
            // The break: full turn the rolled way, weaving in pitch.
            pawn.Steer = new Vector2(breakSign,
                Mathf.Sin(Time.time * 5f) * 0.6f);
            pawn.Throttle = 1f;
            pawn.Firing = false;
            return;
        }

        Vector3 toGoal = _goal - me;
        Vector3 local = pawn.transform.InverseTransformDirection(toGoal.normalized);
        // Proportional steering with a full deflection by ~30 degrees off.
        var steer = new Vector2(
            Mathf.Clamp(local.x * 2.2f, -1f, 1f),
            Mathf.Clamp(local.y * 2.2f, -1f, 1f));
        // Goal behind me: commit to the break side instead of dithering
        // through zero, where proportional steering points nowhere.
        if (local.z < -0.2f)
            steer.x = breakSign;
        pawn.Steer = steer;

        float distance = quarry != null
            ? Vector3.Distance(me, quarry.transform.position)
            : toGoal.magnitude;
        pawn.Throttle = distance > 45f ? 1f : distance < 16f ? -0.5f : 0f;

        if (quarry != null && _goalIsLead)
        {
            Vector3 toQuarry = quarry.Center - me;
            pawn.Firing = toQuarry.magnitude < FireRange
                          && Vector3.Angle(pawn.transform.forward, _goal - me) < FireCone
                          && Vector3.Dot(pawn.transform.forward, toQuarry.normalized) > 0f;
        }
        else
        {
            pawn.Firing = false;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sniper aim assist: the player says WHICH WAY, the computer does the last few
/// degrees.
///
/// Sniping on a touch screen was the hardest thing in the game. At 3.5x zoom a
/// thumb-width of stick sweeps past a robot entirely, and the aim stick steers
/// at a RATE — so landing the crosshair means pushing, overshooting, pushing
/// back, and overshooting the other way. The scope magnified the target and the
/// problem equally.
///
/// THE RULE THIS IMPLEMENTS: push roughly the right way and the shot is on.
/// Not "press to auto-aim" — the player still picks the target, still has to
/// push toward it, and can still walk the crosshair anywhere they like.
///
/// HOW CONTROL IS KEPT. Three properties, in order of how much they matter:
///
///  1. NO INPUT, NO MOVEMENT. The assist is a blend between what the player
///     asked for and what would land on the target, and the blend weight is
///     proportional to how hard they are pushing. Hands off the stick and the
///     weight is zero — the view never drifts on its own, ever. This is the
///     one property that makes the aim feel owned rather than driven.
///  2. PUSH AWAY AND IT LETS GO. Steering more than <see cref="releaseTolerance"/>
///     off the target drops the lock outright, instantly and completely.
///  3. IT NEVER TAKES ALL OF THE STICK. <see cref="strength"/> is capped below
///     1, so a fraction of the raw input always survives the blend and the
///     crosshair can always be walked off a target that isn't the one wanted.
///
/// WHY BLENDING RATHER THAN NUDGING. The obvious implementation adds a pull
/// toward the target on top of the player's input; that one overshoots, because
/// the pull is still pulling when the crosshair arrives. Blending toward the
/// delta that would land EXACTLY on the target gets the braking for free: as
/// the error shrinks, so does the thing being blended toward, so the crosshair
/// decelerates onto the robot and stays there. Magnetism and slowdown out of
/// one number.
///
/// Only while the sniper scope is up — the scope is the player asking for help
/// hitting something far away, and hip-fire is close-range enough not to need
/// it. <see cref="SniperScope"/> draws the bracket that says what is locked, so
/// the assist is never invisible.
/// </summary>
public class AimAssist : MonoBehaviour
{
    [Tooltip("Most of the stick the assist may take, 0-1. Below 1 on purpose: " +
             "the remainder is what lets the player always walk the aim off.")]
    [Range(0f, 0.95f)] public float strength = 0.85f;

    [Tooltip("How far a robot can be and still be worth helping onto, metres.")]
    public float range = 220f;

    [Tooltip("Roughly the right way, in degrees. A push this far off the " +
             "target's screen direction still counts as aiming at it.")]
    public float directionTolerance = 60f;

    [Tooltip("Steering this far away from a locked target drops the lock.")]
    public float releaseTolerance = 110f;

    [Tooltip("Player turn rate that counts as a full push, degrees/second. " +
             "Below it the assist eases in, so a nudge is still a nudge.")]
    public float fullEffortDegreesPerSecond = 18f;

    [Tooltip("Ceiling on how fast the assist itself may swing the view.")]
    public float maxAssistDegreesPerSecond = 200f;

    /// <summary>
    /// How far outside the lens a robot may sit and still be acquired, as a
    /// fraction of the viewport. Screen space rather than a fixed angle so it
    /// tracks the zoom by itself: scoped in, only what is nearly in the lens
    /// can be locked, which is what "aim at that one" means at 3.5x.
    /// </summary>
    const float AcquireMargin = 0.18f;

    /// <summary>Wider than acquisition, so a lock survives a wobble.</summary>
    const float HoldMargin = 0.55f;

    /// <summary>
    /// Inside this radius of the crosshair, direction agreement is not required
    /// to acquire. "Roughly the right way" has no meaning for a target already
    /// under the reticle, and demanding it there is what would make the assist
    /// let go every time the player centred one.
    /// </summary>
    const float CentredRadius = 0.05f;

    /// <summary>Below this the player is not really steering, so nothing happens.</summary>
    const float IdleDegreesPerSecond = 1.5f;

    /// <summary>Enemy list refresh. The scan is a scene-wide query; the maths off it is not.</summary>
    const float RescanSeconds = 0.12f;

    /// <summary>The robot currently being helped onto, or null. Read by the scope overlay.</summary>
    public EnergyShield Target { get; private set; }

    /// <summary>Where the assist is aiming, for the overlay's bracket.</summary>
    public Vector3 TargetPoint => Target != null ? WeaponUtil.Center(Target) : Vector3.zero;

    /// <summary>0-1, how much of the aim the assist is taking right now. Drives the bracket's glow.</summary>
    public float Engagement { get; private set; }

    readonly List<EnergyShield> _candidates = new List<EnergyShield>();
    float _nextScan;

    Camera _camera;
    SniperScope _sniper;
    EnergyShield _own;

    void Awake()
    {
        _camera = GetComponentInChildren<Camera>(true);
        _sniper = GetComponent<SniperScope>();
        _own = GetComponent<EnergyShield>();
    }

    /// <summary>Assist is the scope's: pressing SNIPE is the player asking for it.</summary>
    bool Enabled => _sniper != null && _sniper.IsScoped && _camera != null;

    /// <summary>
    /// Correct one frame of look input. <paramref name="lookDelta"/> is degrees
    /// this frame in the same (yaw, pitch) convention CharacterMotor.AddLook
    /// takes — positive x swings right, positive y looks up — which is also
    /// exactly the screen direction the crosshair travels, so the player's push
    /// and a target's screen offset can be compared directly.
    /// </summary>
    public Vector2 Steer(Vector2 lookDelta, float deltaTime)
    {
        Engagement = 0f;
        if (!Enabled || deltaTime <= 0f)
        {
            Target = null;
            return lookDelta;
        }

        float rate = lookDelta.magnitude / deltaTime;
        if (rate < IdleDegreesPerSecond)
        {
            // Hands still. The lock is kept — the bracket should not blink out
            // between pushes — but nothing is steered, which is property 1.
            if (Target != null && !StillValid(Target))
                Target = null;
            return lookDelta;
        }

        Vector2 push = lookDelta / lookDelta.magnitude;
        Target = ChooseTarget(push);
        if (Target == null)
            return lookDelta;

        Vector2 error = AimError(Target);
        Vector2 ideal = Vector2.ClampMagnitude(error, maxAssistDegreesPerSecond * deltaTime);

        float effort = Mathf.Clamp01(rate / Mathf.Max(1f, fullEffortDegreesPerSecond));
        Engagement = strength * effort;
        return Vector2.Lerp(lookDelta, ideal, Engagement);
    }

    /// <summary>
    /// Keep the lock if it is still worth keeping, otherwise find the robot the
    /// player's push is pointing at.
    ///
    /// Hysteresis is the whole job here: acquiring inside a tight cone and
    /// releasing only outside a much wider one is what stops the assist
    /// flickering between two robots standing near each other, which reads as
    /// the aim being fought over rather than helped.
    /// </summary>
    EnergyShield ChooseTarget(Vector2 push)
    {
        Rescan();

        if (Target != null && StillValid(Target))
        {
            Vector2 screen = ScreenDirection(Target, out float radius);
            bool onScreen = radius < HoldMargin + 0.5f;
            bool centred = radius < CentredRadius;
            float away = screen == Vector2.zero ? 0f : Vector2.Angle(push, screen);
            if (onScreen && (centred || away < releaseTolerance))
                return Target;
        }

        EnergyShield best = null;
        float bestScore = float.MaxValue;
        foreach (var shield in _candidates)
        {
            if (shield == null)
                continue;
            // Project first, raycast second. Scoped in, most of the roster is
            // off the lens entirely, and those cost a matrix multiply here
            // instead of a physics query.
            Vector2 screen = ScreenDirection(shield, out float radius);
            if (radius > 0.5f + AcquireMargin)
                continue;
            if (radius > CentredRadius && Vector2.Angle(push, screen) > directionTolerance)
                continue;
            if (!StillValid(shield))
                continue;
            // Nearest the crosshair wins. Among robots the push agrees with,
            // that is the one the player is looking at.
            if (radius < bestScore)
            {
                bestScore = radius;
                best = shield;
            }
        }
        return best;
    }

    /// <summary>
    /// Degrees of yaw and pitch between where the view points and the target's
    /// chest — the delta that would land the crosshair on it exactly.
    ///
    /// Measured in the CAMERA's frame, which is the body's yaw with the head's
    /// pitch already in it, so the "yaw" here is not quite a world yaw once the
    /// view is tilted. Left that way deliberately: this runs every frame as a
    /// feedback loop onto a shrinking error, so the approximation converges out
    /// rather than accumulating, and the alternative is a spherical solve that
    /// would be exact about a value the next frame recomputes anyway.
    /// </summary>
    Vector2 AimError(EnergyShield shield)
    {
        Vector3 local = _camera.transform.InverseTransformDirection(
            WeaponUtil.Center(shield) - _camera.transform.position);
        float flat = new Vector2(local.x, local.z).magnitude;
        return new Vector2(
            Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg,
            Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg);
    }

    /// <summary>
    /// Which way the target lies from the crosshair, in screen terms, plus how
    /// far out it is as a fraction of the viewport (0 = dead centre, 0.5 = the
    /// edge). Returned together because both come from the same projection and
    /// every caller wants both.
    /// </summary>
    Vector2 ScreenDirection(EnergyShield shield, out float radius)
    {
        Vector3 viewport = _camera.WorldToViewportPoint(WeaponUtil.Center(shield));
        if (viewport.z <= 0f)
        {
            // Behind the camera. WorldToViewportPoint mirrors those onto the
            // screen, so without this a robot at the player's back reads as a
            // perfectly good target dead ahead.
            radius = float.MaxValue;
            return Vector2.zero;
        }
        var offset = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        // Corrected for aspect, so "how far out" means the same left-right as
        // up-down; the lens is round even when the screen is not.
        float aspect = _camera.aspect > 0.01f ? _camera.aspect : 1f;
        radius = new Vector2(offset.x * aspect, offset.y).magnitude;
        return offset.sqrMagnitude < 1e-8f ? Vector2.zero : offset.normalized;
    }

    /// <summary>Alive, on the other team, in range, and actually visible from the lens.</summary>
    bool StillValid(EnergyShield shield)
    {
        if (shield == null || shield.IsDown || !shield.gameObject.activeInHierarchy)
            return false;
        Vector3 eye = _camera.transform.position;
        Vector3 center = WeaponUtil.Center(shield);
        Vector3 to = center - eye;
        float distance = to.magnitude;
        if (distance > range || distance < 0.01f)
            return false;
        // Through cover is not "roughly the right direction", it is a wall.
        // Assisting onto a robot the player cannot see would swing the view at
        // something invisible, which reads as the camera glitching.
        if (Physics.Raycast(eye, to / distance, out RaycastHit hit, distance, ~0,
                QueryTriggerInteraction.Ignore))
            return hit.transform.root == shield.transform.root;
        return true;
    }

    /// <summary>
    /// Refresh the enemy list on a timer. The per-frame maths runs off live
    /// transforms, so only membership goes stale between scans — a robot that
    /// de-rezzes mid-lock is caught by StillValid the same frame regardless.
    /// </summary>
    void Rescan()
    {
        if (Time.time < _nextScan)
            return;
        _nextScan = Time.time + RescanSeconds;

        _candidates.Clear();
        int team = _own != null ? _own.teamId : 0;
        // FindEnemies hands back a shared scratch list, so it has to be copied
        // before the next caller clears it under us.
        foreach (var shield in WeaponUtil.FindEnemies(team))
            _candidates.Add(shield);
    }
}

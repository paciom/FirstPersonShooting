using UnityEngine;

/// <summary>
/// The player's close-range martial arts move: a fast forward dash carrying a
/// three-strike combo — two crossing hooks and a finishing overhead — that
/// damages and shoves every enemy caught inside arm's reach.
///
/// WHY VFX AND NOT ANIMATION. The player's own robot is shadows-only in first
/// person (FirstPersonBody), so a punch clip played on the rig would be a show
/// nobody sees. What the player CAN see is the world: glowing slash arcs
/// sweeping across the view, the lunge itself, and enemies bursting and
/// staggering back. Those three carry the move.
///
/// The dash rides <see cref="CharacterMotor.externalVelocity"/> — the same
/// channel knockback uses — overwritten every frame of the lunge so it decays
/// on OUR schedule (fast in, braking out), then released. Gravity and walls
/// still apply: this is a lunge, not a teleport, and it will not carry the
/// player through cover or off a catwalk any faster than running would.
///
/// Triggered by the F key and the on-screen MARTIAL ARTS button; both routes
/// land on <see cref="TryStrike"/>, which owns every refusal (cooldown, tank
/// form, frozen, de-rezzed) so the input sites stay dumb.
/// </summary>
public class MartialArts : MonoBehaviour
{
    [Tooltip("Metres per second at the start of the lunge; brakes to zero over the combo.")]
    public float dashSpeed = 16f;

    [Tooltip("Damage per landed strike. Three strikes land over the combo.")]
    public float strikeDamage = 16f;

    [Tooltip("Reach of a strike from the robot's chest.")]
    public float range = 2.6f;

    [Tooltip("Seconds between combos.")]
    public float cooldown = 2.2f;

    /// <summary>The combo: two hooks and an overhead, spread across the lunge.</summary>
    static readonly float[] StrikeTimes = { 0.08f, 0.24f, 0.42f };

    /// <summary>Total combo length — a touch after the last strike lands.</summary>
    const float ComboSeconds = 0.55f;

    /// <summary>Enemies inside this frontal cone get hit (dot of flat directions).</summary>
    const float ConeCos = 0.45f;   // ~63° either side: a brawl swing, not a rifle

    static readonly Color SlashColor = new Color(0.35f, 0.95f, 1f);

    CharacterMotor _motor;
    TransformMode _vehicle;
    EnergyShield _shield;
    StatusEffects _status;

    float _comboT = float.MaxValue;   // time into the running combo
    int _strikesDone;
    float _readyAt;

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _vehicle = GetComponent<TransformMode>();
        _shield = GetComponent<EnergyShield>();
    }

    /// <summary>Start the combo. False when refused — cooling down, folded, frozen or down.</summary>
    public bool TryStrike()
    {
        if (Time.time < _readyAt || _comboT < ComboSeconds)
            return false;
        if (_motor == null)
            return false;
        if (_vehicle != null && (_vehicle.IsVehicle || _vehicle.IsBusy))
            return false;
        if (_shield != null && _shield.IsDown)
            return false;
        if (_status == null)
            _status = GetComponent<StatusEffects>();
        if (_status != null && _status.IsFrozen)
            return false;

        _comboT = 0f;
        _strikesDone = 0;
        _readyAt = Time.time + cooldown;
        return true;
    }

    void Update()
    {
        if (_comboT >= float.MaxValue)
            return;

        float previous = _comboT;
        _comboT += Time.deltaTime;

        if (previous < ComboSeconds)
        {
            // The lunge: full speed off the line, braking through the combo so
            // the finisher lands from a stand, not a drive-by.
            float brake = 1f - Mathf.Clamp01(_comboT / ComboSeconds);
            Vector3 forward = FlatForward();
            _motor.externalVelocity = forward * (dashSpeed * brake);

            while (_strikesDone < StrikeTimes.Length && _comboT >= StrikeTimes[_strikesDone])
            {
                Strike(_strikesDone);
                _strikesDone++;
            }
        }

        if (previous < ComboSeconds && _comboT >= ComboSeconds)
        {
            // Release the knockback channel the moment the combo ends, or the
            // leftover lunge keeps sliding the player like a hit they took.
            _motor.externalVelocity = Vector3.zero;
            _comboT = float.MaxValue;
        }
    }

    Vector3 FlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward;
    }

    /// <summary>
    /// One swing: damage and shove everything in the frontal cone, crack any
    /// prop at fist height, and draw the arc the fist travelled.
    /// </summary>
    void Strike(int index)
    {
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Vector3 forward = FlatForward();
        int team = _shield != null ? _shield.teamId : 0;

        foreach (var enemy in WeaponUtil.FindEnemies(team))
        {
            Vector3 to = WeaponUtil.Center(enemy) - origin;
            if (to.magnitude > range)
                continue;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 1e-4f && Vector3.Dot(flat.normalized, forward) < ConeCos)
                continue;

            enemy.TakeHit(strikeDamage, WeaponUtil.Center(enemy), transform);
            VfxUtil.ImpactBurst(WeaponUtil.Center(enemy), SlashColor);

            // The stagger is half the point: a robot that eats a combo without
            // moving reads as the punches not landing. Through StatusEffects,
            // which routes the shove correctly for motor-driven players and
            // navmesh-driven bots alike.
            StatusEffects.Get(enemy.transform).AddImpulse(forward * 6.5f + Vector3.up * 2f);
        }

        // Fists work on scenery too — a crate or a mine in the way gets the hit.
        Vector3 fist = origin + forward * (range * 0.6f);
        foreach (var col in Physics.OverlapSphere(fist, range * 0.55f, ~0, QueryTriggerInteraction.Ignore))
            if (col.GetComponentInParent<EnergyShield>() == null)
                WeaponUtil.DamageProp(col, strikeDamage, fist);

        DrawSlash(index, origin, forward);
    }

    /// <summary>
    /// The visible swing: a glowing arc swept in front of the camera — right
    /// hook, left hook, then an overhead chop for the finisher.
    /// </summary>
    void DrawSlash(int index, Vector3 origin, Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        var points = new Vector3[9];

        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (points.Length - 1f) - 0.5f;   // -0.5 .. 0.5 across the arc
            Vector3 span, lift;
            if (index == 2)
            {
                // Overhead: top to bottom, slightly curved outward.
                span = Vector3.up * (-t * 2.2f);
                lift = right * Mathf.Sin(t * Mathf.PI) * 0.3f;
            }
            else
            {
                // Hooks: horizontal sweep, alternating direction, rising a little.
                float side = index == 0 ? 1f : -1f;
                span = right * (t * 2.4f * side);
                lift = Vector3.up * (0.25f - t * t * 1.1f);
            }
            points[i] = origin + forward * (1.1f + Mathf.Cos(t * Mathf.PI) * 0.35f) + span + lift;
        }

        FadingLine.SpawnPath(points, SlashColor, 0.09f, 0.18f, 2.2f);
        FadingLine.SpawnPath(points, Color.white, 0.035f, 0.12f, 2.4f);
    }
}

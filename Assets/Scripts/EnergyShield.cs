using System;
using UnityEngine;

/// <summary>
/// Kid-friendly damage model: laser hits drain an energy shield, never health.
/// At zero the character "de-rezzes" (dissolves into light) and later
/// re-materializes — death never exists in the fiction.
/// </summary>
public class EnergyShield : MonoBehaviour
{
    [Header("Shield")]
    public float maxShield = 100f;
    public float regenDelay = 3f;
    public float regenPerSecond = 25f;

    [Header("Team")]
    public int teamId;

    public float Current { get; private set; }
    public bool IsDown { get; private set; }
    public float Normalized => Current / maxShield;

    /// <summary>Root of whoever landed the most recent hit (for score attribution).</summary>
    public Transform LastAttacker { get; private set; }

    /// <summary>(damage, worldHitPoint)</summary>
    public event Action<float, Vector3> OnDamaged;
    public event Action OnDeRezzed;
    public event Action OnRematerialized;

    float _lastHitTime = float.NegativeInfinity;

    // Overshield (airdrop pickup): maxShield is raised temporarily, so the HUD
    // and floating bars keep reading correctly off Normalized. -1 = none active.
    float _baseMaxShield = -1f;
    float _overshieldUntil;

    /// <summary>True while an airdrop overshield is inflating maxShield.</summary>
    public bool HasOvershield => _baseMaxShield >= 0f;

    [Header("Transformation")]
    [Tooltip("Damage multiplier while mid-fold. Transforming is a commitment, " +
             "and this is what it costs to be caught in it.")]
    public float foldingDamageMultiplier = 2f;

    [Tooltip("Damage multiplier on a vehicle's frontal arc — its heavy armour.")]
    public float vehicleFrontMultiplier = 0.6f;

    [Tooltip("Damage multiplier on a vehicle's exposed rear engine.")]
    public float vehicleRearMultiplier = 1.8f;

    [Tooltip("How far round the front and back the armour and weak spot reach. " +
             "0.35 leaves a neutral band down each side.")]
    [Range(0.05f, 0.9f)] public float facingThreshold = 0.35f;

    TransformMode _transform;

    /// <summary>
    /// Multiplier the most recent hit was scaled by. Lets VFX and the HUD show
    /// WHY a hit landed harder — a weak-spot hit nobody can see reads as the
    /// damage numbers lying.
    /// </summary>
    public float LastDamageMultiplier { get; private set; } = 1f;

    void Awake()
    {
        Current = maxShield;
        _transform = GetComponent<TransformMode>();
    }

    /// <summary>
    /// How hard a hit lands, given the form and where it came from.
    ///
    /// Mid-fold beats everything: the panels are open, so a robot caught
    /// transforming takes the full penalty and gets no directional armour. It
    /// is the cost that stops transforming being free, and it is why the AI
    /// picking its moment matters.
    ///
    /// A standing robot has no facing armour at all. Only the vehicle trades
    /// its frontal arc against its engine deck, which is what makes flanking
    /// the answer to a tank rather than simply out-shooting it.
    /// </summary>
    public float DamageMultiplierFor(Vector3 hitPoint)
    {
        if (_transform == null)
            return 1f;
        if (_transform.IsBusy)
            return foldingDamageMultiplier;
        if (!_transform.IsVehicle)
            return 1f;

        // Flattened: a shot from directly above should not read as a rear hit
        // just because the arc came down steeply.
        Vector3 toHit = hitPoint - transform.position;
        toHit.y = 0f;
        if (toHit.sqrMagnitude < 1e-4f)
            return 1f;

        float facing = Vector3.Dot(transform.forward, toHit.normalized);
        if (facing >= facingThreshold)
            return vehicleFrontMultiplier;
        if (facing <= -facingThreshold)
            return vehicleRearMultiplier;
        return 1f;                       // broadside: neither armoured nor weak
    }

    public void TakeHit(float damage, Vector3 hitPoint, Transform attacker = null)
    {
        if (IsDown)
            return;

        LastDamageMultiplier = DamageMultiplierFor(hitPoint);
        damage *= LastDamageMultiplier;

        LastAttacker = attacker;
        Current = Mathf.Max(0f, Current - damage);
        _lastHitTime = Time.time;
        OnDamaged?.Invoke(damage, hitPoint);

        if (Current <= 0f)
        {
            IsDown = true;
            DropOvershield();
            OnDeRezzed?.Invoke();
        }
    }

    /// <summary>Top the shield back up (Repair Pack airdrop). No effect while de-rezzed.</summary>
    public void Restore(float amount)
    {
        if (IsDown || amount <= 0f)
            return;
        Current = Mathf.Min(maxShield, Current + amount);
    }

    /// <summary>Raise the ceiling by <paramref name="extra"/> for a while and fill to it.</summary>
    public void AddOvershield(float extra, float duration)
    {
        if (IsDown || extra <= 0f)
            return;
        if (_baseMaxShield < 0f)
            _baseMaxShield = maxShield;
        maxShield = _baseMaxShield + extra;
        Current = maxShield;
        _overshieldUntil = Mathf.Max(_overshieldUntil, Time.time + duration);
    }

    void DropOvershield()
    {
        if (_baseMaxShield < 0f)
            return;
        maxShield = _baseMaxShield;
        _baseMaxShield = -1f;
        _overshieldUntil = 0f;
        Current = Mathf.Min(Current, maxShield);
    }

    public void Rematerialize()
    {
        DropOvershield();
        Current = maxShield;
        IsDown = false;
        OnRematerialized?.Invoke();
    }

    void Update()
    {
        if (HasOvershield && Time.time >= _overshieldUntil)
            DropOvershield();

        if (!IsDown && Current < maxShield && Time.time - _lastHitTime > regenDelay)
            Current = Mathf.Min(maxShield, Current + regenPerSecond * Time.deltaTime);
    }
}

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

    [Tooltip("Online PvP: this shield mirrors a remote player's, and only their " +
             "broadcasts may change it. Local hits become feedback-only.")]
    [HideInInspector] public bool remoteProxy;

    [Tooltip("Nothing lands at all while this is set — no damage, and no hit " +
             "feedback either, so the shots visibly do nothing. The respawn " +
             "grace in an arcade mode: a character that has just re-materialized " +
             "must not be killed by the shell that was already in the air when " +
             "it went down.")]
    [HideInInspector] public bool invulnerable;

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
        if (IsDown || invulnerable)
            return;

        LastDamageMultiplier = DamageMultiplierFor(hitPoint);
        damage *= LastDamageMultiplier;

        // A remote player's mirror: their client decides what their shield is
        // worth; ours only shows the hit landing. Without this, local
        // prediction and their broadcasts would fight over Current — and a
        // predicted de-rez is unrecoverable when the owner disagrees.
        if (remoteProxy)
        {
            OnDamaged?.Invoke(damage, hitPoint);
            return;
        }

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

    /// <summary>
    /// Online PvP reconciliation: adopt the owning client's broadcast value.
    /// Ignored while down — the de-rez cycle owns the shield until it ends.
    /// </summary>
    public void NetworkSet(float current, float max)
    {
        if (IsDown)
            return;
        if (max > 0f)
            maxShield = max;
        Current = Mathf.Clamp(current, 0f, maxShield);
    }

    /// <summary>
    /// Online PvP: the owning client reported their own de-rez. Bypasses the
    /// remoteProxy guard — this is the one legitimate remote kill path.
    /// </summary>
    public void NetworkForceDown()
    {
        if (IsDown)
            return;
        Current = 0f;
        IsDown = true;
        DropOvershield();
        OnDeRezzed?.Invoke();
    }

    /// <summary>
    /// True while a gunfight (Player v AI, AI v AI, Online) is the active mode.
    /// Gunfight shields do NOT auto-heal: damage there is meant to be spent,
    /// not waited out — ducking behind a crate for three seconds should not
    /// undo a fight. Recovery comes from Repair Pack airdrops and the full
    /// refill on re-materializing. Every other mode (Dogfight, Commander,
    /// Tank Raid…) keeps the regen its balance was tuned around.
    /// </summary>
    static bool GunfightActive
    {
        get
        {
            var controller = GameModeController.Instance;
            if (controller == null)
                return false;
            var mode = controller.Mode;
            return mode == GameMode.PlayerVsAI || mode == GameMode.AIvAI
                || mode == GameMode.OnlinePvP;
        }
    }

    void Update()
    {
        if (HasOvershield && Time.time >= _overshieldUntil)
            DropOvershield();

        // A remote player's mirror never regenerates on its own: their client
        // owns that value in both directions, and local regen would creep the
        // enemy's bar back up between broadcasts.
        if (!remoteProxy && !IsDown && !GunfightActive
            && Current < maxShield && Time.time - _lastHitTime > regenDelay)
            Current = Mathf.Min(maxShield, Current + regenPerSecond * Time.deltaTime);
    }
}

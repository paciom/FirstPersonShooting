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

    void Awake()
    {
        Current = maxShield;
    }

    public void TakeHit(float damage, Vector3 hitPoint, Transform attacker = null)
    {
        if (IsDown)
            return;

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

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

    /// <summary>(damage, worldHitPoint)</summary>
    public event Action<float, Vector3> OnDamaged;
    public event Action OnDeRezzed;
    public event Action OnRematerialized;

    float _lastHitTime = float.NegativeInfinity;

    void Awake()
    {
        Current = maxShield;
    }

    public void TakeHit(float damage, Vector3 hitPoint)
    {
        if (IsDown)
            return;

        Current = Mathf.Max(0f, Current - damage);
        _lastHitTime = Time.time;
        OnDamaged?.Invoke(damage, hitPoint);

        if (Current <= 0f)
        {
            IsDown = true;
            OnDeRezzed?.Invoke();
        }
    }

    public void Rematerialize()
    {
        Current = maxShield;
        IsDown = false;
        OnRematerialized?.Invoke();
    }

    void Update()
    {
        if (!IsDown && Current < maxShield && Time.time - _lastHitTime > regenDelay)
            Current = Mathf.Min(maxShield, Current + regenPerSecond * Time.deltaTime);
    }
}

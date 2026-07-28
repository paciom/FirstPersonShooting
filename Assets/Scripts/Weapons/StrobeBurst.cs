using System.Collections;
using UnityEngine;

/// <summary>
/// Pulsed light: three-round bursts of dazzling white-cyan flashes with
/// lens-flare muzzle strobes.
/// </summary>
public class StrobeBurst : Weapon
{
    public float burstsPerSecond = 1.6f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Strobe Burst";
        color = new Color(0.75f, 0.95f, 1f);
        if (damage == 20f) damage = 9f;   // ×3 per burst
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / burstsPerSecond;
        StartCoroutine(FireBurst(direction.normalized));
    }

    IEnumerator FireBurst(Vector3 direction)
    {
        for (int i = 0; i < 3; i++)
        {
            var spec = new BoltSpec
            {
                speed = 42f,         // fast trails smear into streamers on slow frames
                damage = damage,
                color = i % 2 == 0 ? Color.white : color,
                size = 0.09f,
                trailTime = 0.08f,
            };
            GenericBolt.Spawn(muzzle.position, direction, spec, TeamId, ownerRoot);
            FlashMuzzle(5f);   // rapid strobes
            yield return new WaitForSeconds(0.07f);
        }
    }
}

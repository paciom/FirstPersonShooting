using UnityEngine;

/// <summary>
/// The all-rounder: fires visible glowing laser bolts at a steady cadence.
/// Projectiles have travel time on purpose — they read far better on camera
/// than hitscan, for players and viewers alike.
/// </summary>
public class LaserBlaster : Weapon
{
    [Header("Laser Blaster")]
    public float shotsPerSecond = 5f;
    public float boltSpeed = 40f;

    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Laser Blaster";
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;

        _nextFireTime = Time.time + 1f / shotsPerSecond;
        LaserBolt.Spawn(muzzle.position, direction.normalized, boltSpeed, damage, color, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}

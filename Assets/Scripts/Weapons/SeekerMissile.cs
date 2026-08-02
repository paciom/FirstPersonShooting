using UnityEngine;

/// <summary>
/// The straight-up guided missile: one real rocket that picks the nearest robot
/// and bends after it all the way in. Slower than a bolt on purpose — you watch
/// it chase, and so does whoever it is chasing.
/// </summary>
public class SeekerMissile : Weapon
{
    public float shotsPerSecond = 1.2f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Seeker Missile";
        color = new Color(1f, 0.55f, 0.2f);
        if (damage == 20f) damage = 26f;
        preferredRange = 26f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 26f,
            damage = damage,
            color = color,
            size = 0.14f,
            glow = 1.6f,          // a metal body, not an energy bolt
            homingDegreesPerSecond = 150f,
            homingRange = 32f,
            lifetime = 5f,
            trailTime = 0.45f,    // the exhaust IS the read at this speed
            trailWidth = 0.22f,
            trailGlow = 1.7f,
            splashRadius = 2.2f,
            splashScale = 1.1f,
        };
        var bolt = GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        MissileModels.Dress(bolt, 0, 1.0f);
        FlashMuzzle(4f);
    }
}

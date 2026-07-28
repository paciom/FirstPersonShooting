using UnityEngine;

/// <summary>
/// Elastic energy: a pink rubber ball that ricochets wildly around the arena,
/// picking up SPEED with every bounce.
/// </summary>
public class BouncyBallCannon : Weapon
{
    public float shotsPerSecond = 1.1f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Bouncy Ball Cannon";
        color = new Color(1f, 0.45f, 0.75f);
        if (damage == 20f) damage = 16f;
        preferredRange = 17f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 20f,
            gravity = -16f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.32f,
            glow = 1.8f,   // pink rubber ball — keep it pink
            bounces = 7,
            bounceSpeedGain = 1.12f,   // still accelerates, but tops out ~45 m/s
                                       // (1.18^7 hit ~66 and smeared the trail into ribbons)
            lifetime = 7f,
            trailTime = 0.09f,
            trailWidth = 0.13f,
            trailGlow = 1.4f,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}

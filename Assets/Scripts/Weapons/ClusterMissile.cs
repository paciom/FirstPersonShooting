using UnityEngine;

/// <summary>
/// Flies straight, then comes apart: the moment it touches anything it throws
/// six glowing bomblets back out of the impact, each with its own little burst.
/// Aim it at the floor of a crowd rather than at one robot.
/// </summary>
public class ClusterMissile : Weapon
{
    public float shotsPerSecond = 0.8f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Cluster Missile";
        color = new Color(0.6f, 1f, 0.45f);
        if (damage == 20f) damage = 22f;
        preferredRange = 22f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        // The bomblets ride GenericBolt's own split path, so they are plain
        // glowing spheres rather than six more missile meshes — at this size
        // and lifetime the mesh would never be read anyway.
        var bomblet = new BoltSpec
        {
            speed = 13f,
            gravity = -9f,
            damage = damage * 0.4f,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.16f,
            glow = 2f,
            lifetime = 1.6f,
            trailTime = 0.2f,
            trailWidth = 0.08f,
            trailGlow = 1.6f,
            splashRadius = 1.5f,
            splashScale = 0.7f,
            fullImpactBurst = false,
        };

        var spec = new BoltSpec
        {
            speed = 28f,
            damage = damage,
            color = color,
            size = 0.14f,
            glow = 1.6f,
            lifetime = 4.5f,
            trailTime = 0.4f,
            trailWidth = 0.2f,
            trailGlow = 1.7f,
            splashRadius = 2f,
            splitCount = 6,
            splitSpread = 55f,
            splitSpec = bomblet,
        };
        var bolt = GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        MissileModels.Dress(bolt, 2, 1.0f);
        FlashMuzzle(4f);
    }
}

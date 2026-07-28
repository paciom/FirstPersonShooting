using UnityEngine;

/// <summary>
/// Nanobots: lobs a pod that bursts into a murmuration of tiny glowing
/// firefly-bots, each homing in to nibble the nearest enemy.
/// </summary>
public class SwarmHive : Weapon
{
    public float shotsPerSecond = 0.5f;
    public int motes = 8;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Swarm Hive";
        color = new Color(0.6f, 1f, 0.4f);
        if (damage == 20f) damage = 4f;   // per mote
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var pod = new BoltSpec
        {
            speed = 18f,
            gravity = -10f,
            damage = 6f,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.3f,
            glow = 1.8f,   // green pod, not a white ball
            onImpact = ReleaseSwarm,
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.2f).normalized,
            pod, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }

    void ReleaseSwarm(GenericBolt bolt, RaycastHit hit)
    {
        bolt.spec.onImpact = null;
        VfxUtil.ImpactBurst(hit.point + hit.normal * 0.2f, color);
        for (int i = 0; i < motes; i++)
        {
            // Scatter upward, then home hard — the murmuration look.
            Vector3 dir = (hit.normal + Random.insideUnitSphere).normalized;
            var mote = new BoltSpec
            {
                speed = Random.Range(11f, 16f),
                damage = damage,
                color = color,
                shape = PrimitiveType.Sphere,
                size = 0.09f,
                glow = 2.4f,   // green fireflies
                lifetime = 3.5f,
                homingDegreesPerSecond = 260f,
                homingRange = 22f,
                trailTime = 0.2f,
                trailWidth = 0.05f,
                trailGlow = 1.4f,
                addLight = false,
                fullImpactBurst = false,   // nibbles, not explosions
            };
            GenericBolt.Spawn(hit.point + hit.normal * 0.3f, dir, mote, TeamId, ownerRoot);
        }
    }
}

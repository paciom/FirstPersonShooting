using UnityEngine;

/// <summary>
/// Molten energy: arcs a dripping glob that bursts into four smaller globs on
/// landing, leaving glowing puddles that cool and fade.
/// </summary>
public class MagmaMortar : Weapon
{
    public float shotsPerSecond = 0.8f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Magma Mortar";
        color = new Color(1f, 0.4f, 0.1f);
        if (damage == 20f) damage = 30f;
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 17f,
            gravity = -14f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.45f,
            glow = 2.8f,        // molten orange, not yellow-white
            trailTime = 0.3f,
            trailWidth = 0.2f,
            trailGlow = 1.5f,   // dripping tail stays orange
            splashRadius = 2.5f,
            onImpact = Shatter,
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.3f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(4f);
    }

    void Shatter(GenericBolt bolt, RaycastHit hit)
    {
        bolt.spec.onImpact = null;
        // Glowing puddle that cools away.
        GlowBlobEntity.Spawn(hit.point + hit.normal * 0.05f, color, 0.55f, 3f);

        // Four smaller globs hop outward.
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = (hit.normal + Random.insideUnitSphere * 0.7f).normalized;
            var child = new BoltSpec
            {
                speed = 8f,
                gravity = -14f,
                damage = damage * 0.35f,
                color = color,
                shape = PrimitiveType.Sphere,
                size = 0.22f,
                glow = 2.4f,        // hot droplets, still orange
                trailTime = 0.25f,
                trailGlow = 1.5f,
                splashRadius = 1.6f,
                splashScale = 0.7f,
                lifetime = 2f,
                onImpact = (b, h) => GlowBlobEntity.Spawn(h.point + h.normal * 0.05f, color, 0.3f, 2f),
            };
            GenericBolt.Spawn(hit.point + hit.normal * 0.2f, dir, child, TeamId, ownerRoot);
        }
    }
}

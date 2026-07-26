using UnityEngine;

/// <summary>
/// Bioluminescence: lobs sticky glowing blobs that cling where they land and
/// breathe soft green light over the arena.
/// </summary>
public class GlowwormLauncher : Weapon
{
    public float shotsPerSecond = 2f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Glowworm Launcher";
        color = new Color(0.5f, 1f, 0.35f);
        if (damage == 20f) damage = 14f;
        preferredRange = 16f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 18f,
            gravity = -12f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.22f,
            glow = 1.6f,   // stay green in flight — the default 4x blooms to a white ball
            // Short dim drip-trail, not a laser ribbon.
            trailTime = 0.12f,
            trailWidth = 0.07f,
            trailGlow = 1.2f,
            onImpact = (bolt, hit) =>
                GlowBlobEntity.Spawn(hit.point + hit.normal * 0.1f, bolt.spec.color, 0.35f, 6f),
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.15f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}

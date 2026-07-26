using UnityEngine;

/// <summary>
/// Refracted light: one white beam-bolt that splits into three rainbow bolts
/// when it strikes any surface.
/// </summary>
public class PrismSplitter : Weapon
{
    public float shotsPerSecond = 2.2f;
    float _nextFireTime;

    static readonly Color[] Rainbow =
    {
        new Color(1f, 0.25f, 0.25f),
        new Color(0.3f, 1f, 0.35f),
        new Color(0.3f, 0.45f, 1f),
    };

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Prism Splitter";
        color = Color.white;
        preferredRange = 22f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 38f,
            damage = damage,
            color = Color.white,
            size = 0.1f,
            trailTime = 0.08f,   // short — long trails smear on slow frames
            onImpact = SplitIntoRainbow,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }

    void SplitIntoRainbow(GenericBolt bolt, RaycastHit hit)
    {
        bolt.spec.onImpact = null;   // split only once
        Vector3 baseDir = Vector3.Reflect(bolt.Velocity.normalized, hit.normal);
        foreach (var hue in Rainbow)
        {
            Quaternion tilt = Quaternion.AngleAxis(Random.Range(10f, 40f), Random.onUnitSphere);
            var child = new BoltSpec
            {
                speed = 34f,
                damage = damage * 0.5f,
                color = hue,
                size = 0.08f,
                lifetime = 2f,
                glow = 2.2f,       // the rainbow is the point — don't bloom it white
                trailGlow = 1.6f,
            };
            GenericBolt.Spawn(hit.point + hit.normal * 0.15f, (tilt * baseDir).normalized, child, TeamId, ownerRoot);
        }
        // Floating prism shards at the split point.
        VfxUtil.ImpactBurst(hit.point + hit.normal * 0.1f, Color.white);
    }
}

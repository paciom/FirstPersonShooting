using UnityEngine;

/// <summary>
/// Crystallized ice: a burst of five glinting needles; misses embed in walls
/// as little icicles that sparkle, then melt.
/// </summary>
public class IcicleFlechette : Weapon
{
    public float shotsPerSecond = 1.4f;
    public int needles = 5;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Icicle Flechette";
        color = new Color(0.65f, 0.88f, 1f);   // visible ice-blue; near-white whites out
        if (damage == 20f) damage = 7f;   // per needle
        preferredRange = 15f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        for (int i = 0; i < needles; i++)
        {
            Vector3 dir = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-4f, 4f), 0f)
                * direction.normalized;
            var spec = new BoltSpec
            {
                speed = 32f,   // fast trails smear on slow frames
                damage = damage,
                color = color,
                size = 0.05f,
                glow = 2.4f,           // icy sliver, not a white bar
                trailTime = 0.05f,     // needle-thin whisker
                trailWidth = 0.035f,
                trailGlow = 1.4f,
                addLight = false,
                fullImpactBurst = false,   // 5 needles — glints, not explosions
                onImpact = EmbedIcicle,
            };
            GenericBolt.Spawn(muzzle.position, dir, spec, TeamId, ownerRoot);
        }
        FlashMuzzle(3f);
    }

    void EmbedIcicle(GenericBolt bolt, RaycastHit hit)
    {
        // Only walls/floor keep the icicle; shields just shatter the needle.
        if (hit.transform.root.GetComponent<EnergyShield>() != null)
            return;
        var icicle = WeaponUtil.GlowPrimitive(PrimitiveType.Capsule,
            hit.point + hit.normal * 0.12f, new Vector3(0.05f, 0.22f, 0.05f), color, 2.5f);
        icicle.transform.rotation = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(90f, 0f, 0f);
        Object.Destroy(icicle, 2f);   // melts away
    }
}

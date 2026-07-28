using UnityEngine;

/// <summary>
/// Polymer slime: hoses out a stream of jiggling purple goo. Enemies hit get
/// gummed up, and puddles on the floor stay slippery for a while.
/// </summary>
public class GooGusher : Weapon
{
    public float shotsPerSecond = 8f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Goo Gusher";
        color = new Color(0.7f, 0.3f, 1f);
        if (damage == 20f) damage = 3.5f;
        preferredRange = 11f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        Vector3 dir = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-3f, 3f), 0f)
            * (direction.normalized + Vector3.up * 0.08f).normalized;
        var spec = new BoltSpec
        {
            speed = 20f,
            gravity = -18f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = Random.Range(0.14f, 0.24f),
            glow = 1.5f,   // purple goo, not white-hot droplets
            trailTime = 0.15f,
            trailGlow = 1.3f,
            addLight = false,
            fullImpactBurst = false,   // 8 droplets/s — little plips, not blasts
            onShieldHit = (bolt, shield) =>
                StatusEffects.Get(shield.transform.root)?.ApplySlow(0.5f, 1.4f),
            onImpact = SplatPuddle,
        };
        GenericBolt.Spawn(muzzle.position, dir, spec, TeamId, ownerRoot);
        FlashMuzzle(2f);
    }

    void SplatPuddle(GenericBolt bolt, RaycastHit hit)
    {
        // Only some blobs leave a slick, and only on floor-ish surfaces.
        if (hit.normal.y < 0.5f || Random.value > 0.25f)
            return;

        // Splat fades with the slick — a purple mark that outlives its hazard
        // would read as danger that isn't there.
        WeaponUtil.PaintSplat(hit.point, hit.normal, color, 1.1f, 3.5f);
        var zone = EffectZone.Spawn(hit.point + Vector3.up * 0.2f, 1.8f, 3f, color,
            ZoneParticles.None, TeamId, ownerRoot, showDome: false);
        zone.affectAllTeams = true;   // your own goo is just as slippery
        zone.onCharacterTick = (z, shield) =>
            StatusEffects.Get(shield.transform.root)?.ApplySlow(0.45f, 0.4f);
    }
}

using UnityEngine;

/// <summary>
/// Hard-light frisbee: a neon disc trailing a TRON light-wall that hops from
/// enemy to enemy — each hit re-aims it at the next nearest target.
/// </summary>
public class RicochetDisc : Weapon
{
    public float shotsPerSecond = 1f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Ricochet Disc";
        color = new Color(0.2f, 1f, 1f);
        if (damage == 20f) damage = 15f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 30f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Cylinder,
            customScale = new Vector3(0.55f, 0.04f, 0.55f),
            glow = 2.4f,   // TRON-cyan disc, not white
            spinDegreesPerSecond = 900f,
            pierce = 4,                     // survives up to 4 targets
            lifetime = 4f,
            trailTime = 0.5f,               // the light wall
            trailWidth = 0.35f,
            onShieldHit = HopToNext,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }

    void HopToNext(GenericBolt bolt, EnergyShield justHit)
    {
        var next = WeaponUtil.NearestEnemy(bolt.transform.position, TeamId, 16f, justHit.transform.root);
        if (next == null)
            return;
        Vector3 dir = (WeaponUtil.Center(next) - bolt.transform.position).normalized;
        bolt.Velocity = dir * bolt.spec.speed;
        VfxUtil.ImpactBurst(bolt.transform.position, color);   // ping flash per hop
    }
}

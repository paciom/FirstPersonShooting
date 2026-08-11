using UnityEngine;

/// <summary>
/// Adhesive: a honey-colored bomb that glues nearby enemies' feet to the floor
/// with stretchy golden strands.
/// </summary>
public class GlueGrenade : Weapon
{
    public float shotsPerSecond = 0.6f;
    public float glueRadius = 3.2f;
    public float stuckSeconds = 2f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Glue Grenade";
        color = new Color(1f, 0.8f, 0.2f);
        if (damage == 20f) damage = 10f;
        preferredRange = 16f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 16f,
            gravity = -13f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.32f,
            glow = 1.7f,   // honey-gold, not a white ball
            trailTime = 0.2f,
            trailGlow = 1.4f,
            onImpact = Splash,
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.22f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }

    void Splash(GenericBolt bolt, RaycastHit hit)
    {
        bolt.spec.onImpact = null;
        VfxUtil.EnergyBurst(hit.point + hit.normal * 0.1f, color, 0.9f);
        WeaponUtil.PaintSplat(hit.point, hit.normal, color, 1.6f);

        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            if (Vector3.Distance(shield.transform.position, hit.point) > glueRadius)
                continue;
            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            StatusEffects.Get(shield.transform.root)?.ApplyStuck(stuckSeconds);
            // Stretchy strands from the splash to their boots.
            FadingLine.SpawnJagged(hit.point, shield.transform.position + Vector3.up * 0.2f,
                color, 0.06f, stuckSeconds * 0.6f, 0.25f, 5, 1.8f);   // golden strands
        }
    }
}

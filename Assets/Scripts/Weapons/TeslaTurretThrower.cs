using UnityEngine;

/// <summary>
/// Static electricity: tosses a small coil orb that plants itself where it
/// lands and zaps anything hostile that wanders close.
/// </summary>
public class TeslaTurretThrower : Weapon
{
    public float shotsPerSecond = 0.5f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Tesla Turret Thrower";
        color = new Color(0.35f, 0.85f, 1f);
        if (damage == 20f) damage = 8f;   // per zap, over the coil's lifetime
        preferredRange = 15f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 14f,
            gravity = -14f,
            damage = 5f,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.3f,
            glow = 2.5f,   // electric blue orb, not a white ball
            onImpact = PlantCoil,
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.25f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }

    void PlantCoil(GenericBolt bolt, RaycastHit hit)
    {
        // A little device, not a glowing pillar: dark base disc, thin glowing
        // stalk, electric orb on top with prongs that make the spin readable.
        Vector3 basePos = hit.point + hit.normal * 0.02f;
        var coilGo = new GameObject("TeslaCoil");
        coilGo.transform.position = basePos;

        var baseDisc = WeaponUtil.GlowPrimitive(PrimitiveType.Cylinder, basePos + Vector3.up * 0.08f,
            new Vector3(0.34f, 0.08f, 0.34f), new Color(0.09f, 0.10f, 0.14f), 1f);
        baseDisc.transform.SetParent(coilGo.transform, true);

        var stalk = WeaponUtil.GlowPrimitive(PrimitiveType.Cylinder, basePos + Vector3.up * 0.3f,
            new Vector3(0.06f, 0.16f, 0.06f), color, 1.6f);
        stalk.transform.SetParent(coilGo.transform, true);

        var orb = WeaponUtil.GlowPrimitive(PrimitiveType.Sphere, basePos + Vector3.up * 0.55f,
            Vector3.one * 0.24f, color, 2.3f);
        orb.transform.SetParent(coilGo.transform, true);

        for (int i = 0; i < 2; i++)
        {
            var prong = WeaponUtil.GlowPrimitive(PrimitiveType.Cube,
                basePos + Vector3.up * 0.55f + (i == 0 ? Vector3.left : Vector3.right) * 0.2f,
                new Vector3(0.16f, 0.04f, 0.04f), color, 1.8f);
            prong.transform.SetParent(coilGo.transform, true);
        }

        var coil = coilGo.AddComponent<TeslaCoilEntity>();
        coil.teamId = bolt.teamId;
        coil.ownerRoot = bolt.ownerRoot;
        coil.color = bolt.spec.color;
        coil.zapDamage = damage;
        VfxUtil.ImpactBurst(hit.point, color);
    }
}

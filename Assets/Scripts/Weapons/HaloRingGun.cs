using UnityEngine;

/// <summary>
/// Light rings: golden tori that sail forward and pass straight through
/// multiple enemies, sparkling wherever they touch.
/// </summary>
public class HaloRingGun : Weapon
{
    public float shotsPerSecond = 1.8f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Halo Ring Gun";
        color = new Color(1f, 0.85f, 0.35f);
        if (damage == 20f) damage = 16f;
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 28f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Quad,
            customScale = new Vector3(1.15f, 1.15f, 1f),
            pierce = 3,                    // passes through enemies
            trailTime = 0.18f,
            trailWidth = 0.1f,
            trailGlow = 1.5f,
        };
        Vector3 dir = direction.normalized;
        var bolt = GenericBolt.Spawn(muzzle.position, dir, spec, TeamId, ownerRoot);
        // An actual ring: the donut sprite on the quad, flying face-forward.
        // Toned gold (not the 4x default) so it stays a golden halo, not a
        // white blob. The additive shader is two-sided, so it reads from
        // behind too; edge-on it thins to a line, which is what a ring does.
        bolt.transform.rotation = Quaternion.LookRotation(dir);
        bolt.GetComponent<MeshRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.RingTexture, color, 1.7f);
        FlashMuzzle(3.5f);
    }
}

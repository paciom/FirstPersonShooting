using UnityEngine;

/// <summary>
/// Chromatic splatter: paint bombs that hurt AND redecorate — every burst
/// splats your team's color across floors and walls, so a long fight visibly
/// turns the arena into a scoreboard.
/// </summary>
public class PaintBomber : Weapon
{
    public float shotsPerSecond = 0.9f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Paint Bomber";
        // Paint in team colors: cyan for team 0, magenta for team 1.
        color = TeamId == 1 ? new Color(1f, 0.25f, 0.85f) : new Color(0.2f, 0.9f, 1f);
        if (damage == 20f) damage = 24f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 19f,
            gravity = -12f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.35f,
            glow = 1.7f,   // the bomb should be visibly team-colored paint
            trailTime = 0.25f,
            trailWidth = 0.25f,
            trailGlow = 1.5f,
            splashRadius = 3f,
            onImpact = SplatterEverything,
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.18f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }

    void SplatterEverything(GenericBolt bolt, RaycastHit hit)
    {
        bolt.spec.onImpact = null;
        // The big center splat plus paint thrown onto every nearby surface.
        WeaponUtil.PaintSplat(hit.point, hit.normal, color, 2.2f);
        for (int i = 0; i < 10; i++)
        {
            Vector3 dir = (hit.normal + Random.insideUnitSphere * 1.2f).normalized;
            if (Physics.Raycast(hit.point + hit.normal * 0.1f, dir, out RaycastHit spray, 5f,
                    ~0, QueryTriggerInteraction.Ignore)
                && spray.transform.root.GetComponent<EnergyShield>() == null)
                WeaponUtil.PaintSplat(spray.point, spray.normal, color, Random.Range(0.5f, 1.2f));
        }
        // Droplet fountain.
        VfxUtil.SpawnBurst(hit.point + hit.normal * 0.2f, color, 18, 6f);
    }
}

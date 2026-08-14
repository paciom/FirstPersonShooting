using UnityEngine;

/// <summary>
/// Charge dispersal: a wide fan of crackling micro-lightning pellets — close-up
/// punch that falls off quickly with distance.
/// </summary>
public class StaticShotgun : Weapon
{
    public float shotsPerSecond = 1.1f;
    public int pellets = 8;
    public float spreadDegrees = 11f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Static Shotgun";
        color = new Color(0.45f, 0.72f, 1f);   // saturated spark-blue
        if (damage == 20f) damage = 5f;   // per pellet
        preferredRange = 8f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        for (int i = 0; i < pellets; i++)
        {
            Vector3 dir = Quaternion.Euler(
                Random.Range(-spreadDegrees, spreadDegrees),
                Random.Range(-spreadDegrees, spreadDegrees), 0f) * direction.normalized;
            var spec = new BoltSpec
            {
                // Slow-ish pellets on purpose: trails can never be shorter than
                // one frame of travel, so 50 m/s pellets smeared into room-long
                // ribbons whenever the framerate dipped. 30 m/s keeps the
                // whiskers short even on a slow machine, and ~9m reach is a
                // proper shotgun anyway.
                speed = 30f,
                damage = damage,
                color = color,
                size = 0.06f,
                glow = 1.7f,        // soft pure-blue sparks — no white core
                // 0.3 gave the pellets a 9 m reach — fine in an arena
                // corridor, but a DOWNGRADE next to Tank Raid's 70 m/s
                // cannon, where fights happen at 15-40 m. The spread still
                // does the falloff; the pellets just live long enough to
                // arrive.
                lifetime = 0.9f,
                trailTime = 0.06f,
                trailWidth = 0.045f,
                trailGlow = 1.5f,
                addLight = false,        // 8 pellets × light would be overkill
                fullImpactBurst = false, // ...and 8 full bursts read as one big explosion
            };
            GenericBolt.Spawn(muzzle.position, dir, spec, TeamId, ownerRoot);
        }

        // Muzzle crackle: popcorn sparks plus a couple of micro-arcs snapping
        // into the cone — sells "static discharge" at the source.
        VfxUtil.SpawnBurst(muzzle.position + direction.normalized * 0.5f, color, 10, 5f);
        for (int i = 0; i < 2; i++)
        {
            Vector3 arcDir = Quaternion.Euler(Random.Range(-16f, 16f), Random.Range(-16f, 16f), 0f)
                * direction.normalized;
            WeaponUtil.LightningArc(muzzle.position, muzzle.position + arcDir * Random.Range(1.2f, 2f),
                color, 0.1f);
        }
        FlashMuzzle(5f);
    }
}

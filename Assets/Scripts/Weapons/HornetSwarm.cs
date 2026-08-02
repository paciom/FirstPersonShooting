using UnityEngine;

/// <summary>
/// Opens a pod and lets five little rockets out at once. They fan wide, then
/// each one bends back toward whatever is nearest — so the volley closes into a
/// point from five directions instead of arriving as one lump.
/// </summary>
public class HornetSwarm : Weapon
{
    public float shotsPerSecond = 0.7f;
    public int missiles = 5;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Hornet Swarm";
        color = new Color(1f, 0.85f, 0.25f);
        if (damage == 20f) damage = 9f;   // per missile — the volley is the damage
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        Vector3 aim = direction.normalized;
        Vector3 side = Vector3.Cross(aim, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.01f)
            side = Vector3.right;
        Vector3 up = Vector3.Cross(side, aim);

        for (int i = 0; i < missiles; i++)
        {
            // Spread them around a ring rather than randomly: five rockets
            // leaving on a clean fan reads as a pod opening, where five random
            // directions just reads as a misfire.
            float angle = (i / (float)missiles) * Mathf.PI * 2f;
            Vector3 offset = (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * 0.35f;

            var spec = new BoltSpec
            {
                speed = 30f,
                damage = damage,
                color = color,
                size = 0.09f,
                glow = 1.6f,
                homingDegreesPerSecond = 110f,
                homingRange = 26f,
                lifetime = 4f,
                trailTime = 0.3f,
                trailWidth = 0.1f,
                trailGlow = 1.6f,
                splashRadius = 1.1f,
                splashScale = 0.6f,
                fullImpactBurst = false,   // five full bursts on one robot is a white flash
            };
            var bolt = GenericBolt.Spawn(muzzle.position + offset * 0.5f,
                (aim + offset).normalized, spec, TeamId, ownerRoot);
            MissileModels.Dress(bolt, 1, 0.55f);
        }
        FlashMuzzle(4.5f);
    }
}

using UnityEngine;

/// <summary>
/// Kinetic force: a short-range palm blast that hurls enemies backward and
/// pops incoming enemy projectiles out of the air in a spray of sparks.
/// </summary>
public class RepulsorPalm : Weapon
{
    public float blastsPerSecond = 1.4f;
    public float pushRange = 6.5f;
    public float coneDot = 0.45f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Repulsor Palm";
        color = new Color(0.45f, 0.7f, 1f);
        if (damage == 20f) damage = 10f;
        preferredRange = 5f;
        range = pushRange;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / blastsPerSecond;

        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;

        // Expanding force dome.
        var dome = WeaponUtil.GhostShell(PrimitiveType.Sphere, origin + dir * 1.5f, Vector3.one * 0.5f, color, 0.9f);
        Object.Destroy(dome, 0.25f);
        VfxUtil.ImpactBurst(origin + dir * 1f, color);

        // Shove enemies in the cone.
        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            Vector3 to = WeaponUtil.Center(shield) - origin;
            if (to.magnitude > pushRange || Vector3.Dot(to.normalized, dir) < coneDot)
                continue;
            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            var fx = StatusEffects.Get(shield.transform.root);
            fx?.AddImpulse(to.normalized * 11f + Vector3.up * 4f);
        }

        // Reflect the incoming fire: pop hostile bolts inside the dome.
        foreach (var bolt in FindObjectsByType<GenericBolt>(FindObjectsSortMode.None))
        {
            if (bolt.teamId == TeamId)
                continue;
            Vector3 to = bolt.transform.position - origin;
            if (to.magnitude > pushRange || Vector3.Dot(to.normalized, dir) < coneDot)
                continue;
            VfxUtil.ImpactBurst(bolt.transform.position, Color.white);
            Destroy(bolt.gameObject);
        }
        foreach (var bolt in FindObjectsByType<LaserBolt>(FindObjectsSortMode.None))
        {
            if (bolt.teamId == TeamId)
                continue;
            Vector3 to = bolt.transform.position - origin;
            if (to.magnitude > pushRange || Vector3.Dot(to.normalized, dir) < coneDot)
                continue;
            VfxUtil.ImpactBurst(bolt.transform.position, Color.white);
            Destroy(bolt.gameObject);
        }

        FlashMuzzle(5f);
    }
}

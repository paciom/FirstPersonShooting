using UnityEngine;

/// <summary>
/// Celebratory pyrotechnics: a whistling glitter rocket that detonates into a
/// three-stage firework show — cyan, magenta, gold — damaging in waves while
/// sparkle rain drifts down. The crowd-pleaser.
/// </summary>
public class FireworksFinale : Weapon
{
    public float shotsPerSecond = 0.4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Fireworks Finale";
        color = new Color(1f, 0.85f, 0.4f);
        if (damage == 20f) damage = 16f;   // per burst wave
        preferredRange = 24f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var rocket = new BoltSpec
        {
            speed = 20f,
            gravity = -4f,
            damage = damage,
            color = color,
            size = 0.18f,
            glow = 5f,
            trailTime = 0.6f,           // glitter trail
            trailWidth = 0.25f,
            lifetime = 1.6f,
            onImpact = (bolt, hit) => FireworkShowEntity.Spawn(
                hit.point + hit.normal * 1.5f, damage, bolt.teamId, bolt.ownerRoot),
            onExpire = bolt => FireworkShowEntity.Spawn(
                bolt.transform.position, damage, bolt.teamId, bolt.ownerRoot),
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.12f).normalized,
            rocket, TeamId, ownerRoot);
        FlashMuzzle(4.5f);
    }
}

using UnityEngine;

/// <summary>
/// Superheated plasma: a huge, slow fireball dragging a long sparkling comet
/// tail, with a massive splash where it lands.
/// </summary>
public class CometSling : Weapon
{
    public float shotsPerSecond = 0.7f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Comet Sling";
        color = new Color(1f, 0.5f, 0.2f);
        if (damage == 20f) damage = 50f;
        preferredRange = 24f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            // Slow on purpose — you watch it fly — but 13 read as a downgrade
            // beside a 70 m/s cannon; 18 keeps the spectacle and loses the
            // wait.
            speed = 18f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.8f,
            glow = 1.8f,               // orange fireball; the warm heat lives in the core child
            trailTime = 0.3f,          // modest ribbon — the embers carry the tail
            trailWidth = 0.16f,
            trailGlow = 1.3f,
            splashRadius = 4.5f,
            splashScale = 1.7f,
            lifetime = 6f,
        };
        var bolt = GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        WeaponUtil.DressAsFireball(bolt, color);   // warm heart + corona + ember tail
        // 5 lit the firer's own hull past the bloom threshold — a white ball
        // ON the tank every shot, from a top-down camera.
        FlashMuzzle(2.5f);
    }
}

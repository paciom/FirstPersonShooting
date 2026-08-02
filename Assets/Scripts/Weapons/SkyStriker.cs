using UnityEngine;

/// <summary>
/// Lobs a heavy rocket up over the cover instead of into it, so it comes down
/// on the robot hiding behind the block. Slow, arcing, and worth the wait: the
/// widest blast in the missile rack.
/// </summary>
public class SkyStriker : Weapon
{
    public float shotsPerSecond = 0.6f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Sky Striker";
        color = new Color(0.45f, 0.75f, 1f);
        if (damage == 20f) damage = 34f;
        preferredRange = 30f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 23f,
            gravity = -12f,
            damage = damage,
            color = color,
            size = 0.16f,
            glow = 1.6f,
            lifetime = 6f,
            trailTime = 0.55f,   // the arc is the aim; draw the whole of it
            trailWidth = 0.24f,
            trailGlow = 1.7f,
            splashRadius = 3.4f,
            splashScale = 1.5f,
        };
        // Thrown up 35°: enough to clear the arena's cover blocks from a
        // sensible distance without turning every shot into a mortar the player
        // has to lead by a second and a half.
        var bolt = GenericBolt.Spawn(muzzle.position,
            (direction.normalized + Vector3.up * 0.35f).normalized, spec, TeamId, ownerRoot);
        MissileModels.Dress(bolt, 3, 1.2f);
        FlashMuzzle(4.5f);
    }
}

using UnityEngine;

/// <summary>
/// The big one. A slow, heavy torpedo that takes its time crossing the arena
/// and takes a chunk out of everything standing near where it lands. One shot
/// every two and a half seconds — miss and you have a long walk to think about
/// it.
/// </summary>
public class SiegeTorpedo : Weapon
{
    public float shotsPerSecond = 0.4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Siege Torpedo";
        color = new Color(0.75f, 0.5f, 1f);
        if (damage == 20f) damage = 48f;
        preferredRange = 34f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 17f,        // slow enough to run from, which is the point
            damage = damage,
            color = color,
            size = 0.2f,
            glow = 1.6f,
            lifetime = 7f,
            // Gently guided rather than truly homing: it corrects for a robot
            // that strafes, but anyone who breaks and runs gets away from it.
            homingDegreesPerSecond = 45f,
            homingRange = 28f,
            trailTime = 0.7f,
            trailWidth = 0.3f,
            trailGlow = 1.8f,
            splashRadius = 4.5f,
            splashScale = 2f,
        };
        var bolt = GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        MissileModels.Dress(bolt, 5, 1.6f);
        FlashMuzzle(5.5f);
    }
}

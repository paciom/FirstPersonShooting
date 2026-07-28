using UnityEngine;

/// <summary>
/// Standing waves: a teal ribbon of light that snakes left-right as it
/// travels — weaving around cover that would stop a straight shot.
/// </summary>
public class WaveRider : Weapon
{
    public float shotsPerSecond = 2.4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Wave Rider";
        color = new Color(0.2f, 0.9f, 0.85f);
        if (damage == 20f) damage = 16f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 24f,
            damage = damage,
            color = color,
            size = 0.13f,
            glow = 2.2f,               // teal head to match the ribbon
            trailTime = 0.4f,          // the ribbon
            trailWidth = 0.16f,
            trailGlow = 1.7f,
            weaveAmplitude = 1.1f,
            weaveFrequency = 7f,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}

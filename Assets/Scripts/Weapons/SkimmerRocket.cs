using UnityEngine;

/// <summary>
/// The light one: small rockets fired almost as fast as a blaster, weaving as
/// they go. Each hit is small, but they never stop coming and the weave makes
/// them very hard to read.
/// </summary>
public class SkimmerRocket : Weapon
{
    public float shotsPerSecond = 2.2f;
    float _nextFireTime;
    bool _fast;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Skimmer Rocket";
        color = new Color(1f, 0.4f, 0.75f);
        if (damage == 20f) damage = 13f;
        preferredRange = 16f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 45f,
            damage = damage,
            color = color,
            size = 0.1f,
            glow = 1.6f,
            lifetime = 3f,
            // Every rocket weaves off the same axis, so a held trigger would
            // otherwise send them all down one identical S-bend. Alternating
            // the rate desynchronises consecutive rockets into a braid.
            // (The amplitude can't carry the sign — GenericBolt gates the weave
            // on amplitude > 0, so a negative one just flies straight.)
            weaveAmplitude = 0.7f,
            weaveFrequency = _fast ? 8.5f : 6f,
            trailTime = 0.25f,
            trailWidth = 0.12f,
            trailGlow = 1.7f,
            splashRadius = 1.2f,
            splashScale = 0.6f,
            fullImpactBurst = false,
        };
        _fast = !_fast;

        var bolt = GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        MissileModels.Dress(bolt, 4, 0.7f);
        FlashMuzzle(3f);
    }
}

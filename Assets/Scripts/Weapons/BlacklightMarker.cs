using UnityEngine;

/// <summary>
/// Ultraviolet: deep-purple bolts that tag whoever they hit with a glowing
/// neon outline visible straight through walls for a few seconds.
/// </summary>
public class BlacklightMarker : Weapon
{
    public float shotsPerSecond = 3f;
    public float revealSeconds = 4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Blacklight Marker";
        color = new Color(0.55f, 0.2f, 1f);
        if (damage == 20f) damage = 12f;
        preferredRange = 24f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 40f,
            damage = damage,
            color = color,
            size = 0.1f,
            glow = 2.2f,        // deep violet bolt, not a pink-white flare
            // Short dim tail — a UV streak, not a 7m magenta beam.
            trailTime = 0.07f,
            trailWidth = 0.06f,
            trailGlow = 1.3f,
            onShieldHit = (bolt, shield) =>
                StatusEffects.Get(shield.transform.root)?.ApplyReveal(revealSeconds, bolt.spec.color),
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}

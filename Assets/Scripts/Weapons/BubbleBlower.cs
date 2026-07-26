using UnityEngine;

/// <summary>
/// Surfactant field: blows wobbling iridescent bubbles that trap whoever they
/// touch inside a floating soap sphere until it pops.
/// </summary>
public class BubbleBlower : Weapon
{
    public float shotsPerSecond = 2.2f;
    public float trapSeconds = 2.2f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Bubble Blower";
        color = new Color(0.55f, 0.8f, 1f);   // visible blue — pale blue whites out
        if (damage == 20f) damage = 6f;
        preferredRange = 10f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 10f,
            damage = damage,
            gravity = 2.5f,            // bubbles drift gently UP
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.4f,
            glow = 1.1f,   // soap film, not a glowing pearl
            weaveAmplitude = 0.3f,     // wobble
            weaveFrequency = 4f,
            lifetime = 3f,
            trailTime = 0f,
            onShieldHit = (bolt, shield) =>
                StatusEffects.Get(shield.transform.root)?.ApplyFloat(trapSeconds, bubble: true),
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(2.5f);
    }
}

using UnityEngine;

/// <summary>
/// Adaptive energy: a shape-shifting cube that copies the flavour of the last
/// shot that hit YOU — hit by a comet, it throws comets back. If nothing has
/// hit you yet, it improvises from its own bag of tricks.
/// </summary>
public class MimicCube : Weapon
{
    public float shotsPerSecond = 1.2f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Mimic Cube";
        color = new Color(0.5f, 1f, 0.7f);
        if (damage == 20f) damage = 16f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        // Copy what last hurt us; otherwise improvise.
        BoltSpec spec = WeaponUtil.LastSpecAgainst(ownerRoot)?.Clone() ?? Improvise();

        // The mimic's tell: whatever it copies flies as a morphing cube.
        spec.shape = PrimitiveType.Cube;
        spec.customScale = Vector3.zero;
        spec.size = Mathf.Max(0.16f, spec.size);
        spec.spinDegreesPerSecond = 540f;
        spec.damage = Mathf.Min(spec.damage, damage * 1.6f);   // copy the look, cap the sting
        // Callbacks belong to the original weapon — the copy keeps only the flight style.
        spec.onImpact = null;
        spec.onShieldHit = null;
        spec.onExpire = null;
        spec.splitSpec = null;
        spec.splitCount = 0;

        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }

    BoltSpec Improvise()
    {
        // glow 2.2: the shifting hue IS the identity — don't bloom it white.
        float roll = Random.value;
        if (roll < 0.33f)
            return new BoltSpec { speed = 45f, damage = damage, color = RandomHue(), size = 0.12f, glow = 2.2f };
        if (roll < 0.66f)
            return new BoltSpec
            {
                speed = 18f, gravity = -12f, damage = damage, color = RandomHue(),
                size = 0.3f, splashRadius = 2.5f, glow = 2.2f,
            };
        return new BoltSpec
        {
            speed = 26f, damage = damage, color = RandomHue(), size = 0.2f,
            bounces = 3, bounceSpeedGain = 1.1f, glow = 2.2f,
        };
    }

    static Color RandomHue() => Color.HSVToRGB(Random.value, 0.7f, 1f);
}

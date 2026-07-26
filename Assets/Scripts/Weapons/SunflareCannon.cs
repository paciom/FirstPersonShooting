using UnityEngine;

/// <summary>
/// Solar energy: hold to charge a growing orange glow, then release a mini sun
/// that detonates in a golden corona with drifting embers.
/// </summary>
public class SunflareCannon : Weapon
{
    public float chargeTime = 1.1f;
    public float cooldownAfterShot = 0.6f;

    float _charge;
    float _readyTime;
    bool _firedThisFrame;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Sunflare Cannon";
        color = new Color(1f, 0.6f, 0.15f);
        if (damage == 20f) damage = 55f;
        preferredRange = 24f;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        if (Time.time < _readyTime)
            return;

        _charge += Time.deltaTime;
        FlashMuzzle(1f + 5f * (_charge / chargeTime));   // the sun swells in the barrel

        if (_charge < chargeTime)
            return;
        _charge = 0f;
        _readyTime = Time.time + cooldownAfterShot;

        var spec = new BoltSpec
        {
            speed = 16f,
            damage = damage,
            gravity = -2f,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.7f,
            glow = 5f,
            trailTime = 0.5f,
            trailWidth = 0.5f,
            splashRadius = 5f,
            splashScale = 1.8f,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(7f);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame && _charge > 0f)
            _charge = Mathf.Max(0f, _charge - Time.deltaTime * 1.5f);
        _firedThisFrame = false;
    }
}

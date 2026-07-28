using UnityEngine;

/// <summary>
/// Micro-plasma: hold the trigger and the barrel spins up — fire rate climbs
/// from a putter to a roaring stream of orange tracer sparks.
/// </summary>
public class EmberGatling : Weapon
{
    public float minRate = 4f;
    public float maxRate = 13f;
    public float spinUpTime = 1.4f;

    float _heat;              // 0..1 spin-up
    float _nextFireTime;
    bool _firedThisFrame;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Ember Gatling";
        color = new Color(1f, 0.55f, 0.15f);
        if (damage == 20f) damage = 5f;
        preferredRange = 16f;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        _heat = Mathf.Min(1f, _heat + Time.deltaTime / spinUpTime);

        if (Time.time < _nextFireTime)
            return;
        float rate = Mathf.Lerp(minRate, maxRate, _heat);
        _nextFireTime = Time.time + 1f / rate;

        Vector3 dir = Quaternion.Euler(Random.Range(-1.5f, 1.5f), Random.Range(-1.5f, 1.5f), 0f)
            * direction.normalized;
        var spec = new BoltSpec
        {
            speed = 34f,             // fast trails smear into javelins on slow frames
            damage = damage,
            color = color,
            size = 0.07f,
            glow = 2.6f,             // orange tracer, not yellow-white
            lifetime = 1f,
            trailTime = 0.07f,
            trailWidth = 0.05f,
            trailGlow = 1.5f,
            addLight = false,        // the stream is dense; muzzle light carries the glow
            fullImpactBurst = false, // 13 bursts/s would white out the target
        };
        GenericBolt.Spawn(muzzle.position, dir, spec, TeamId, ownerRoot);
        FlashMuzzle(2f + 3f * _heat);   // barrel glows hotter as it spins
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame)
            _heat = Mathf.Max(0f, _heat - Time.deltaTime / (spinUpTime * 0.7f));
        _firedThisFrame = false;
    }
}

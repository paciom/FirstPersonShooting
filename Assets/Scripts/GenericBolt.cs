using UnityEngine;

/// <summary>
/// Recipe for a GenericBolt. Most of the expanded arsenal is "a bolt with a
/// twist" — this spec captures the twists (gravity, bounces, splits, homing,
/// weave, pierce, splash, spin) so each weapon is just a spec + a cadence.
/// </summary>
public class BoltSpec
{
    public float speed = 40f;
    public float damage = 15f;
    public float gravity = 0f;              // 0 = straight; negative = falls
    public float lifetime = 4f;
    public float size = 0.12f;              // world-scale of the projectile body
    public float splashRadius = 0f;         // >0 → area burst on death
    public float splashScale = 1f;          // explosion VFX size

    public int bounces = 0;                 // world-surface bounces before dying
    public float bounceSpeedGain = 1f;      // ×speed per bounce (Bouncy Ball!)

    public int splitCount = 0;              // children spawned on first impact
    public float splitSpread = 35f;         // degrees of split cone
    public BoltSpec splitSpec;              // recipe for the children

    public float homingDegreesPerSecond = 0f;
    public float homingRange = 25f;

    public float weaveAmplitude = 0f;       // sine weave (Wave Rider)
    public float weaveFrequency = 6f;

    public int pierce = 0;                  // enemies passed through before dying

    public Color color = Color.white;
    public float glow = 4f;
    public PrimitiveType shape = PrimitiveType.Capsule;
    public Vector3 customScale = Vector3.zero;   // non-zero → overrides size-based scale (discs, rings)
    public float spinDegreesPerSecond = 0f; // visual spin (discs, boomerangs)
    public float trailTime = 0.15f;
    public float trailWidth = 0.1f;
    public float trailGlow = 2f;    // trail emissive; ≳2.5 blooms saturated colors to white
    public bool fullImpactBurst = true;   // false → tiny spark pop (multi-pellet weapons stack 8 bursts otherwise)
    public bool addLight = true;

    /// <summary>Extra behaviour when any surface/shield is struck.</summary>
    public System.Action<GenericBolt, RaycastHit> onImpact;
    /// <summary>Extra behaviour when an enemy shield is damaged (status effects etc.).</summary>
    public System.Action<GenericBolt, EnergyShield> onShieldHit;
    /// <summary>Called when the bolt dies without hitting anything.</summary>
    public System.Action<GenericBolt> onExpire;

    public BoltSpec Clone() => (BoltSpec)MemberwiseClone();
}

/// <summary>
/// The configurable projectile behind most of the arsenal. Raycast-stepped like
/// LaserBolt (no tunneling), honours EffectZone time bubbles, and executes the
/// spec's twists. Visuals: glowing primitive + trail + point light.
/// </summary>
public class GenericBolt : MonoBehaviour
{
    public BoltSpec spec;
    public int teamId;
    public Transform ownerRoot;

    /// <summary>
    /// Explicit seeker lock. Null — the default, and what all the arena
    /// weapons use — keeps the classic behaviour: bend toward whatever enemy
    /// shield is nearest each frame. Set (DOGFIGHT's locked missiles), the
    /// bolt chases THIS root and no other; when it dies or disappears the
    /// bolt falls back to the scan, which is what lets a missile that just
    /// ate a flare go looking again.
    /// </summary>
    public Transform homingTarget;

    Vector3 _velocity;
    Vector3 _weaveBase;      // position ignoring weave, so the sine stays clean
    Vector3 _weaveAxis;
    float _age;
    int _bouncesLeft;
    int _pierceLeft;
    bool _dead;

    public Vector3 Velocity { get => _velocity; set => _velocity = value; }

    public static GenericBolt Spawn(Vector3 position, Vector3 direction, BoltSpec spec, int teamId, Transform ownerRoot)
    {
        var go = GameObject.CreatePrimitive(spec.shape);
        go.name = "GenericBolt";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        if (spec.customScale != Vector3.zero)
            go.transform.localScale = spec.customScale;
        else
            go.transform.localScale = spec.shape == PrimitiveType.Capsule
                ? new Vector3(spec.size, spec.size * 3f, spec.size)
                : Vector3.one * spec.size;
        if (spec.shape == PrimitiveType.Capsule)
            go.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(spec.color, spec.glow);

        if (spec.trailTime > 0f)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = spec.trailTime;
            trail.startWidth = spec.trailWidth;
            trail.endWidth = 0f;
            trail.material = VfxUtil.MakeGlowMaterial(spec.color, spec.trailGlow);
        }

        if (spec.addLight)
        {
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = spec.color;
            light.intensity = 2.2f;
            light.range = 3.5f;
        }

        var bolt = go.AddComponent<GenericBolt>();
        bolt.spec = spec;
        bolt.teamId = teamId;
        bolt.ownerRoot = ownerRoot;
        bolt._velocity = direction.normalized * spec.speed;
        bolt._weaveBase = position;
        bolt._weaveAxis = Vector3.Cross(direction.normalized, Vector3.up).normalized;
        if (bolt._weaveAxis.sqrMagnitude < 0.01f)
            bolt._weaveAxis = Vector3.right;
        bolt._bouncesLeft = spec.bounces;
        bolt._pierceLeft = spec.pierce;
        return bolt;
    }

    void Update()
    {
        if (_dead)
            return;

        // A script recompile during Play wipes plain-C# state like the spec.
        // Orphaned bolts clean themselves up instead of throwing every frame.
        if (spec == null)
        {
            Destroy(gameObject);
            return;
        }

        // Time bubbles slow projectiles crossing them — very readable on camera.
        float dt = Time.deltaTime * EffectZone.TimeScaleAt(transform.position);
        _age += dt;
        if (_age > spec.lifetime)
        {
            spec.onExpire?.Invoke(this);
            Die(transform.position);
            return;
        }

        _velocity += Vector3.up * (spec.gravity * dt);

        // Homing: bend toward the locked root if one is set and still alive,
        // the nearest enemy otherwise.
        if (spec.homingDegreesPerSecond > 0f)
        {
            EnergyShield target = null;
            if (homingTarget != null)
            {
                var locked = homingTarget.GetComponent<EnergyShield>();
                if (locked != null && !locked.IsDown && locked.teamId != teamId)
                    target = locked;
                else
                    homingTarget = null;            // lock died: back to the scan
            }
            if (target == null)
                target = WeaponUtil.NearestEnemy(transform.position, teamId, spec.homingRange, ownerRoot);
            if (target != null)
            {
                Vector3 want = (WeaponUtil.Center(target) - transform.position).normalized;
                _velocity = Vector3.RotateTowards(_velocity, want * _velocity.magnitude,
                    spec.homingDegreesPerSecond * Mathf.Deg2Rad * dt, 0f);
            }
        }

        Vector3 step = _velocity * dt;
        Vector3 newBase = _weaveBase + step;

        // Weave rides a sine wave sideways off the true flight path.
        Vector3 weaveOffset = Vector3.zero;
        if (spec.weaveAmplitude > 0f)
            weaveOffset = _weaveAxis * (Mathf.Sin(_age * spec.weaveFrequency) * spec.weaveAmplitude);
        Vector3 newPos = newBase + weaveOffset;

        Vector3 travel = newPos - transform.position;
        float dist = travel.magnitude;
        if (dist > 0.0001f && Physics.Raycast(transform.position, travel / dist, out RaycastHit hit, dist,
                ~0, QueryTriggerInteraction.Ignore)
            && hit.transform.root != ownerRoot)
        {
            HandleHit(hit);
            if (_dead)
                return;
        }

        _weaveBase = newBase;
        transform.position = newPos;

        if (spec.spinDegreesPerSecond != 0f)
            transform.Rotate(Vector3.up, spec.spinDegreesPerSecond * dt, Space.Self);
        else if (spec.shape == PrimitiveType.Capsule && _velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_velocity.normalized) * Quaternion.Euler(90f, 0f, 0f);
    }

    void HandleHit(RaycastHit hit)
    {
        var shield = hit.transform.root.GetComponent<EnergyShield>();
        bool enemyShield = shield != null && shield.teamId != teamId && !shield.IsDown;

        if (enemyShield)
        {
            shield.TakeHit(spec.damage, hit.point, ownerRoot);
            WeaponUtil.RecordHit(shield.transform.root, spec);
            spec.onShieldHit?.Invoke(this, shield);
            EmitImpact(hit);
            spec.onImpact?.Invoke(this, hit);

            if (_pierceLeft > 0)
            {
                _pierceLeft--;
                // Step just past the target so the next ray doesn't re-hit it.
                transform.position = hit.point + _velocity.normalized * 0.6f;
                _weaveBase = transform.position;
                return;
            }
            Die(hit.point + hit.normal * 0.05f);
            return;
        }

        if (shield == null)
            WeaponUtil.DamageProp(hit.collider, spec.damage, hit.point);
        EmitImpact(hit);
        spec.onImpact?.Invoke(this, hit);
        if (_dead)
            return;

        SpawnSplits(hit);

        if (_bouncesLeft > 0)
        {
            _bouncesLeft--;
            _velocity = Vector3.Reflect(_velocity, hit.normal) * spec.bounceSpeedGain;
            transform.position = hit.point + hit.normal * 0.08f;
            _weaveBase = transform.position;
            FlashQuadBounce(hit);
            return;
        }

        Die(hit.point + hit.normal * 0.05f);
    }

    void EmitImpact(RaycastHit hit)
    {
        if (spec.fullImpactBurst)
            VfxUtil.ImpactBurst(hit.point + hit.normal * 0.05f, spec.color);
        else
            VfxUtil.SpawnBurst(hit.point + hit.normal * 0.05f, spec.color, 3, 2.5f, 0.08f);
    }

    void FlashQuadBounce(RaycastHit hit)
    {
        EmitImpact(hit);
    }

    void SpawnSplits(RaycastHit hit)
    {
        if (spec.splitCount <= 0 || spec.splitSpec == null)
            return;
        Vector3 baseDir = Vector3.Reflect(_velocity.normalized, hit.normal);
        for (int i = 0; i < spec.splitCount; i++)
        {
            Quaternion tilt = Quaternion.AngleAxis(Random.Range(-spec.splitSpread, spec.splitSpread), Random.onUnitSphere);
            Spawn(hit.point + hit.normal * 0.15f, (tilt * baseDir).normalized, spec.splitSpec.Clone(), teamId, ownerRoot);
        }
        spec.splitCount = 0;   // only split once
    }

    /// <summary>Kill the bolt, applying any splash. Safe to call from spec callbacks.</summary>
    public void Die(Vector3 at)
    {
        if (_dead)
            return;
        _dead = true;
        if (spec.splashRadius > 0f)
        {
            // EnergyBurst, NOT Explosion: a splash weapon lands several times
            // a second, and the full combustion explosion — which since
            // 2026-08 includes a War FX fireball — stacked per shot into a
            // permanent white inferno at the impact point (and a particle
            // storm). The bolt's splash is an energy pop in the weapon's
            // colour; the fireball belongs to things that DIE.
            VfxUtil.EnergyBurst(at, spec.color, spec.splashScale);
            WeaponUtil.SplashDamage(at, spec.splashRadius, spec.damage, teamId, ownerRoot);
        }
        Destroy(gameObject);
    }
}

using System.Collections.Generic;
using UnityEngine;

// Runtime-spawned battlefield actors used by the expanded arsenal. These are
// only ever AddComponent'ed during play (never serialized into the scene), so
// they can share this file.

/// <summary>Sticky glowing blob (Glowworm Launcher): pulses light on a wall for a while.</summary>
public class GlowBlobEntity : MonoBehaviour
{
    public float life = 6f;
    Light _light;
    float _age;
    Vector3 _baseScale;

    public static void Spawn(Vector3 position, Color color, float size, float life)
    {
        // Moderate core glow: intensity above ~2.5 blooms saturated colors into
        // plain white balls — the blob should read as colored goo, not a light.
        var go = WeaponUtil.GlowPrimitive(PrimitiveType.Sphere, position, Vector3.one * size, color, 1.4f);
        go.name = "GlowBlob";

        // Soft translucent halo shell around the core sells the "jelly" look.
        var halo = WeaponUtil.GhostShell(PrimitiveType.Sphere, position, Vector3.one * (size * 1.8f), color, 0.45f);
        halo.transform.SetParent(go.transform, true);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 6f;
        light.intensity = 2f;
        var blob = go.AddComponent<GlowBlobEntity>();
        blob.life = life;
        blob._light = light;
        blob._baseScale = go.transform.localScale;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(_age / life);
        float pulse = 1f + Mathf.Sin(_age * 5f) * 0.15f;   // the blob "breathes"
        transform.localScale = _baseScale * k * pulse;
        if (_light != null)
            _light.intensity = 2.5f * k * pulse;
        if (_age >= life)
            Destroy(gameObject);
    }
}

/// <summary>Planted coil (Tesla Turret Thrower): periodically zaps the nearest enemy.</summary>
public class TeslaCoilEntity : MonoBehaviour
{
    public float life = 6f;
    public float zapRange = 9f;
    public float zapDamage = 8f;
    public float zapInterval = 0.8f;
    public int teamId;
    public Transform ownerRoot;
    public Color color;

    float _age;
    float _nextZap;
    float _nextIdleSpark;

    void Update()
    {
        _age += Time.deltaTime;
        transform.Rotate(Vector3.up, 240f * Time.deltaTime);
        Vector3 orbPosition = transform.position + Vector3.up * 0.55f;

        if (_age >= life)
        {
            VfxUtil.ImpactBurst(orbPosition, color);
            Destroy(gameObject);
            return;
        }

        // Idle crackle so a waiting coil still looks charged.
        if (Time.time >= _nextIdleSpark)
        {
            _nextIdleSpark = Time.time + Random.Range(0.3f, 0.6f);
            VfxUtil.SpawnBurst(orbPosition, color, 2, 1.5f, 0.07f);
        }

        if (Time.time < _nextZap)
            return;
        _nextZap = Time.time + zapInterval;

        var target = WeaponUtil.NearestEnemy(transform.position, teamId, zapRange, ownerRoot);
        if (target == null)
            return;
        Vector3 point = WeaponUtil.Center(target);
        WeaponUtil.LightningArc(orbPosition, point, color);
        VfxUtil.ImpactBurst(point, color);
        target.TakeHit(zapDamage, point, ownerRoot);
    }
}

/// <summary>
/// A ring sprite that collapses toward its spawn point while fading — the
/// "attraction force" telegraph for pull fields (spawned repeatedly, the rings
/// read as concentric circles being sucked into the core).
/// </summary>
public class ShrinkingRingEntity : MonoBehaviour
{
    Material _material;
    float _life;
    float _age;
    float _startDiameter;
    float _peakIntensity;

    public static void Spawn(Vector3 position, Quaternion rotation, Color color,
        float startDiameter, float life, float intensity = 1.3f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "ShrinkRing";
        go.transform.position = position;
        go.transform.rotation = rotation;
        go.transform.localScale = Vector3.one * startDiameter;

        var mat = VfxUtil.MakeAdditiveMaterial(VfxUtil.RingTexture, color, 0f);
        go.GetComponent<MeshRenderer>().material = mat;

        var ring = go.AddComponent<ShrinkingRingEntity>();
        ring._material = mat;
        ring._life = life;
        ring._startDiameter = startDiameter;
        ring._peakIntensity = intensity;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _life);
        if (t >= 1f)
        {
            Destroy(_material);
            Destroy(gameObject);
            return;
        }

        // Accelerating collapse (ease-in) sells "force grows near the core";
        // brightness swells in and dies out at the center.
        float scale = Mathf.Lerp(_startDiameter, 0.4f, t * t);
        transform.localScale = Vector3.one * scale;
        _material.SetFloat("_Intensity", _peakIntensity * Mathf.Sin(t * Mathf.PI));
    }
}

/// <summary>
/// A sound-wave ring: spawns facing the fire direction, then travels forward
/// while expanding and fading — staggered, a few of these read as a scream
/// rippling through the air (Sonic Screech).
/// </summary>
public class SonicWaveEntity : MonoBehaviour
{
    const float Life = 0.45f;
    const float TravelDistance = 11f;

    Material _material;
    Vector3 _origin;
    Vector3 _direction;
    float _delay;
    float _age;

    public static void Spawn(Vector3 origin, Vector3 direction, Color color, float delay)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "SonicWave";
        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(direction);
        go.transform.localScale = Vector3.one * 0.7f;

        var mat = VfxUtil.MakeAdditiveMaterial(VfxUtil.RingTexture, color, 0f);
        go.GetComponent<MeshRenderer>().material = mat;

        var wave = go.AddComponent<SonicWaveEntity>();
        wave._material = mat;
        wave._origin = origin;
        wave._direction = direction.normalized;
        wave._delay = delay;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = (_age - _delay) / Life;
        if (t >= 1f)
        {
            Destroy(_material);
            Destroy(gameObject);
            return;
        }
        if (t < 0f)
            return;   // still waiting for its beat in the stagger

        transform.position = _origin + _direction * (t * TravelDistance);
        transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 4.8f, t);
        _material.SetFloat("_Intensity", 1.5f * Mathf.Sin(t * Mathf.PI));
    }
}

/// <summary>
/// A ghost sphere that collapses toward its spawn point while fading — the
/// volumetric partner of ShrinkingRingEntity for pull-field telegraphs.
/// </summary>
public class ShrinkingSphereEntity : MonoBehaviour
{
    Material _material;
    float _life;
    float _age;
    float _startDiameter;
    float _peakIntensity;

    public static void Spawn(Vector3 position, Color color, float startDiameter, float life, float intensity = 0.18f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "ShrinkSphere";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * startDiameter;

        var mat = VfxUtil.MakeAdditiveMaterial(null, color, 0f);
        go.GetComponent<MeshRenderer>().material = mat;

        var sphere = go.AddComponent<ShrinkingSphereEntity>();
        sphere._material = mat;
        sphere._life = life;
        sphere._startDiameter = startDiameter;
        sphere._peakIntensity = intensity;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _life);
        if (t >= 1f)
        {
            Destroy(_material);
            Destroy(gameObject);
            return;
        }

        transform.localScale = Vector3.one * Mathf.Lerp(_startDiameter, 0.5f, t * t);
        _material.SetFloat("_Intensity", _peakIntensity * Mathf.Sin(t * Mathf.PI));
    }
}

/// <summary>
/// Planted magnet anchor (Magnet Ram): for a few seconds it drags every nearby
/// enemy toward itself and slows them — team-colored core, spinning ring, and
/// field lines snapping from the victims into the anchor.
/// </summary>
public class MagnetFieldEntity : MonoBehaviour
{
    public float radius = 6f;
    public float life = 4f;
    public float pullPerSecond = 4.5f;
    public float damagePerSecond = 5f;
    public int teamId;
    public Transform ownerRoot;
    public Color teamColor = Color.white;

    float _age;
    float _nextFieldLine;
    float _nextShrinkRing;
    Transform _ring;
    GameObject _dome;

    public static void Spawn(Vector3 position, Vector3 normal, int teamId, Transform ownerRoot, Color teamColor)
    {
        var go = new GameObject("MagnetField");
        go.transform.position = position + normal * 0.2f;

        var core = WeaponUtil.GlowPrimitive(PrimitiveType.Sphere, go.transform.position,
            Vector3.one * 0.35f, teamColor, 2.2f);
        core.transform.SetParent(go.transform, true);

        // Spinning ring, flat against the surface it hit.
        var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(ringGo.GetComponent<Collider>());
        ringGo.transform.position = go.transform.position;
        ringGo.transform.rotation = Quaternion.LookRotation(normal);
        ringGo.transform.localScale = Vector3.one * 1.3f;
        ringGo.GetComponent<MeshRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.RingTexture, teamColor, 1.4f);
        ringGo.transform.SetParent(go.transform, true);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = teamColor;
        light.range = 6f;
        light.intensity = 2f;

        var field = go.AddComponent<MagnetFieldEntity>();
        field.teamId = teamId;
        field.ownerRoot = ownerRoot;
        field.teamColor = teamColor;
        field._ring = ringGo.transform;

        // Faint team-colored dome marking the pull radius — "stay out of the
        // magenta bubble" needs to be readable from across the arena. Very low
        // intensity: the additive shader draws both faces of the sphere, so
        // anything brighter reads as a solid whitish ball instead of a bubble.
        field._dome = WeaponUtil.GhostShell(PrimitiveType.Sphere, go.transform.position,
            Vector3.one * field.radius * 2f, teamColor, 0.055f);
        field._dome.transform.SetParent(go.transform, true);

        VfxUtil.Explosion(go.transform.position, teamColor, 0.7f);
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= life)
        {
            VfxUtil.ImpactBurst(transform.position, teamColor);
            Destroy(gameObject);
            return;
        }

        if (_ring != null)
            _ring.Rotate(Vector3.forward, 220f * Time.deltaTime, Space.Self);

        // The attraction pulse, two layers collapsing together from the field
        // edge into the core: a circle flat on the ground + a ghost sphere.
        // (The boundary dome stays fixed — it's the border, not the pulse.)
        // Skip the last life-moments so no pulse outlives the field.
        if (Time.time >= _nextShrinkRing && _age < life - 0.9f)
        {
            _nextShrinkRing = Time.time + 0.45f;
            ShrinkingRingEntity.Spawn(transform.position, Quaternion.Euler(90f, 0f, 0f),
                teamColor, radius * 2f, 0.9f);
            ShrinkingSphereEntity.Spawn(transform.position, teamColor, radius * 2f, 0.9f);
        }

        // Dome fades out over the last moments so the expiry is telegraphed.
        if (_dome != null && _age > life - 0.6f)
            _dome.transform.localScale = Vector3.one * radius * 2f * Mathf.Clamp01((life - _age) / 0.6f);

        bool drawLine = Time.time >= _nextFieldLine;
        if (drawLine)
            _nextFieldLine = Time.time + 0.14f;

        foreach (var shield in WeaponUtil.FindEnemies(teamId))
        {
            Vector3 center = WeaponUtil.Center(shield);
            float distance = Vector3.Distance(center, transform.position);
            if (distance > radius)
                continue;

            var fx = StatusEffects.Get(shield.transform.root);
            if (fx != null)
            {
                // Reel them toward the anchor and gum up their footing — but
                // stop pulling near the core so victims ring it, not stack in it.
                if (distance > 1.3f)
                    fx.Drag((transform.position - center).normalized * (pullPerSecond * Time.deltaTime));
                fx.ApplySlow(0.55f, 0.25f);
            }
            shield.TakeHit(damagePerSecond * Time.deltaTime, center, ownerRoot);

            // Field lines snapping victim → anchor, in the owning team's color.
            if (drawLine)
            {
                Color strand = Random.value < 0.5f ? teamColor : Color.white;
                FadingLine.SpawnJagged(center, transform.position, strand, 0.03f, 0.18f, 0.35f, 8, 1.8f);
            }
        }
    }
}

/// <summary>Wandering vortex (Tornado Tube): drifts forward, lifting and zapping whoever it touches.</summary>
public class TornadoEntity : MonoBehaviour
{
    public float life = 5f;
    public float speed = 4f;
    public float grabRadius = 2.2f;
    public float damagePerSecond = 12f;
    public int teamId;
    public Transform ownerRoot;
    public Color color;

    Vector3 _heading;
    float _age;
    float _wanderSeed;

    public static TornadoEntity Spawn(Vector3 position, Vector3 heading, int teamId, Transform ownerRoot, Color color)
    {
        var go = new GameObject("Tornado");
        go.transform.position = position;

        // Funnel: fast orbital particles in a cone, plus a soft core glow.
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startColor = color * 1.5f;
        main.startLifetime = 1.2f;
        main.startSpeed = 2.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // cone opens upward
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalY = 7f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 80f;
        go.GetComponent<ParticleSystemRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, Color.white, 1.4f);
        ps.Play();

        var tornado = go.AddComponent<TornadoEntity>();
        tornado.teamId = teamId;
        tornado.ownerRoot = ownerRoot;
        tornado.color = color;
        tornado._heading = new Vector3(heading.x, 0f, heading.z).normalized;
        tornado._wanderSeed = Random.value * 100f;
        return tornado;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= life)
        {
            VfxUtil.Explosion(transform.position + Vector3.up * 1f, color, 0.8f);
            Destroy(gameObject);
            return;
        }

        // Meander: heading sways with noise so the funnel feels alive.
        float sway = (Mathf.PerlinNoise(_wanderSeed, _age * 0.5f) - 0.5f) * 120f * Time.deltaTime;
        _heading = Quaternion.Euler(0f, sway, 0f) * _heading;
        Vector3 step = _heading * (speed * Time.deltaTime);
        if (!Physics.Raycast(transform.position + Vector3.up * 0.5f, _heading, step.magnitude + 0.6f,
                ~0, QueryTriggerInteraction.Ignore))
            transform.position += step;
        else
            _heading = Quaternion.Euler(0f, 90f, 0f) * _heading;   // bounce off walls

        foreach (var shield in WeaponUtil.FindEnemies(teamId))
        {
            if (Vector3.Distance(shield.transform.position, transform.position) > grabRadius)
                continue;
            shield.TakeHit(damagePerSecond * Time.deltaTime, WeaponUtil.Center(shield), ownerRoot);
            var fx = StatusEffects.Get(shield.transform.root);
            if (fx != null)
            {
                fx.ApplyFloat(0.4f);
                fx.Drag((transform.position - shield.transform.position).normalized * (2f * Time.deltaTime));
            }
        }
    }
}

/// <summary>Personal storm (Thundercloud Pet): follows a victim, raining and zapping.</summary>
public class ThundercloudEntity : MonoBehaviour
{
    public EnergyShield target;
    public float life = 5f;
    public float zapDamage = 7f;
    public Transform ownerRoot;
    public Color color;

    float _age;
    float _nextZap;

    public static void Spawn(EnergyShield target, Transform ownerRoot, Color color)
    {
        var go = new GameObject("Thundercloud");
        go.transform.position = target.transform.position + Vector3.up * 3.2f;

        // Puffy cloud body: a few grey ghost spheres.
        for (int i = 0; i < 4; i++)
        {
            var puff = WeaponUtil.GhostShell(PrimitiveType.Sphere, go.transform.position, Vector3.one, new Color(0.5f, 0.5f, 0.6f), 0.5f);
            puff.transform.SetParent(go.transform, true);
            puff.transform.localPosition = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.15f, 0.15f), Random.Range(-0.5f, 0.5f));
            puff.transform.localScale = Vector3.one * Random.Range(0.7f, 1.1f);
        }

        // Personal rainfall.
        var psGo = new GameObject("Rain");
        psGo.transform.SetParent(go.transform, false);
        var ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(0.5f, 0.7f, 1f) * 1.6f;
        main.startLifetime = 0.7f;
        main.startSpeed = 0.1f;
        main.startSize = 0.05f;
        main.gravityModifier = 1.4f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.6f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 40f;
        var renderer = psGo.GetComponent<ParticleSystemRenderer>();
        renderer.material = VfxUtil.MakeAdditiveMaterial(VfxUtil.SparkTexture, Color.white, 1.4f);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 5f;
        renderer.velocityScale = 0.08f;
        ps.Play();

        var cloud = go.AddComponent<ThundercloudEntity>();
        cloud.target = target;
        cloud.ownerRoot = ownerRoot;
        cloud.color = color;
    }

    void Update()
    {
        _age += Time.deltaTime;
        bool lostTarget = target == null || target.IsDown || !target.gameObject.activeInHierarchy;
        if (_age >= life || lostTarget)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 want = target.transform.position + Vector3.up * 3.2f;
        transform.position = Vector3.Lerp(transform.position, want, 4f * Time.deltaTime);

        if (Time.time < _nextZap)
            return;
        _nextZap = Time.time + 1f;
        Vector3 point = WeaponUtil.Center(target);
        WeaponUtil.LightningArc(transform.position, point, color);
        VfxUtil.ImpactBurst(point, color);
        target.TakeHit(zapDamage, point, ownerRoot);
    }
}

/// <summary>One orbiting guard-moon for the Orbit Launcher. Damages enemies it brushes; flingable.</summary>
public class OrbitOrbEntity : MonoBehaviour
{
    public Transform ownerRoot;
    public int teamId;
    public Color color;
    public float angle;
    public float damage = 10f;

    const float Radius = 1.7f;
    const float Height = 1.3f;
    const float DegreesPerSecond = 160f;
    const float Life = 9f;   // unflung moons pop on their own — bots switch
                             // weapons constantly and would otherwise collect
                             // permanent orbiting balls.
    float _age;
    readonly Dictionary<Transform, float> _touchCooldown = new Dictionary<Transform, float>();

    void Update()
    {
        if (ownerRoot == null || !ownerRoot.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        _age += Time.deltaTime;
        if (_age >= Life)
        {
            VfxUtil.ImpactBurst(transform.position, color);
            Destroy(gameObject);
            return;
        }

        angle += DegreesPerSecond * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        transform.position = ownerRoot.position + new Vector3(Mathf.Cos(rad) * Radius, Height, Mathf.Sin(rad) * Radius);

        foreach (var shield in WeaponUtil.FindEnemies(teamId))
        {
            if (Vector3.Distance(WeaponUtil.Center(shield), transform.position) > 1.0f)
                continue;
            if (_touchCooldown.TryGetValue(shield.transform.root, out float until) && Time.time < until)
                continue;
            _touchCooldown[shield.transform.root] = Time.time + 0.6f;
            shield.TakeHit(damage, transform.position, ownerRoot);
            VfxUtil.ImpactBurst(transform.position, color);
        }
    }

    /// <summary>Slingshot the moon outward as a projectile.</summary>
    public void Fling(Vector3 direction, float boltDamage)
    {
        var spec = new BoltSpec
        {
            speed = 30f,
            damage = boltDamage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.3f,
            glow = 1.7f,        // same violet as while orbiting — no white flare
            trailTime = 0.12f,  // short streak, not an arena-length ribbon
            trailWidth = 0.12f,
            trailGlow = 1.4f,
            splashRadius = 2f,
            splashScale = 0.8f,
        };
        GenericBolt.Spawn(transform.position, direction, spec, teamId, ownerRoot);
        Destroy(gameObject);
    }
}

/// <summary>Hard-light decoy (Clone Decoy Caster): jogs forward, soaks enemy fire, glitch-pops.</summary>
public class DecoyEntity : MonoBehaviour
{
    public float life = 8f;
    float _age;
    EnergyShield _shield;

    public static void Spawn(Vector3 position, Vector3 forward, int teamId, Color color)
    {
        var go = new GameObject("HoloDecoy");
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z).normalized);

        // Translucent hard-light body: ghost capsule + head cube.
        var body = WeaponUtil.GhostShell(PrimitiveType.Capsule, position + Vector3.up * 1f, new Vector3(0.8f, 1f, 0.8f), color, 0.9f);
        body.transform.SetParent(go.transform, true);
        var head = WeaponUtil.GhostShell(PrimitiveType.Cube, position + Vector3.up * 1.9f, Vector3.one * 0.45f, color, 1.1f);
        head.transform.SetParent(go.transform, true);

        // Solid collider so enemy raycast shots connect with it.
        var col = go.AddComponent<CapsuleCollider>();
        col.center = Vector3.up * 1.1f;
        col.height = 2.2f;
        col.radius = 0.45f;

        var shield = go.AddComponent<EnergyShield>();
        shield.teamId = teamId;
        shield.maxShield = 40f;
        shield.Rematerialize();   // Awake already ran with the default max; resync Current

        var decoy = go.AddComponent<DecoyEntity>();
        decoy._shield = shield;
        shield.OnDeRezzed += decoy.Dissolve;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= life)
        {
            Dissolve();
            return;
        }

        // Jog forward until something blocks the way; flicker like a hologram.
        Vector3 step = transform.forward * (4f * Time.deltaTime);
        if (!Physics.Raycast(transform.position + Vector3.up * 1f, transform.forward, 0.9f, ~0, QueryTriggerInteraction.Ignore))
            transform.position += step;

        if (Random.value < 0.03f)
            transform.position += Random.insideUnitSphere * 0.05f;   // glitch jitter
    }

    void Dissolve()
    {
        VfxUtil.Explosion(transform.position + Vector3.up * 1.2f, new Color(0.4f, 0.95f, 1f), 0.9f);
        Destroy(gameObject);
    }
}

/// <summary>Placed exit ring for the Portal Pistol.</summary>
public class PortalEntity : MonoBehaviour
{
    public float life = 12f;
    float _age;

    public static PortalEntity Spawn(Vector3 position, Vector3 normal, Color color)
    {
        var go = new GameObject("Portal");
        go.transform.position = position + normal * 0.4f;
        go.transform.rotation = Quaternion.LookRotation(normal);

        // Rimmed glowing oval: flattened ghost sphere + a brighter inner core.
        var rim = WeaponUtil.GhostShell(PrimitiveType.Sphere, go.transform.position, new Vector3(1.2f, 1.8f, 0.25f), color, 1.4f);
        rim.transform.SetParent(go.transform, false);
        rim.transform.localPosition = Vector3.zero;
        rim.transform.localRotation = Quaternion.identity;
        var core = WeaponUtil.GhostShell(PrimitiveType.Sphere, go.transform.position, new Vector3(0.85f, 1.4f, 0.18f), color * 0.6f, 0.8f);
        core.transform.SetParent(go.transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localRotation = Quaternion.identity;

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 5f;
        light.intensity = 2f;

        return go.AddComponent<PortalEntity>();
    }

    void Update()
    {
        _age += Time.deltaTime;
        transform.Rotate(Vector3.forward, 40f * Time.deltaTime, Space.Self);   // event-horizon swirl
        if (_age >= life)
        {
            VfxUtil.ImpactBurst(transform.position, Color.white);
            Destroy(gameObject);
        }
    }
}

/// <summary>Spinning return-blade (Volt Boomerang): flies out, then homes back to its thrower.</summary>
public class BoomerangEntity : MonoBehaviour
{
    public Transform ownerRoot;
    public int teamId;
    public Color color;
    public float damage = 18f;
    public float speed = 26f;

    const float OutboundTurnDegrees = 110f;   // constant bank → banana-shaped outbound arc

    Vector3 _direction;
    float _age;
    bool _returning;
    float _curveSign = 1f;                    // which way this throw banks
    float _returnTurnRate = 150f;             // ramps up so the return can't orbit forever
    float _turnDistance = 18f;                // ray-picked: flip around at the aimed point
    float _traveled;
    GameObject _trailGo;
    readonly Dictionary<Transform, float> _hitCooldown = new Dictionary<Transform, float>();

    public static void Spawn(Vector3 position, Vector3 direction, float damage, int teamId, Transform ownerRoot,
        Color color, float turnDistance = 18f)
    {
        var go = WeaponUtil.GlowPrimitive(PrimitiveType.Cube, position, new Vector3(0.5f, 0.06f, 0.5f), color, 2.5f);
        go.name = "VoltBoomerang";

        var boom = go.AddComponent<BoomerangEntity>();
        boom.ownerRoot = ownerRoot;
        boom.teamId = teamId;
        boom.color = color;
        boom.damage = damage;
        // Bank left or right per throw; pre-aim against the bank so the
        // middle of the outbound arc sweeps across the original aim line.
        boom._curveSign = Random.value < 0.5f ? -1f : 1f;
        boom._direction = Quaternion.AngleAxis(-boom._curveSign * 28f, Vector3.up) * direction.normalized;
        boom._turnDistance = turnDistance;
        boom.AttachTrail(color, 2f);
    }

    /// <summary>Trail lives on a detachable child so the outbound ribbon can be left behind at the turnaround.</summary>
    void AttachTrail(Color trailColor, float glow)
    {
        _trailGo = new GameObject("BoomerangTrail");
        _trailGo.transform.SetParent(transform, false);
        var trail = _trailGo.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.startWidth = 0.18f;
        trail.endWidth = 0f;
        trail.material = VfxUtil.MakeGlowMaterial(trailColor, glow);
    }

    void Update()
    {
        _age += Time.deltaTime;
        transform.Rotate(Vector3.up, 1080f * Time.deltaTime, Space.World);

        // Outbound: constant bank carves the first half of the boomerang loop.
        if (!_returning)
        {
            _direction = Quaternion.AngleAxis(OutboundTurnDegrees * _curveSign * Time.deltaTime, Vector3.up)
                * _direction;
            _traveled += speed * Time.deltaTime;
        }

        if (!_returning && (_traveled >= _turnDistance || Physics.Raycast(transform.position, _direction, 1f, ~0, QueryTriggerInteraction.Ignore)))
        {
            _returning = true;
            VfxUtil.ImpactBurst(transform.position, color);   // turnaround flash

            // Leave the bright outbound ribbon behind to fade, and fly home
            // with a darker trail + darker blade so the two legs read apart.
            if (_trailGo != null)
            {
                _trailGo.transform.SetParent(null, true);
                Destroy(_trailGo, 0.5f);
            }
            Color darker = color * 0.45f;
            AttachTrail(darker, 1.4f);
            GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(darker, 1.6f);
        }

        if (_returning)
        {
            if (ownerRoot == null)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 home = ownerRoot.position + Vector3.up * 1.2f;
            // Limited-rate steering home: the blade swings in from the side it
            // banked to, tracing a different arc than the outbound leg. The
            // rate ramps up so it can't circle its owner forever.
            _returnTurnRate += 250f * Time.deltaTime;
            Vector3 toHome = (home - transform.position).normalized;
            _direction = Vector3.RotateTowards(_direction, toHome,
                _returnTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
            if (Vector3.Distance(transform.position, home) < 1f)
            {
                Destroy(gameObject);
                return;
            }
        }

        transform.position += _direction * (speed * Time.deltaTime);

        // Electrified edge: hurt whoever it passes on both legs of the trip.
        foreach (var shield in WeaponUtil.FindEnemies(teamId))
        {
            if (Vector3.Distance(WeaponUtil.Center(shield), transform.position) > 1.0f)
                continue;
            if (_hitCooldown.TryGetValue(shield.transform.root, out float until) && Time.time < until)
                continue;
            _hitCooldown[shield.transform.root] = Time.time + 0.8f;
            shield.TakeHit(damage, transform.position, ownerRoot);
            WeaponUtil.LightningArc(transform.position, WeaponUtil.Center(shield), color, 0.12f);
        }

        if (_age > 6f)
            Destroy(gameObject);
    }
}

/// <summary>Micro singularity (Black Hole Yo-yo): anchors, inhales enemies, snaps home.</summary>
public class BlackHoleEntity : MonoBehaviour
{
    public Transform ownerRoot;
    public int teamId;
    public float pullRadius = 7f;
    public float holdTime = 2.2f;

    Vector3 _direction;
    float _age;
    int _phase;   // 0 = fly out, 1 = hold + pull, 2 = return
    float _phaseStart;
    GameObject _ring;

    public static void Spawn(Vector3 position, Vector3 direction, int teamId, Transform ownerRoot)
    {
        var go = WeaponUtil.GlowPrimitive(PrimitiveType.Sphere, position, Vector3.one * 0.45f, Color.black, 0f);
        go.name = "BlackHole";
        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(new Color(0.02f, 0f, 0.05f), 0.3f);

        // Swirling accretion ring.
        var ring = WeaponUtil.GhostShell(PrimitiveType.Sphere, position, new Vector3(1.1f, 0.12f, 1.1f), new Color(0.9f, 0.5f, 1f), 1.6f);
        ring.transform.SetParent(go.transform, true);

        var hole = go.AddComponent<BlackHoleEntity>();
        hole.ownerRoot = ownerRoot;
        hole.teamId = teamId;
        hole._direction = direction.normalized;
        hole._ring = ring;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_ring != null)
            _ring.transform.Rotate(Vector3.up, 300f * Time.deltaTime, Space.Self);

        switch (_phase)
        {
            case 0:
                transform.position += _direction * (18f * Time.deltaTime);
                bool blocked = Physics.Raycast(transform.position, _direction, 0.8f, ~0, QueryTriggerInteraction.Ignore);
                if (_age > 0.8f || blocked)
                {
                    _phase = 1;
                    _phaseStart = Time.time;
                }
                break;

            case 1:
                foreach (var shield in WeaponUtil.FindEnemies(teamId))
                {
                    float d = Vector3.Distance(shield.transform.position, transform.position);
                    if (d > pullRadius)
                        continue;
                    var fx = StatusEffects.Get(shield.transform.root);
                    if (d > 1.2f)   // inhale, but don't stack victims inside the core
                        fx?.Drag((transform.position - WeaponUtil.Center(shield)).normalized * (5.5f * Time.deltaTime));
                    if (d < 1.6f)
                        shield.TakeHit(16f * Time.deltaTime, WeaponUtil.Center(shield), ownerRoot);
                    // Stretched light streams feeding the hole.
                    if (Random.value < 0.1f)
                        FadingLine.Spawn(WeaponUtil.Center(shield), transform.position, new Color(0.8f, 0.5f, 1f), 0.03f, 0.15f, 2.5f);
                }
                if (Time.time - _phaseStart > holdTime)
                    _phase = 2;
                break;

            case 2:
                if (ownerRoot == null)
                {
                    Destroy(gameObject);
                    return;
                }
                Vector3 home = ownerRoot.position + Vector3.up * 1.2f;
                transform.position = Vector3.MoveTowards(transform.position, home, 26f * Time.deltaTime);
                if (Vector3.Distance(transform.position, home) < 0.8f)
                    Destroy(gameObject);
                break;
        }

        if (_age > 8f)
            Destroy(gameObject);
    }
}

/// <summary>Temporary ice barricade (Glacier Wall): crystal blocks that grow up, then melt away.</summary>
public class IceWallEntity : MonoBehaviour
{
    public float life = 6f;
    float _age;
    float _groundY;
    Vector3 _fullScale;

    /// <summary>Returns the wall's root object (alive slightly longer than the blocks) so weapons can enforce one-wall-at-a-time.</summary>
    public static GameObject SpawnWall(Vector3 center, Vector3 right, int blocks, Color color)
    {
        var root = new GameObject("GlacierWall");
        Object.Destroy(root, 7.5f);   // outlives the 6s blocks by a beat

        // Blocks face the wall line (depth perpendicular to it), each with a
        // small random tilt so the wall reads as erupted crystal, not boxes.
        Vector3 depthDir = Vector3.Cross(right, Vector3.up).normalized;
        if (depthDir.sqrMagnitude < 0.5f)
            depthDir = Vector3.forward;

        for (int i = 0; i < blocks; i++)
        {
            float offset = (i - (blocks - 1) * 0.5f) * 1.15f;
            Vector3 pos = center + right * offset;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // keeps its BoxCollider — real cover!
            go.name = "IceBlock";
            go.transform.SetParent(root.transform, true);
            go.transform.position = pos + Vector3.up * 0.1f;
            go.transform.rotation = Quaternion.LookRotation(depthDir)
                * Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-9f, 9f), Random.Range(-3f, 3f));
            // Translucent glass ice — same visual language as the freeze shell.
            // Kept faint: additive faces stack, so looking through several
            // blocks at once must not climb into white.
            go.GetComponent<MeshRenderer>().material =
                VfxUtil.MakeAdditiveMaterial(null, new Color(0.5f, 0.8f, 1f), 0.28f);

            // Brighter crystal tips poking out of the top edge.
            for (int j = 0; j < 2; j++)
            {
                var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(tip.GetComponent<Collider>());
                tip.transform.SetParent(go.transform, false);
                tip.transform.localPosition = new Vector3(Random.Range(-0.32f, 0.32f), 0.55f, 0f);
                tip.transform.localScale = new Vector3(Random.Range(0.12f, 0.22f), 0.3f, Random.Range(0.25f, 0.5f));
                tip.GetComponent<MeshRenderer>().material =
                    VfxUtil.MakeAdditiveMaterial(null, new Color(0.6f, 0.88f, 1f), 0.65f);
            }

            // Bots don't collide with plain colliders — carve the NavMesh so they path around.
            var obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.carving = true;

            var block = go.AddComponent<IceWallEntity>();
            block._groundY = pos.y;
            block._fullScale = new Vector3(1.1f, 2.2f + Random.Range(-0.3f, 0.5f), 0.6f);
            go.transform.localScale = new Vector3(block._fullScale.x, 0.05f, block._fullScale.z);
            VfxUtil.ImpactBurst(pos, color);
        }
        return root;
    }

    void Update()
    {
        _age += Time.deltaTime;

        if (_age < 0.4f)
        {
            // Crystals erupt out of the ground.
            float t = _age / 0.4f;
            transform.localScale = new Vector3(_fullScale.x, _fullScale.y * t, _fullScale.z);
            transform.position = new Vector3(transform.position.x, _groundY + _fullScale.y * t * 0.5f, transform.position.z);
        }
        else if (_age > life - 0.8f)
        {
            // Melt: sink and thin out.
            float t = Mathf.Clamp01((life - _age) / 0.8f);
            transform.localScale = new Vector3(_fullScale.x * t, Mathf.Max(0.02f, _fullScale.y * t), _fullScale.z * t);
            transform.position = new Vector3(transform.position.x, _groundY + _fullScale.y * t * 0.5f, transform.position.z);
        }

        if (_age >= life)
            Destroy(gameObject);
    }
}

/// <summary>
/// Flapping flame wings for the Phoenix Dart: keeps a wing rig aligned to the
/// bolt's flight direction (independent of the capsule's spin-twist) and
/// pitch-flaps both wings — the cartoon flap that makes it read "bird".
/// </summary>
public class PhoenixWingsEntity : MonoBehaviour
{
    public GenericBolt bolt;
    public Transform leftWing;
    public Transform rightWing;

    float _phase;

    void Update()
    {
        if (bolt == null)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = bolt.transform.position;
        if (bolt.Velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(bolt.Velocity.normalized);

        _phase += Time.deltaTime * 11f;
        float flap = Mathf.Sin(_phase) * 26f;
        if (leftWing != null)
            leftWing.localRotation = Quaternion.Euler(90f + flap, -38f, 0f);
        if (rightWing != null)
            rightWing.localRotation = Quaternion.Euler(90f + flap, 38f, 0f);
    }
}

/// <summary>Multi-stage firework show (Fireworks Finale): timed bursts that damage in waves.</summary>
public class FireworkShowEntity : MonoBehaviour
{
    public int teamId;
    public Transform ownerRoot;
    public float waveDamage = 16f;

    static readonly Color[] Stages =
    {
        new Color(0.2f, 0.9f, 1f),     // cyan
        new Color(1f, 0.3f, 0.9f),     // magenta
        new Color(1f, 0.85f, 0.3f),    // gold
    };

    float _age;
    int _stage;

    public static void Spawn(Vector3 position, float waveDamage, int teamId, Transform ownerRoot)
    {
        var go = new GameObject("FireworkShow");
        go.transform.position = position;
        var show = go.AddComponent<FireworkShowEntity>();
        show.teamId = teamId;
        show.ownerRoot = ownerRoot;
        show.waveDamage = waveDamage;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_stage < Stages.Length && _age >= _stage * 0.35f)
        {
            var color = Stages[_stage];
            VfxUtil.Explosion(transform.position + Vector3.up * (_stage * 0.8f), color, 1.4f + _stage * 0.3f);
            WeaponUtil.SplashDamage(transform.position, 4.5f, waveDamage, teamId, ownerRoot);
            _stage++;
        }
        if (_stage >= Stages.Length && _age > 1.6f)
            Destroy(gameObject);
    }
}

/// <summary>Erupting water column (Geyser Rod): warning glow, then a knock-up blast of spray.</summary>
public class GeyserEntity : MonoBehaviour
{
    public int teamId;
    public Transform ownerRoot;
    public float damage = 22f;

    float _age;
    bool _erupted;
    GameObject _warning;

    public static void Spawn(Vector3 position, float damage, int teamId, Transform ownerRoot)
    {
        var go = new GameObject("Geyser");
        go.transform.position = position;
        var geyser = go.AddComponent<GeyserEntity>();
        geyser.teamId = teamId;
        geyser.ownerRoot = ownerRoot;
        geyser.damage = damage;
        geyser._warning = WeaponUtil.GhostShell(PrimitiveType.Sphere, position + Vector3.up * 0.05f,
            new Vector3(2.4f, 0.05f, 2.4f), new Color(0.4f, 0.8f, 1f), 0.9f);
        geyser._warning.transform.SetParent(go.transform, true);
    }

    void Update()
    {
        _age += Time.deltaTime;

        if (!_erupted && _age >= 0.45f)
        {
            _erupted = true;
            Destroy(_warning);
            Erupt();
        }

        if (_age >= 1.6f)
            Destroy(gameObject);
    }

    void Erupt()
    {
        // The white-blue column with mist.
        var psGo = new GameObject("WaterColumn");
        psGo.transform.SetParent(transform, false);
        var ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(0.7f, 0.9f, 1f) * 1.8f;
        main.startLifetime = 0.8f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.gravityModifier = 1.1f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 220f;
        var renderer = psGo.GetComponent<ParticleSystemRenderer>();
        renderer.material = VfxUtil.MakeAdditiveMaterial(VfxUtil.SparkTexture, Color.white, 1.5f);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 4f;
        renderer.velocityScale = 0.05f;
        ps.Play();

        // Knock-up + damage everyone unlucky enough to stand on it.
        foreach (var shield in WeaponUtil.FindEnemies(teamId))
        {
            if (Vector3.Distance(shield.transform.position, transform.position) > 2.2f)
                continue;
            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            var fx = StatusEffects.Get(shield.transform.root);
            if (fx != null)
            {
                fx.ApplyFloat(0.5f);
                fx.AddImpulse(Vector3.up * 7f);
            }
        }
    }
}

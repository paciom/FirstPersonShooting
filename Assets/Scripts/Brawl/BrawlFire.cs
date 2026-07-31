using UnityEngine;

/// <summary>
/// Runtime particle builder for the hot stuff — no assets, WebGL-safe.
/// Layered ParticleSystems (soft-sprite billboards with color-over-life,
/// size curves and turbulence) are what make the fire read as FIRE instead
/// of orange confetti.
/// </summary>
public static class BrawlFireVfx
{
    static Texture2D _soft;
    static Material _additive, _blended;

    /// <summary>A soft radial sprite: bright centre, feathered edge.</summary>
    static Texture2D SoftSprite()
    {
        if (_soft != null)
            return _soft;
        const int size = 64;
        _soft = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float fall = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                fall = fall * fall * (3f - 2f * fall);   // smoothstep feather
                _soft.SetPixel(x, y, new Color(1f, 1f, 1f, fall));
            }
        _soft.Apply();
        return _soft;
    }

    static Material ParticleMaterial(bool additive)
    {
        var cached = additive ? _additive : _blended;
        if (cached != null)
            return cached;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader) { mainTexture = SoftSprite() };
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 3000;
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", SoftSprite());
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (additive) _additive = mat; else _blended = mat;
        return mat;
    }

    /// <summary>
    /// One upward-billowing system: colour runs start→end over each puff's
    /// life, turbulence wobbles it, size grows by <paramref name="grow"/>.
    /// Rate 0 = built silent; callers drive emission.
    /// </summary>
    public static ParticleSystem MakeSystem(Transform parent, string name,
        Color start, Color end, bool additive, float rate, float size,
        float grow, float speed, float lifetime, float radius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // emit UP
        var system = go.AddComponent<ParticleSystem>();

        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.25f);
        main.startColor = start;
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;

        var emission = system.emission;
        emission.rateOverTime = rate;

        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(end.r, end.g, end.b), 0.55f),
                    new GradientColorKey(new Color(end.r * 0.6f, end.g * 0.6f, end.b * 0.6f), 1f) },
            new[] { new GradientAlphaKey(start.a, 0f),
                    new GradientAlphaKey(end.a > 0f ? end.a : start.a * 0.55f, 0.6f),
                    new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var sizeLife = system.sizeOverLifetime;
        sizeLife.enabled = true;
        sizeLife.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.EaseInOut(0f, 0.6f, 1f, grow));

        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.7f;
        noise.scrollSpeed = 0.6f;

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.material = ParticleMaterial(additive);
        renderer.sortMode = ParticleSystemSortMode.Distance;
        return system;
    }
}

/// <summary>
/// The falling fire: a warning ring, a blazing comet, then a burning patch
/// that stays lit for ~30 seconds and hurts BOTH robots standing in it — a
/// piece of the arena neither side owns and both must dance around. Direct
/// hits sting extra.
/// </summary>
public class BrawlFire : MonoBehaviour, BrawlProps.IStrikeable
{
    const int LandDamage = 12;
    const int BurnDamage = 4;
    const float BurnTick = 0.9f;
    const float PatchRadius = 1.25f;
    const float BurnSeconds = 30f;
    const float DieDownSeconds = 4f;
    const float FallSpeed = 11f;

    float _groundY;
    bool _burning;
    float _burnLeft = BurnSeconds;
    float _cyanCooldown, _magentaCooldown;
    GameObject _warning, _comet;
    ParticleSystem _flames, _embers, _smoke;
    Light _glow;
    float _flicker;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        var go = new GameObject("BrawlFire");
        go.transform.SetParent(stageRoot, false);
        float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
        go.transform.localPosition = new Vector3(x, ground, z);

        var fire = go.AddComponent<BrawlFire>();
        fire._groundY = ground;
        fire.BuildComet(ground + 10f);
        fire._warning = fire.BuildWarningRing(stageRoot, x, ground, z);
        BrawlAudio.Play(BrawlAudio.Id.Whoosh, new Vector3(x, ground + 6f, z), 0.5f);
        BrawlProps.Register(fire);
    }

    GameObject BuildWarningRing(Transform stageRoot, float x, float ground, float z)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition = new Vector3(x, ground + 0.03f, z);
        ring.transform.localScale = new Vector3(PatchRadius * 2.1f, 0.012f, PatchRadius * 2.1f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-fire-warning", new Color(1f, 0.3f, 0.12f), 2.2f);
        return ring;
    }

    void BuildComet(float y)
    {
        _comet = new GameObject("Comet");
        _comet.transform.SetParent(transform, false);
        _comet.transform.position = new Vector3(transform.position.x, y, transform.position.z);

        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(_comet.transform, false);
        core.transform.localScale = Vector3.one * 0.5f;
        core.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-fire-core", new Color(1f, 0.62f, 0.2f), 2.6f);

        var trail = BrawlFireVfx.MakeSystem(_comet.transform, "Trail",
            new Color(1f, 0.85f, 0.35f, 0.9f), new Color(0.9f, 0.25f, 0.05f, 0f),
            additive: true, rate: 70f, size: 0.4f, grow: 1.4f,
            speed: 0.6f, lifetime: 0.45f, radius: 0.12f);
        trail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // trail UPWARD behind the fall

        var light = new GameObject("CometGlow").AddComponent<Light>();
        light.transform.SetParent(_comet.transform, false);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.intensity = 2f;
        light.range = 7f;
    }

    void BuildPatch()
    {
        var scorch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        scorch.name = "Scorch";
        Destroy(scorch.GetComponent<Collider>());
        scorch.transform.SetParent(transform, false);
        scorch.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        scorch.transform.localScale = new Vector3(PatchRadius * 2f, 0.02f, PatchRadius * 2f);
        scorch.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("brawl-fire-scorch", new Color(0.09f, 0.06f, 0.05f), 0.2f);

        _flames = BrawlFireVfx.MakeSystem(transform, "Flames",
            new Color(1f, 0.88f, 0.4f, 0.95f), new Color(0.85f, 0.2f, 0.04f, 0f),
            additive: true, rate: 60f, size: 0.45f, grow: 1.6f,
            speed: 1.9f, lifetime: 0.75f, radius: PatchRadius * 0.8f);
        _embers = BrawlFireVfx.MakeSystem(transform, "Embers",
            new Color(1f, 0.75f, 0.3f, 1f), new Color(1f, 0.4f, 0.1f, 0f),
            additive: true, rate: 14f, size: 0.09f, grow: 0.8f,
            speed: 3.4f, lifetime: 1.2f, radius: PatchRadius * 0.7f);
        _smoke = BrawlFireVfx.MakeSystem(transform, "Smoke",
            new Color(0.22f, 0.2f, 0.19f, 0.34f), new Color(0.12f, 0.11f, 0.11f, 0f),
            additive: false, rate: 10f, size: 0.6f, grow: 2.4f,
            speed: 1.1f, lifetime: 1.6f, radius: PatchRadius * 0.55f);
        _smoke.transform.localPosition = new Vector3(0f, 0.5f, 0f);

        _glow = new GameObject("FireGlow").AddComponent<Light>();
        _glow.transform.SetParent(transform, false);
        _glow.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        _glow.type = LightType.Point;
        _glow.color = new Color(1f, 0.55f, 0.2f);
        _glow.intensity = 2.2f;
        _glow.range = 8f;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (!_burning)
        {
            var p = _comet.transform.position;
            p.y -= FallSpeed * dt;
            if (p.y <= _groundY + 0.25f)
            {
                Land();
                return;
            }
            _comet.transform.position = p;
            return;
        }

        _burnLeft -= dt;
        if (_burnLeft <= 0f)
        {
            Despawn();
            return;
        }

        // The last stretch dies down honestly — emission and glow fade so
        // nobody is surprised when it goes out.
        float strength = Mathf.Clamp01(_burnLeft / DieDownSeconds);
        var flameEmission = _flames.emission;
        flameEmission.rateOverTime = 60f * strength;
        var emberEmission = _embers.emission;
        emberEmission.rateOverTime = 14f * strength;
        var smokeEmission = _smoke.emission;
        smokeEmission.rateOverTime = 10f * Mathf.Clamp01(strength + 0.3f);

        _flicker += dt * 11f;
        _glow.intensity = (2.2f + 0.7f * Mathf.PerlinNoise(_flicker, 0.37f)) * strength;

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            _cyanCooldown -= dt;
            _magentaCooldown -= dt;
            if (Burn(controller.Cyan, _cyanCooldown))
                _cyanCooldown = BurnTick;
            if (Burn(controller.Magenta, _magentaCooldown))
                _magentaCooldown = BurnTick;
        }
    }

    void Land()
    {
        _burning = true;
        Destroy(_comet);
        if (_warning != null) { Destroy(_warning); _warning = null; }

        Vector3 at = transform.position;
        VfxUtil.Explosion(at + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.15f), 0.65f);
        BrawlAudio.Play(BrawlAudio.Id.BlastHit, at, 0.8f);

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            DirectHit(controller.Cyan, at);
            DirectHit(controller.Magenta, at);
        }
        BuildPatch();
    }

    static void DirectHit(BrawlFighter fighter, Vector3 at)
    {
        if (fighter == null)
            return;
        Vector3 gap = fighter.transform.position - at;
        if (Mathf.Abs(gap.y) > 1.6f)
            return;
        gap.y = 0f;
        if (gap.sqrMagnitude <= 1.1f * 1.1f)
            fighter.TakeAreaHit(LandDamage, at, heavy: false);
    }

    bool Burn(BrawlFighter fighter, float cooldown)
    {
        if (fighter == null || cooldown > 0f)
            return false;
        Vector3 gap = fighter.transform.position - transform.position;
        if (Mathf.Abs(gap.y) > 1.4f)
            return false;
        gap.y = 0f;
        if (gap.sqrMagnitude > PatchRadius * PatchRadius)
            return false;
        fighter.TakeAreaHit(BurnDamage, transform.position, heavy: false);
        return true;
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker) => false;

    public void Despawn()
    {
        BrawlProps.Unregister(this);
        if (_warning != null)
            Destroy(_warning);
        if (gameObject != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        BrawlProps.Unregister(this);
    }
}

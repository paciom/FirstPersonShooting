using UnityEngine;

/// <summary>
/// The hot-VFX factory: flames, embers, heat haze and steam, built at
/// runtime on the project's own additive sprite pipeline (PhotonArena/
/// Additive via VfxUtil) — and OVERRIDABLE by any prefab dropped into
/// Resources/BrawlFx, which is how a bought asset-store fire replaces this
/// one without touching code. See Assets/Resources/BrawlFx/README.md.
///
/// What makes a runtime fire read as FIRE rather than orange confetti:
///
/// 1. SHAPE. Flame licks are tongues — pinched at the base, bulging, then
///    tapering to a point. Round glow blobs never read as fire no matter
///    how they are coloured, which is exactly how the first attempt failed.
/// 2. TAPER, NOT GROW. A particle that grows as it rises is smoke. Fire
///    shrinks and pinches out.
/// 3. DENSITY. Fire is a continuous mass. Sparse particles read as sparks.
/// 4. RGB FADE, NOT ALPHA. PhotonArena/Additive is `Blend One One` and
///    multiplies the particle's RGB — alpha is not in the blend at all, so
///    a colour-over-lifetime that only drops alpha never fades. Every
///    gradient here ramps its COLOUR down to black.
/// 5. TURBULENCE. Noise at flame frequency is the flicker.
/// </summary>
public static class BrawlFx
{
    const string PrefabDir = "BrawlFx/";

    static Texture2D _flame;

    /// <summary>
    /// The flame-tongue sprite: pinched base, bulging middle, tapering tip,
    /// with noise-ragged edges and a hot core. Generated once per session —
    /// no asset, no import step, WebGL-safe.
    /// </summary>
    public static Texture2D FlameTexture
    {
        get
        {
            if (_flame != null)
                return _flame;
            const int size = 128;
            _flame = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color[size * size];
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size;
                    float y = (py + 0.5f) / size;
                    float t = Mathf.Clamp01((y - 0.06f) / 0.88f);      // 0 base, 1 tip
                    // Width: pinched where it attaches, widest low, gone at the tip.
                    float width = 0.36f * Mathf.Pow(1f - t, 0.85f)
                                  * (0.35f + 0.65f * Mathf.Sqrt(Mathf.Clamp01(t * 6f)));
                    float dx = Mathf.Abs(x - 0.5f);
                    float a = Mathf.Clamp01(1f - dx / Mathf.Max(1e-4f, width));
                    a = a * a * (3f - 2f * a);                          // smooth edge
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(y / 0.10f));
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) * 3f));
                    a *= 0.55f + 0.45f * Mathf.PerlinNoise(x * 7f + 3.1f, y * 4f + 1.7f);
                    float core = Mathf.Clamp01(1f - dx / Mathf.Max(1e-4f, width * 0.5f)) * (1f - t);
                    pixels[py * size + px] = new Color(1f, 1f, 1f, Mathf.Clamp01(a + core * 0.5f));
                }
            _flame.SetPixels(pixels);
            _flame.Apply();
            return _flame;
        }
    }

    /// <summary>
    /// A store-bought effect, if the project has one: drop a prefab named
    /// e.g. "fire" into Assets/Resources/BrawlFx/ and it replaces the
    /// runtime rig wholesale. Returns null when there is nothing to load.
    /// </summary>
    public static GameObject TryPrefab(string name, Transform parent, Vector3 localPosition)
    {
        var prefab = Resources.Load<GameObject>(PrefabDir + name);
        if (prefab == null)
            return null;
        var instance = Object.Instantiate(prefab, parent);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.identity;
        return instance;
    }

    /// <summary>Stop a prefab override emitting so it can die down naturally.</summary>
    public static void StopEmitting(GameObject instance)
    {
        if (instance == null)
            return;
        foreach (var system in instance.GetComponentsInChildren<ParticleSystem>())
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>
    /// One additive sprite system pointed straight up. `taper` &lt; 1 shrinks
    /// each particle over its life (flames), &gt; 1 swells it (haze).
    /// Colour keys must END DARK — see the class note on additive fade.
    /// </summary>
    public static ParticleSystem MakeSystem(Transform parent, string name, Texture2D sprite,
        Gradient life, float rate, float sizeMin, float sizeMax, float taper,
        float speedMin, float speedMax, float lifeMin, float lifeMax,
        float radius, float coneAngle, float intensity, float rise,
        float noiseStrength, float noiseFrequency, bool spin = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        // The shape emits along its local +Z; -90° on X aims that at the sky.
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        var system = go.AddComponent<ParticleSystem>();
        var main = system.main;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = Color.white;
        // Flame tongues must stay upright; only embers and haze may spin.
        main.startRotation = spin
            ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f)
            : new ParticleSystem.MinMaxCurve(0f);
        main.gravityModifier = -rise;      // negative gravity = the updraught
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = Mathf.Max(0.01f, radius);

        var emission = system.emission;
        emission.rateOverTime = rate;

        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = life;

        var sizeLife = system.sizeOverLifetime;
        sizeLife.enabled = true;
        sizeLife.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(
                new Keyframe(0f, taper < 1f ? 0.75f : 0.6f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, taper)));

        if (noiseStrength > 0f)
        {
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = noiseStrength;
            noise.frequency = noiseFrequency;
            noise.scrollSpeed = 1.1f;
            noise.damping = true;
        }

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        // A null texture on this shader renders as a hard SQUARE — always
        // hand it a sprite (the note VfxUtil leaves for exactly this).
        renderer.material = VfxUtil.MakeAdditiveMaterial(sprite, Color.white, intensity);
        renderer.sortMode = ParticleSystemSortMode.Distance;

        // A system added by AddComponent has already missed its own Awake,
        // so playOnAwake never fires — a continuous fire must be told to
        // run. (VfxUtil's one-shot bursts sidestep this with Emit.)
        system.Play(true);
        return system;
    }

    /// <summary>Colour keys that END BLACK, so an additive particle fades out.</summary>
    public static Gradient Ramp(params (float at, Color color)[] keys)
    {
        var gradient = new Gradient();
        var colorKeys = new GradientColorKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].at);
        gradient.SetKeys(colorKeys, new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0f, 1f),
        });
        return gradient;
    }

    // ---- the standard recipes -------------------------------------------

    /// <summary>White-hot at birth, orange, deep red, out.</summary>
    public static Gradient FlameRamp => Ramp(
        (0.00f, new Color(1.00f, 0.95f, 0.72f)),
        (0.18f, new Color(1.00f, 0.72f, 0.24f)),
        (0.48f, new Color(0.95f, 0.34f, 0.06f)),
        (0.78f, new Color(0.40f, 0.09f, 0.01f)),
        (1.00f, Color.black));

    public static Gradient EmberRamp => Ramp(
        (0.00f, new Color(1.00f, 0.88f, 0.45f)),
        (0.45f, new Color(1.00f, 0.45f, 0.10f)),
        (1.00f, Color.black));

    /// <summary>Additive "smoke" can only add light, so it is hot haze.</summary>
    public static Gradient HazeRamp => Ramp(
        (0.00f, new Color(0.30f, 0.16f, 0.07f)),
        (0.40f, new Color(0.18f, 0.10f, 0.05f)),
        (1.00f, Color.black));

    public static Gradient SteamRamp => Ramp(
        (0.00f, new Color(0.85f, 0.92f, 1.00f)),
        (0.45f, new Color(0.45f, 0.52f, 0.62f)),
        (1.00f, Color.black));

    /// <summary>
    /// The full fire rig at a point: hot core, flame tongues, embers, haze.
    /// Returns the systems whose emission the caller throttles as it dies.
    /// </summary>
    public static ParticleSystem[] BuildFire(Transform parent, float radius, float scale = 1f)
    {
        var core = MakeSystem(parent, "FireCore", VfxUtil.GlowTexture, FlameRamp,
            rate: 26f, sizeMin: 0.55f * scale, sizeMax: 0.95f * scale, taper: 0.25f,
            speedMin: 0.3f, speedMax: 0.9f, lifeMin: 0.22f, lifeMax: 0.38f,
            radius: radius * 0.45f, coneAngle: 4f, intensity: 2.4f, rise: 0.05f,
            noiseStrength: 0.2f, noiseFrequency: 1.2f);

        var flames = MakeSystem(parent, "Flames", FlameTexture, FlameRamp,
            rate: 95f, sizeMin: 0.5f * scale, sizeMax: 0.9f * scale, taper: 0.10f,
            speedMin: 1.5f, speedMax: 2.8f, lifeMin: 0.45f, lifeMax: 0.8f,
            radius: radius * 0.62f, coneAngle: 7f, intensity: 1.9f, rise: 0.35f,
            noiseStrength: 0.55f, noiseFrequency: 1.5f);

        var embers = MakeSystem(parent, "Embers", VfxUtil.SparkTexture, EmberRamp,
            rate: 16f, sizeMin: 0.07f * scale, sizeMax: 0.15f * scale, taper: 0.4f,
            speedMin: 2.6f, speedMax: 4.4f, lifeMin: 0.9f, lifeMax: 1.7f,
            radius: radius * 0.7f, coneAngle: 16f, intensity: 2.2f, rise: 0.5f,
            noiseStrength: 0.7f, noiseFrequency: 0.9f, spin: true);

        var haze = MakeSystem(parent, "Haze", VfxUtil.PuffTexture, HazeRamp,
            rate: 11f, sizeMin: 0.7f * scale, sizeMax: 1.3f * scale, taper: 2.3f,
            speedMin: 1.2f, speedMax: 2.0f, lifeMin: 1.1f, lifeMax: 1.9f,
            radius: radius * 0.5f, coneAngle: 10f, intensity: 1.1f, rise: 0.25f,
            noiseStrength: 0.35f, noiseFrequency: 0.5f, spin: true);
        haze.transform.localPosition = new Vector3(0f, 0.55f, 0f);

        return new[] { core, flames, embers, haze };
    }

    /// <summary>The geyser's column: bright, fast, short-lived steam.</summary>
    public static ParticleSystem BuildSteam(Transform parent, float radius)
    {
        var steam = MakeSystem(parent, "Steam", VfxUtil.PuffTexture, SteamRamp,
            rate: 0f, sizeMin: 0.45f, sizeMax: 0.85f, taper: 2.6f,
            speedMin: 4.5f, speedMax: 7.5f, lifeMin: 0.5f, lifeMax: 0.95f,
            radius: radius * 0.6f, coneAngle: 6f, intensity: 1.5f, rise: 0.15f,
            noiseStrength: 0.4f, noiseFrequency: 0.8f, spin: true);
        return steam;
    }
}

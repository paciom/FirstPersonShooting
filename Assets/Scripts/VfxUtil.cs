using UnityEngine;

/// <summary>
/// Sprite-based VFX built from the procedurally generated textures in
/// Resources/VFX (see SpriteForge). Explosions are multi-stage: core flash,
/// expanding shockwave ring, stretched sparks, and drifting glow motes.
/// </summary>
public static class VfxUtil
{
    static Texture2D _glow, _spark, _ring, _puff;

    static Texture2D Glow => _glow != null ? _glow : _glow = Resources.Load<Texture2D>("VFX/glow");
    static Texture2D Spark => _spark != null ? _spark : _spark = Resources.Load<Texture2D>("VFX/spark");
    static Texture2D Ring => _ring != null ? _ring : _ring = Resources.Load<Texture2D>("VFX/ring");
    static Texture2D Puff => _puff != null ? _puff : _puff = Resources.Load<Texture2D>("VFX/puff");

    // Public sprites for other systems' particles — a null texture on the
    // additive shader renders as a hard SQUARE, so particles must use these.
    public static Texture2D GlowTexture => Glow;
    public static Texture2D SparkTexture => Spark;
    public static Texture2D PuffTexture => Puff;
    public static Texture2D RingTexture => Ring;

    public static Material MakeAdditiveMaterial(Texture2D texture, Color color, float intensity = 1f)
    {
        var mat = new Material(Shader.Find("PhotonArena/Additive"));
        if (texture != null) mat.SetTexture("_MainTex", texture);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Intensity", intensity);
        return mat;
    }

    /// <summary>Create a glowing unlit material (HDR color feeds bloom).</summary>
    public static Material MakeGlowMaterial(Color color, float intensity = 3f)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color * intensity);
        return mat;
    }

    /// <summary>Big multi-stage explosion — used for de-rez and future gadgets.</summary>
    /// <summary>
    /// A real explosion — combustion, smoke, debris: the War FX pack's burst
    /// layered under the tinted energy flash, the pairing the dogfight
    /// established. Anything that pops without burning — de-rez, morphs,
    /// pings, portals, celebrations — wants <see cref="EnergyBurst"/>.
    /// </summary>
    public static void Explosion(Vector3 position, Color color, float scale = 1f)
    {
        EnergyBurst(position, color, scale);
        WarFx.Spawn(scale >= 1.6f ? WarFx.Kind.Big : WarFx.Kind.Small,
            position, Mathf.Clamp(scale * 0.9f, 0.35f, 3f));
    }

    /// <summary>The tinted flash + ring + sparks pop with no fire — the old
    /// Explosion look under its honest name.</summary>
    public static void EnergyBurst(Vector3 position, Color color, float scale = 1f)
    {
        FlashQuad.Spawn(position, Glow, Color.white, 0.6f * scale, 2.6f * scale, 0.18f, 3.5f);
        FlashQuad.Spawn(position, Glow, color, 0.8f * scale, 3.4f * scale, 0.35f, 2.5f);
        FlashQuad.Spawn(position, Ring, color, 0.4f * scale, 5.5f * scale, 0.5f, 2.2f);
        SpawnSparks(position, color, Mathf.RoundToInt(26 * scale), 8f * scale);
        SpawnMotes(position, color, Mathf.RoundToInt(14 * scale), 2.2f * scale);
    }

    /// <summary>Small pop for laser bolt impacts.</summary>
    public static void ImpactBurst(Vector3 position, Color color)
    {
        FlashQuad.Spawn(position, Glow, color, 0.25f, 0.85f, 0.15f, 3f);
        SpawnSparks(position, color, 8, 4.5f);
    }

    /// <summary>Legacy simple burst — kept for callers that want a quick pop.</summary>
    public static void SpawnBurst(Vector3 position, Color color, int count, float speed = 4f, float size = 0.12f)
    {
        SpawnSparks(position, color, count, speed);
    }

    static void SpawnSparks(Vector3 position, Color color, int count, float speed)
    {
        var ps = MakeSystem(position, Spark, color);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
        main.gravityModifier = 0.35f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 5f;
        renderer.velocityScale = 0.06f;

        ps.Emit(count);
        Object.Destroy(ps.gameObject, 1.2f);
    }

    static void SpawnMotes(Vector3 position, Color color, int count, float speed)
    {
        var ps = MakeSystem(position, Glow, color);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.gravityModifier = -0.08f;   // motes drift gently upward

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.35f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ps.Emit(count);
        Object.Destroy(ps.gameObject, 1.8f);
    }

    static ParticleSystem MakeSystem(Vector3 position, Texture2D texture, Color color)
    {
        var go = new GameObject("Vfx");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.startColor = color * 2f;   // HDR tint feeds bloom
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = MakeAdditiveMaterial(texture, Color.white, 1.6f);
        return ps;
    }
}

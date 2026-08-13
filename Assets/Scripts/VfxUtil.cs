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

    /// <summary>
    /// A real explosion — combustion, smoke, debris: the War FX pack's burst
    /// carrying the look, with a BRIEF tinted pop over it. Anything that pops
    /// without burning — de-rez, morphs, pings, portals, celebrations — wants
    /// <see cref="EnergyBurst"/>.
    ///
    /// The flash layer here is deliberately small and short. Additive glow
    /// past ~2.5 clips to white under this project's bloom, and a white flash
    /// quad big enough to cover the fireball IS the whole effect from a
    /// top-down camera — one white blob, no fire visible under it. So the
    /// flash is a detonation pop that clears in a tenth of a second, and the
    /// pack's fire and smoke are what the eye actually reads.
    /// </summary>
    public static void Explosion(Vector3 position, Color color, float scale = 1f)
    {
        // Warm-white, not white, and under the bloom threshold's reach: this
        // pop sits ON TOP of the fireball's own stacked-hot core, and pure
        // white here is the last coat of paint on a whiteout.
        FlashQuad.Spawn(position, Glow, new Color(1f, 0.85f, 0.55f),
            0.4f * scale, 1.2f * scale, 0.08f, 1.8f);
        // NO team-coloured glow quad. A metres-wide soft glow at flash
        // intensity spends a fifth of a second past the bloom threshold and
        // paints a white ball squarely over the fireball — it was the last
        // whiteout layer standing after the pack itself was calmed. The ring
        // and the sparks say whose explosion it was; the fire says what kind.
        FlashQuad.Spawn(position, Ring, color, 0.4f * scale, 5.5f * scale, 0.5f, 1.8f);
        SpawnSparks(position, color, Mathf.RoundToInt(22 * scale), 8f * scale);
        // No glow motes either: the fireball brings its own embers, and
        // additive blobs drifting over smoke read as bloom artefacts.

        // A wrecked tank (scale ~1.3) earns the full fireball — the small
        // burst under the old 1.6 bar was a campfire from eighteen metres up.
        WarFx.Spawn(scale >= 1.2f ? WarFx.Kind.Big : WarFx.Kind.Small,
            position, Mathf.Clamp(scale, 0.35f, 3f));
    }

    /// <summary>The tinted flash + ring + sparks pop with no fire — energy,
    /// not combustion. The white core stays under the bloom whiteout line
    /// (~2.5) so the burst keeps its colour instead of clipping to a blob.</summary>
    public static void EnergyBurst(Vector3 position, Color color, float scale = 1f)
    {
        // Sized for STACKING: splash weapons land one of these several times a
        // second on the same spot, so each instance stays modest — brief small
        // white pop, coloured flash under the whiteout line, and the ring and
        // sparks doing the talking.
        FlashQuad.Spawn(position, Glow, Color.white, 0.5f * scale, 1.5f * scale, 0.10f, 2.0f);
        FlashQuad.Spawn(position, Glow, color, 0.7f * scale, 2.6f * scale, 0.28f, 1.9f);
        FlashQuad.Spawn(position, Ring, color, 0.4f * scale, 5.5f * scale, 0.5f, 2.0f);
        SpawnSparks(position, color, Mathf.RoundToInt(26 * scale), 8f * scale);
        SpawnMotes(position, color, Mathf.RoundToInt(14 * scale), 2.2f * scale);
    }

    /// <summary>
    /// The pop where a shot or a fist lands. Small on purpose: a robot is
    /// <see cref="RobotFactory.NormalizedHeight"/> tall, so an impact reads as
    /// an impact only while it stays a fraction of that — the streaks here are
    /// about a fifth of a body height and the spray about a shoulder's width.
    /// Anything bigger stops looking like sparks off armour and starts looking
    /// like the robot is on fire.
    ///
    /// <paramref name="scale"/> is for the rare caller hitting something that
    /// is not robot-sized; leave it alone for combat.
    /// </summary>
    public static void ImpactBurst(Vector3 position, Color color, float scale = 1f)
    {
        FlashQuad.Spawn(position, Glow, color, 0.10f * scale, 0.34f * scale, 0.12f, 3f);
        SpawnImpactSparks(position, color, 10, 2.4f * scale, 0.055f * scale);
    }

    /// <summary>
    /// A quick spark pop at a caller-chosen size — grazes, blocks, breaking
    /// crates, rubble.
    /// </summary>
    public static void SpawnBurst(Vector3 position, Color color, int count, float speed = 4f, float size = 0.12f)
    {
        SpawnImpactSparks(position, color, count, speed, size);
    }

    /// <summary>
    /// Explosion sparks: long streaks that carry across a room. Only the big
    /// bursts want these — see <see cref="SpawnImpactSparks"/> for hits.
    /// </summary>
    static void SpawnSparks(Vector3 position, Color color, int count, float speed)
    {
        EmitSparks(position, color, count, speed,
            sizeMin: 0.10f, sizeMax: 0.22f, lifeMin: 0.25f, lifeMax: 0.55f,
            lengthScale: 5f, velocityScale: 0.06f, gravity: 0.35f);
    }

    /// <summary>
    /// Impact sparks, sized from <paramref name="size"/> — and the streak is
    /// derived from it, which is the part that used to be missing.
    ///
    /// THE BUG THIS FIXES. SpawnBurst took a size and quietly dropped it on
    /// the floor, forwarding to the explosion sparks instead. Every caller in
    /// the game asks for 0.08–0.14; every one of them was getting 0.10–0.22
    /// stretched 5x, which is a streak up to 1.4 m — 86% of a robot's height,
    /// out of a punch. That is why hits looked like the whole body had caught
    /// light rather than like something striking armour.
    /// </summary>
    static void SpawnImpactSparks(Vector3 position, Color color, int count, float speed, float size)
    {
        EmitSparks(position, color, count, speed,
            sizeMin: size * 0.45f, sizeMax: size, lifeMin: 0.08f, lifeMax: 0.20f,
            // Short streaks and almost no velocity stretch, so the length
            // follows the size the caller asked for instead of the speed the
            // spark happens to leave at.
            lengthScale: 2.6f, velocityScale: 0.02f,
            // Heavier than the explosion sparks: small sparks that arc down
            // and die read as struck metal, ones that fly flat read as tracer.
            gravity: 0.9f);
    }

    static void EmitSparks(Vector3 position, Color color, int count, float speed,
        float sizeMin, float sizeMax, float lifeMin, float lifeMax,
        float lengthScale, float velocityScale, float gravity)
    {
        var ps = MakeSystem(position, Spark, color);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.gravityModifier = gravity;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = lengthScale;
        renderer.velocityScale = velocityScale;

        ps.Emit(count);
        // Just past the last particle rather than a flat second and a bit: hits
        // are the most frequent effect in the game, and holding a dead
        // GameObject open for a second each is a crowd of them in a firefight.
        Object.Destroy(ps.gameObject, lifeMax + 0.4f);
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

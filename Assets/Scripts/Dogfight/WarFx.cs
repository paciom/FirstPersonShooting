using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The imported War FX pack, turned into something the dogfight can spend.
///
/// Three things stand between that pack and this sky, and every caller would
/// otherwise have to solve them (MissileModels' reasoning, one asset over):
///
/// 1. Its prefabs live outside a Resources folder, where a runtime load
///    cannot reach them — the ones this mode uses are copied under
///    Assets/Resources/WarFX, and their material references still point back
///    into the pack.
/// 2. A few of its materials sit on BUILT-IN pipeline shaders (Standard,
///    Particles/Multiply and friends), which render solid magenta under URP.
///    The pack's own WFX/* shaders are handwritten and URP-fine, so
///    <see cref="Sanitize"/> walks each spawned instance and re-seats any
///    legacy material onto the nearest WFX or project shader, keeping its
///    texture — the texture reference on a magenta material is still good.
/// 3. Its systems are authored at courtyard scale. <see cref="Spawn"/> takes
///    a scale and applies it through hierarchy scaling mode, because a bare
///    localScale is ignored by systems authored in local space.
///
/// Spawned bursts parent under <see cref="Bin"/> — the mode points it at the
/// stage root, so every lingering plume dies with the set on teardown and
/// nothing ever smokes over the main menu. The explosion prefabs carry the
/// pack's own auto-destruct; looping fire is the caller's to put out.
/// </summary>
public static class WarFx
{
    public enum Kind { Small, Big, Nuke }

    /// <summary>Where bursts live. The mode sets this to its stage root on
    /// setup and clears it on teardown; unset, bursts parent to the scene
    /// and rely on their own auto-destruct.</summary>
    public static Transform Bin;

    static readonly Dictionary<Kind, GameObject> Prefabs = new Dictionary<Kind, GameObject>();
    static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();
    static readonly Dictionary<Material, Material> Calmed = new Dictionary<Material, Material>();
    static GameObject _firePrefab;
    static Material _smokePuff;
    static Material _flame;

    static readonly string[] LegacyPrefixes = { "Particles/", "Legacy Shaders/" };

    public static GameObject Spawn(Kind kind, Vector3 at, float scale = 1f)
    {
        var prefab = Prefab(kind);
        if (prefab == null)
            return null;
        var instance = Object.Instantiate(prefab, at, Quaternion.identity, Bin);
        Fit(instance, scale);
        return instance;
    }

    /// <summary>
    /// A looping burn riding <paramref name="parent"/> — a killed battery.
    /// It lives until its parent dies or the caller puts it out; nothing
    /// here will do it for you. (Moving pawns burn with their own streak
    /// systems instead: this prefab is a bonfire, and a bonfire cannot be
    /// steered — its plume rises in its own frame however its parent turns.)
    /// </summary>
    public static GameObject AttachFire(Transform parent, Vector3 localOffset, float scale = 1f)
    {
        if (_firePrefab == null)
            _firePrefab = Resources.Load<GameObject>("WarFX/WFX_Fire SmallFlame (Black Smoke)");
        if (_firePrefab == null || parent == null)
            return null;
        var instance = Object.Instantiate(_firePrefab, parent);
        instance.transform.localPosition = localOffset;
        Fit(instance, scale);
        return instance;
    }

    /// <summary>
    /// The pack's own smoke-puff look, for emitters the pack did not author
    /// — the damage trails. Its source (WFX_M_Smoke) sits on a dead built-in
    /// shader, so it goes through the same conversion every spawned effect
    /// gets, and the tint is handed back to vertex colour, where the
    /// particles do their own grading. Null when the pack is missing; the
    /// caller keeps its procedural fallback.
    /// </summary>
    public static Material SmokePuffMaterial()
    {
        if (_smokePuff != null)
            return _smokePuff;
        var material = FindMaterial(Prefab(Kind.Big), "WFX_M_Smoke");
        if (material == null)
            return null;
        _smokePuff = Convert(material);
        if (_smokePuff.HasProperty("_TintColor"))
            _smokePuff.SetColor("_TintColor", Color.white);
        return _smokePuff;
    }

    /// <summary>The pack's burning-flame material, straight off its own fire
    /// prefab — scroll-additive, URP-safe, fire-tinted by its author. What a
    /// flame SPRITE looks like when it is not a glow blob.</summary>
    public static Material FlameMaterial()
    {
        if (_flame != null)
            return _flame;
        if (_firePrefab == null)
            _firePrefab = Resources.Load<GameObject>("WarFX/WFX_Fire SmallFlame (Black Smoke)");
        _flame = FindMaterial(_firePrefab, "WFX_M_FlameSmall");
        return _flame;
    }

    static Material FindMaterial(GameObject prefab, string namePrefix)
    {
        if (prefab == null)
            return null;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.name.StartsWith(namePrefix))
                    return material;
        return null;
    }

    /// <summary>A burn at a place rather than on a thing — the crash-site
    /// scar. Bins with the other plumes and puts itself out.</summary>
    public static GameObject SpawnFire(Vector3 at, float scale, float seconds)
    {
        if (_firePrefab == null)
            _firePrefab = Resources.Load<GameObject>("WarFX/WFX_Fire SmallFlame (Black Smoke)");
        if (_firePrefab == null)
            return null;
        var instance = Object.Instantiate(_firePrefab, at, Quaternion.identity, Bin);
        Fit(instance, scale);
        Object.Destroy(instance, seconds);
        return instance;
    }

    static GameObject Prefab(Kind kind)
    {
        if (Prefabs.TryGetValue(kind, out var cached) && cached != null)
            return cached;
        string name = kind == Kind.Small ? "WFX_Explosion Small"
            : kind == Kind.Big ? "WFX_Explosion"
            : "WFX_Nuke";
        var prefab = Resources.Load<GameObject>($"WarFX/{name}");
        Prefabs[kind] = prefab;
        if (prefab == null)
            Debug.LogWarning($"[WarFx] Resources/WarFX/{name} missing — did the pack move?");
        return prefab;
    }

    static void Fit(GameObject instance, float scale)
    {
        if (!Mathf.Approximately(scale, 1f))
        {
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            instance.transform.localScale *= scale;
        }
        Sanitize(instance);
    }

    /// <summary>
    /// Re-seat any legacy-shader material on this instance, and CALM the hot
    /// ones. Additive-flavoured names go to the project's own additive;
    /// multiplies to the pack's URP-safe multiply; the rest to the pack's
    /// plain alpha blend. Converted materials are cached per source, so a
    /// hundred explosions build three materials, not three hundred.
    /// </summary>
    static void Sanitize(GameObject instance)
    {
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;
                if (IsLegacy(material.shader))
                {
                    if (!Converted.TryGetValue(material, out var replacement) || replacement == null)
                    {
                        replacement = Convert(material);
                        Converted[material] = replacement;
                    }
                    // Converted materials land on the pack's own WFX shaders,
                    // so they carry the same overbright tints — the smoke goes
                    // through the same calming as everything else.
                    materials[i] = Calm(replacement) ?? replacement;
                    touched = true;
                }
                else
                {
                    var calmed = Calm(material);
                    if (calmed != null)
                    {
                        materials[i] = calmed;
                        touched = true;
                    }
                }
            }
            if (touched)
                renderer.sharedMaterials = materials;
        }
    }

    /// <summary>
    /// Peak brightness ONE additive particle may reach — on its hottest
    /// channel, after the shader's own 4x. Deliberately near the bloom
    /// threshold rather than above it, because additive particles STACK:
    /// the fireball's core is five or ten of them deep, and the stack is
    /// what should carry the heat. A ceiling of 1.6 looked right for one
    /// particle and still summed to a white core.
    /// </summary>
    const float AdditiveCeiling = 0.9f;

    /// <summary>
    /// What the fire renders AS, whatever colour the particles think they
    /// are. Dimmed alone was not enough: the pack's gradients spend the whole
    /// visible span of every glow layer near WHITE (white to cream, alpha
    /// gone by the time the keys turn orange) — its look depends on drawing
    /// that white core with no bloom, where the edges stay sharp. Under this
    /// project's bloom a white core of any real brightness smears into a
    /// featureless blob. Forcing the tint to a fire colour makes even the
    /// white-phase particles render orange, so the bloom spreads FIRE.
    /// </summary>
    /// Deep orange, not amber: when the core stacks hot, channels saturate in
    /// order — red first, then green, then blue — and the lower green/blue sit
    /// here, the longer a stacked core stays yellow-orange instead of white.
    static readonly Color FireWarm = new Color(1f, 0.48f, 0.16f);

    /// <summary>
    /// The pack's additive shaders OVERBRIGHTEN BY DESIGN: they multiply
    /// vertex colour by tint by four (2x tint, 2x alpha) — the 2015 built-in-
    /// pipeline way to get punch in gamma space with no bloom. Under linear
    /// space and hot bloom that punch clips to white. Every additive WFX
    /// material is re-seated onto a clone whose tint is the fire colour at
    /// the ceiling: same shapes, same motion, but the palette is imposed
    /// rather than trusted. Cached per source; the pack's own asset is never
    /// written.
    /// </summary>
    /// <summary>What SMOKE may reach after its shader's 2x — safely under the
    /// 0.8 bloom threshold, because glowing smoke is a contradiction. The
    /// pack's smoke tints sit at 0.5, which its shader doubles to exactly
    /// white-at-threshold: the "same white blob" that survived two rounds of
    /// calming the additive layers was the smoke blooming all along.</summary>
    const float SmokeCeiling = 0.55f;

    static Material Calm(Material source)
    {
        var shader = source.shader;
        if (shader == null || !shader.name.StartsWith("WFX/")
            || !source.HasProperty("_TintColor"))
            return null;
        if (shader.name.Contains("Multiply"))
            return null;                   // multiplies darken; bloom cannot bite them
        if (Calmed.TryGetValue(source, out var cached) && cached != null)
            return cached;

        Color tint = source.GetColor("_TintColor");
        Material copy;
        if (shader.name.Contains("Add"))
        {
            copy = new Material(source) { name = source.name + " (calmed)" };
            // The shader's output for a white particle is 4x tint, so this
            // puts the hottest channel exactly at the ceiling, in fire.
            float scale = AdditiveCeiling / 4f;
            copy.SetColor("_TintColor", new Color(
                FireWarm.r * scale, FireWarm.g * scale, FireWarm.b * scale, tint.a));
        }
        else
        {
            // Alpha-blended layers: the smoke. Its shader doubles the tint too.
            float peak = 2f * Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            if (peak <= SmokeCeiling)
                return null;
            copy = new Material(source) { name = source.name + " (calmed)" };
            float scale = SmokeCeiling / peak;
            copy.SetColor("_TintColor",
                new Color(tint.r * scale, tint.g * scale, tint.b * scale, tint.a));
        }
        Calmed[source] = copy;
        return copy;
    }

    static bool IsLegacy(Shader shader)
    {
        if (shader == null)
            return true;
        string name = shader.name;
        if (name == "Standard" || name == "Hidden/InternalErrorShader")
            return true;
        foreach (var prefix in LegacyPrefixes)
            if (name.StartsWith(prefix))
                return true;
        return false;
    }

    static Material Convert(Material source)
    {
        string was = source.shader != null ? source.shader.name : "(none)";
        Texture texture = source.HasProperty("_MainTex") ? source.mainTexture : null;

        Material replacement;
        if (was.Contains("Multiply"))
        {
            // The shader's declared name, not its file name — the file says
            // "WFX_S Multiply Soft.shader", the Shader block says "Tint".
            var multiply = Shader.Find("WFX/Multiply Soft Tint");
            replacement = multiply != null
                ? new Material(multiply)
                : VfxUtil.MakeAdditiveMaterial(null, Color.white, 1f);
        }
        else if (was.Contains("Add") || source.name.Contains("Add"))
        {
            // The proof rig's material dump caught this branch red-handed:
            // the pack's SMOKE sits on the built-in Particles/Additive (2015
            // glow-smoke), so it landed here and came out as untinted white
            // additive at full intensity — the white cloud that survived
            // every calming pass, because it had left the WFX shader family
            // Calm watches. Smoke gets a warm-grey ember-lit haze under the
            // bloom threshold; anything else additive gets fire at the same
            // ceiling the WFX layers keep.
            bool smoke = source.name.Contains("Smoke");
            return VfxUtil.MakeAdditiveMaterial(texture as Texture2D,
                smoke ? new Color(0.5f, 0.45f, 0.4f) : FireWarm,
                smoke ? 0.5f : AdditiveCeiling);
        }
        else
        {
            var blend = Shader.Find("WFX/Alpha Blended (No Soft Particles)");
            replacement = blend != null
                ? new Material(blend)
                : VfxUtil.MakeAdditiveMaterial(texture as Texture2D, Color.white, 0.8f);
        }

        replacement.name = source.name + " (URP reseat)";
        if (texture != null && replacement.HasProperty("_MainTex"))
            replacement.SetTexture("_MainTex", texture);
        if (source.HasProperty("_TintColor") && replacement.HasProperty("_TintColor"))
            replacement.SetColor("_TintColor", source.GetColor("_TintColor"));
        return replacement;
    }
}

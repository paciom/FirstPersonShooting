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
    static GameObject _firePrefab;

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
    /// Re-seat any legacy-shader material on this instance. Additive-flavoured
    /// names go to the project's own additive; multiplies to the pack's URP-
    /// safe multiply; the rest to the pack's plain alpha blend. Converted
    /// materials are cached per source, so a hundred explosions build three
    /// materials, not three hundred.
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
                if (material == null || !IsLegacy(material.shader))
                    continue;
                if (!Converted.TryGetValue(material, out var replacement) || replacement == null)
                {
                    replacement = Convert(material);
                    Converted[material] = replacement;
                }
                materials[i] = replacement;
                touched = true;
            }
            if (touched)
                renderer.sharedMaterials = materials;
        }
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
            var multiply = Shader.Find("WFX/Multiply Soft");
            replacement = multiply != null
                ? new Material(multiply)
                : VfxUtil.MakeAdditiveMaterial(null, Color.white, 1f);
        }
        else if (was.Contains("Add") || source.name.Contains("Add"))
        {
            return VfxUtil.MakeAdditiveMaterial(texture as Texture2D, Color.white, 1f);
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

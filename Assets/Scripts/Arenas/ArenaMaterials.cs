using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime material factory for arena surfaces.
///
/// Deliberately NOT AssetDatabase-backed like ArenaBuilder's material helpers:
/// arenas are built while the game is running, where there is no asset
/// database. Same approach VfxUtil already takes.
///
/// Every shader named here must also be listed in ArenaBuilder's
/// EnsureShadersIncluded, or a player build strips it, Shader.Find returns
/// null, and `new Material(null)` throws mid-arena-load.
/// </summary>
public static class ArenaMaterials
{
    static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

    /// <summary>
    /// Drop every cached material. Called when an arena unloads so a long
    /// session cycling arenas does not accumulate one set per visit.
    /// </summary>
    public static void Clear()
    {
        foreach (var mat in Cache.Values)
            if (mat != null)
                Object.Destroy(mat);
        Cache.Clear();
    }

    /// <summary>
    /// Material archetypes on PhotonArena/Surface. These are what make arenas
    /// differ by more than hue — stone is mottled and matte with wandering
    /// crevices, brick has staggered courses and mortar, tread plate is glossy
    /// diamond checker. Only Hull, Organic and Crystal emit at all.
    /// </summary>
    public enum SurfaceStyle
    {
        Hull = 0,     // spaceship interior plating with lit seams
        Stone = 1,    // rough rock, dark crevices
        Brick = 2,    // running-bond courses and mortar
        Tread = 3,    // metal diamond checker plate
        Organic = 4,  // soft cells with glowing veins
        Crystal = 5,  // flat cut facets
        Strata = 6,   // sedimentary bands
        Plank = 7,    // painted boards with grain
    }

    /// <summary>
    /// A real material surface. <paramref name="featureSize"/> is the world size
    /// of one feature (one plate, one brick, one board) in metres.
    /// </summary>
    public static Material Style(string key, SurfaceStyle style, Color baseColor, Color second,
                                 float featureSize, float roughness = 0.85f,
                                 Color? emit = null, float emitStrength = 0f,
                                 float bump = 1f, float cavity = 0.55f)
    {
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var shader = Shader.Find("PhotonArena/Surface");
        if (shader == null)
            return Lit(key, baseColor);

        var mat = new Material(shader) { name = key };
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_SecondColor", second);
        mat.SetColor("_EmitColor", emit ?? Color.black);
        mat.SetFloat("_Tiling", featureSize);
        mat.SetFloat("_Style", (float)style);
        mat.SetFloat("_Roughness", roughness);
        mat.SetFloat("_EmitStrength", emitStrength);
        mat.SetFloat("_BumpStrength", bump);
        mat.SetFloat("_Cavity", cavity);
        Cache[key] = mat;
        return mat;
    }

    /// <summary>Sci-fi panelling: triplanar grooves plus glowing seams.</summary>
    public static Material Surface(string key, Color baseColor, Color seam, float tiling,
                                   float seamGlow = 0.8f, int patternMode = 0, Color? accent = null)
    {
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var shader = Shader.Find("PhotonArena/SciFiPanel");
        if (shader == null)
            return Lit(key, baseColor);

        var mat = new Material(shader) { name = key };
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_PanelColor", baseColor * 0.35f);
        mat.SetColor("_SeamColor", seam);
        mat.SetFloat("_Tiling", tiling);
        mat.SetFloat("_SeamGlow", seamGlow);
        mat.SetFloat("_PatternMode", patternMode);
        mat.SetColor("_AccentColor", accent ?? seam);
        Cache[key] = mat;
        return mat;
    }

    /// <summary>Plain lit surface.</summary>
    public static Material Lit(string key, Color color, float smoothness = 0.25f, float metallic = 0f)
    {
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        Cache[key] = mat;
        return mat;
    }

    /// <summary>
    /// Glowing surface. Intensity is clamped to 2.5 — past that bloom washes
    /// the colour to white, which is the difference between a magenta spire and
    /// a white one.
    /// </summary>
    public static Material Emissive(string key, Color color, float intensity = 1.6f)
    {
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key };
        mat.SetColor("_BaseColor", Color.black);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", color * Mathf.Min(intensity, 2.5f));
        Cache[key] = mat;
        return mat;
    }
}

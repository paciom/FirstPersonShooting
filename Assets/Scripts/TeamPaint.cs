using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Paints a robot into its team's colours by rewriting its albedo, so the same
/// robot picked by both teams is two visibly different robots on the field and
/// on the select screen.
///
/// WHY NOT A COLOUR MULTIPLY. This replaces one — the imported glTF materials
/// used to get `baseColor *= Lerp(white, teamColor, 0.35)`. Two things were
/// wrong with it. It looked for `_BaseColor`/`_Color`, and the Meshy robots
/// import as "Shader Graphs/glTF-pbrMetallicRoughness", whose properties are
/// `baseColorFactor`/`baseColorTexture` — so it silently found nothing and did
/// nothing at all. And even where it lands, a multiply can only ever darken: an
/// orange robot times pale cyan is a muddy orange, not a cyan robot. Team
/// colour has to REPLACE hue, not filter it.
///
/// The repaint runs on the GPU (see PhotonArena/TeamRecolor) rather than over
/// pixels on the CPU, because the imported textures are not CPU-readable and
/// reading 2048² back per robot would stall the frame the match starts on.
///
/// Results are cached per source texture, team hue and size, so six bots on a
/// team, their reinforcements and their select-screen card all share one
/// repainted texture. Cards ask for <see cref="CardSize"/> — they are 190px on
/// screen and there are eighteen of them, so a full-size copy each would cost
/// more memory than the entire robot fleet.
/// </summary>
public static class TeamPaint
{
    /// <summary>Repaint size for the select screen's card previews.</summary>
    public const int CardSize = 256;

    /// <summary>
    /// Repaint size for the in-between frames of a transformation. Those are on
    /// screen for a fraction of the second the fold takes, and there are seven
    /// of them per robot per team — full-size copies would cost more texture
    /// memory than every robot on the field put together.
    /// </summary>
    public const int StageSize = 512;

    /// <summary>Repaint size everywhere else — arena robots and the inspector.</summary>
    public const int DefaultSize = 1024;

    /// <summary>The size a request of <paramref name="maxSize"/> actually gets;
    /// 0 (an unset inspector field) means <see cref="DefaultSize"/>.</summary>
    public static int Resolve(int maxSize) => maxSize > 0 ? maxSize : DefaultSize;

    // Matched to the shader's own defaults; see PA_TeamRecolor.shader for what
    // each one does. Kept here as well so the colour-factor path (materials with
    // no albedo texture at all) shades identically to the texture path.
    const float Strength = 1f;
    const float GreyCutoff = 0.15f;
    const float GreySoftness = 0.25f;
    const float SaturationFloor = 0.5f;

    /// <summary>
    /// How much team colour white and grey armour picks up, on top of the hue
    /// replacement above.
    ///
    /// The hue rule alone wants this at zero — neutrals staying neutral is what
    /// lets two robots on opposite teams still read as the SAME robot instead of
    /// as a cyan silhouette and a magenta one. But it cannot repaint white, and
    /// the default robot (the ranger) is nearly all white plating with a handful
    /// of coloured accents: at zero, two rangers across the two rows differ by a
    /// few scattered patches and that is the exact case this whole thing exists
    /// to fix. A light wash separates them while leaving every panel line,
    /// shadow and highlight reading through it — the armour goes from white to
    /// tinted white, not to solid team colour.
    ///
    /// 0 restores strict neutrals; ~0.45 approaches a full team wash.
    /// </summary>
    const float NeutralWash = 0.22f;

    // glTFast first, then URP Lit, then built-in: the robots are the first of
    // these and the primitive-built props are the last.
    static readonly string[] AlbedoTextures = { "baseColorTexture", "_BaseMap", "_MainTex" };
    static readonly string[] AlbedoColors = { "baseColorFactor", "_BaseColor", "_Color" };

    static readonly Dictionary<(Texture source, int hue, int size), RenderTexture> Painted =
        new Dictionary<(Texture, int, int), RenderTexture>();

    static Material _blit;

    /// <summary>Repaints every renderer under <paramref name="instance"/>.</summary>
    public static void Apply(GameObject instance, Color teamColor, int maxSize = DefaultSize,
        bool editTime = false)
    {
        if (instance != null)
            Apply(instance.GetComponentsInChildren<Renderer>(true), teamColor, maxSize, editTime);
    }

    /// <summary>
    /// Repaints <paramref name="renderers"/>, copying their materials — never
    /// touching the shared import assets.
    ///
    /// <paramref name="editTime"/> is for editor diagnostics that render and
    /// throw the result away in the same call; see the no-op note below for why
    /// nothing that SAVES a scene may set it.
    /// </summary>
    public static void Apply(Renderer[] renderers, Color teamColor, int maxSize = DefaultSize,
        bool editTime = false)
    {
        if (renderers == null)
            return;

        // Edit time is a no-op on purpose. A RenderTexture cannot be serialized,
        // so an ArenaBuilder run that painted here would save a scene whose
        // robots reference textures that are null the moment it is reopened —
        // untextured white robots that look like a broken import. The built
        // scene stays in factory colours and GameModeController paints on the
        // first match launch instead.
        if (!Application.isPlaying && !editTime)
            return;

        // One copy per source material per call: a robot's body and its limbs
        // usually share one material, and copying it per renderer would break
        // batching for nothing.
        var copies = new Dictionary<Material, Material>();

        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null)
                    continue;
                if (!copies.TryGetValue(source, out var painted))
                {
                    painted = Paint(source, teamColor, maxSize);
                    copies[source] = painted;
                }
                materials[i] = painted;
            }
            renderer.sharedMaterials = materials;
        }
    }

    /// <summary>
    /// Drops every repaint made at <paramref name="maxSize"/>. The select screen
    /// calls this for <see cref="CardSize"/> when it closes; the arena's own
    /// repaints are deliberately kept, so re-entering a match is instant.
    /// </summary>
    public static void Release(int maxSize)
    {
        var doomed = new List<(Texture, int, int)>();
        foreach (var entry in Painted)
        {
            if (entry.Key.size != maxSize)
                continue;
            doomed.Add(entry.Key);
            if (entry.Value == null)
                continue;
            entry.Value.Release();
            Object.Destroy(entry.Value);
        }
        foreach (var key in doomed)
            Painted.Remove(key);
    }

    /// <summary>
    /// The team-painted equivalent of a flat colour, for materials carrying no
    /// albedo texture. Same rule the shader applies per pixel.
    /// </summary>
    public static Color Recolor(Color source, Color teamColor)
    {
        Color.RGBToHSV(source, out _, out float saturation, out float value);
        Color.RGBToHSV(teamColor, out float teamHue, out _, out _);

        float weight = Strength * Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(GreyCutoff, GreyCutoff + GreySoftness, saturation));
        var painted = Color.HSVToRGB(teamHue, Mathf.Max(saturation, SaturationFloor), value);

        var result = Color.Lerp(source, painted, weight);
        var wash = Color.HSVToRGB(teamHue, SaturationFloor, value);
        result = Color.Lerp(result, wash, NeutralWash * (1f - weight));
        result.a = source.a;
        return result;
    }

    static Material Paint(Material source, Color teamColor, int maxSize)
    {
        var copy = new Material(source);

        string textureProperty = FirstProperty(copy, AlbedoTextures);
        if (textureProperty != null)
        {
            var albedo = copy.GetTexture(textureProperty);
            if (albedo != null)
                copy.SetTexture(textureProperty, Recolor(albedo, teamColor, maxSize));
        }

        // Applied whether or not there is a texture: glTF carries a base colour
        // factor alongside the map, and leaving it in the old hue would tint the
        // freshly repainted albedo straight back toward where it started.
        string colorProperty = FirstProperty(copy, AlbedoColors);
        if (colorProperty != null)
            copy.SetColor(colorProperty, Recolor(copy.GetColor(colorProperty), teamColor));

        return copy;
    }

    static string FirstProperty(Material material, string[] candidates)
    {
        foreach (var name in candidates)
            if (material.HasProperty(name))
                return name;
        return null;
    }

    static Texture Recolor(Texture source, Color teamColor, int maxSize)
    {
        // Normalized before the key so a caller leaving the size unset shares
        // the default-size repaint rather than duplicating it.
        int budget = Resolve(maxSize);

        Color.RGBToHSV(teamColor, out float hue, out _, out _);
        var key = (source, Mathf.RoundToInt(hue * 360f), budget);
        if (Painted.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var blit = BlitMaterial();
        if (blit == null)
            return source;

        // Fit inside the budget rather than squaring off to it, so a non-square
        // atlas is not stretched on the way through.
        float fit = Mathf.Min(1f, budget / (float)Mathf.Max(source.width, source.height));
        int width = Mathf.Max(1, Mathf.RoundToInt(source.width * fit));
        int height = Mathf.Max(1, Mathf.RoundToInt(source.height * fit));

        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB)
        {
            name = $"{source.name}_team{Mathf.RoundToInt(hue * 360f)}",
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            anisoLevel = source.anisoLevel,
            useMipMap = true,
            autoGenerateMips = false,
        };
        target.Create();

        blit.SetColor("_TeamColor", teamColor);
        // The source's own mip chain does the downsampling: a full-screen blit
        // into a smaller target derives the right mip level from its UV
        // derivatives, so shrinking 2048 to 256 filters instead of aliasing.
        Graphics.Blit(source, target, blit);
        target.GenerateMips();

        Painted[key] = target;
        return target;
    }

    static Material BlitMaterial()
    {
        if (_blit != null)
            return _blit;

        var shader = Shader.Find("PhotonArena/TeamRecolor");
        if (shader == null)
        {
            // Stripped from a player build means every robot silently stays in
            // factory colours, which reads as a team bug rather than a build
            // one — say so. ArenaBuilder.EnsureShadersIncluded is the fix.
            Debug.LogWarning("[TeamPaint] PhotonArena/TeamRecolor not found — robots keep factory colours.");
            return null;
        }

        _blit = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _blit.SetFloat("_Strength", Strength);
        _blit.SetFloat("_GreyCutoff", GreyCutoff);
        _blit.SetFloat("_GreySoftness", GreySoftness);
        _blit.SetFloat("_SaturationFloor", SaturationFloor);
        _blit.SetFloat("_NeutralWash", NeutralWash);
        return _blit;
    }
}

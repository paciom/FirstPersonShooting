using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Repaints a robot for the away team by rewriting its albedo, so the same
/// robot picked by both teams is two visibly different robots on the field and
/// on the select screen. The home team keeps the robots exactly as they were
/// made — see <see cref="FactoryTeam"/>.
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
/// screen and there is one per robot, so a full-size copy each would cost more
/// memory than the entire robot fleet.
///
/// Only ONE team is repainted; see <see cref="FactoryTeam"/>.
/// </summary>
public static class TeamPaint
{
    /// <summary>
    /// The team that wears the robots as they were made — no repaint at all.
    ///
    /// Telling two teams apart only needs ONE of them moved. Repainting both
    /// costs a second set of textures for no extra readability, and it throws
    /// away the palette the robots were designed in: every robot is somebody's
    /// idea of what that robot looks like, and cyan keeps it.
    ///
    /// Matched with a tolerance rather than exactly, because the cyan the
    /// select screen draws its labels in, the one ArenaBuilder gives the bots
    /// and the one MatchAnnouncer names are three separate constants that only
    /// happen to agree today. See <see cref="KeepsFactoryColors"/>.
    /// </summary>
    public static readonly Color FactoryTeam = new Color(0.2f, 0.9f, 1f);

    /// <summary>Repaint size for the select screen's card previews.</summary>
    public const int CardSize = 256;

    /// <summary>
    /// Repaint size for the in-between frames of a transformation. Those are on
    /// screen for a fraction of the second the fold takes, and there are seven
    /// of them per robot — full-size copies would cost more texture memory than
    /// every robot on the field put together.
    /// </summary>
    public const int StageSize = 512;

    /// <summary>Repaint size everywhere else — arena robots and the inspector.</summary>
    public const int DefaultSize = 1024;

    /// <summary>The size a request of <paramref name="maxSize"/> actually gets;
    /// 0 (an unset inspector field) means <see cref="DefaultSize"/>.</summary>
    public static int Resolve(int maxSize) => maxSize > 0 ? maxSize : DefaultSize;

    /// <summary>
    /// True when <paramref name="teamColor"/> is the team that wears the robots'
    /// own colours, and so is never repainted. See <see cref="FactoryTeam"/>.
    ///
    /// Compared as a colour with a tolerance, NOT by hue. Hue would be the
    /// tidier identity — it is what the repaint cache is keyed by — but hue is
    /// meaningless on an unsaturated colour, and not every caller passes a team
    /// colour: CommanderMap paints wrecks with an ash grey whose nominal hue is
    /// an artifact of a 0.04 spread between its channels. Matching on hue would
    /// let a grey drift into "this is the cyan team" and silently stop painting
    /// wrecks. The tolerance still absorbs the real risk, which is the three
    /// separate cyan constants (select screen, ArenaBuilder, MatchAnnouncer)
    /// drifting apart from each other.
    /// </summary>
    public static bool KeepsFactoryColors(Color teamColor) =>
        Mathf.Abs(teamColor.r - FactoryTeam.r) < FactoryTolerance &&
        Mathf.Abs(teamColor.g - FactoryTeam.g) < FactoryTolerance &&
        Mathf.Abs(teamColor.b - FactoryTeam.b) < FactoryTolerance;

    const float FactoryTolerance = 0.12f;

    /// <summary>A team's hue in whole degrees — the identity a repaint is keyed
    /// and compared by, so near-identical constants land on one entry.</summary>
    static int HueKey(Color teamColor)
    {
        Color.RGBToHSV(teamColor, out float hue, out _, out _);
        return Mathf.RoundToInt(hue * 360f);
    }

    /// <summary>
    /// Anchor used when a caller has no per-robot value: the home team's own
    /// paint, which suits the five cool-dominant robots and is wrong for the four
    /// warm ones. Callers with a roster entry should pass its
    /// <c>paintAnchorHue</c> instead — see <see cref="Apply"/>.
    /// </summary>
    public static float DefaultAnchorHue
    {
        get
        {
            Color.RGBToHSV(FactoryTeam, out float hue, out _, out _);
            return hue;
        }
    }

    /// <summary>
    /// Where a source hue lands for this team. Shared by the texture path (via
    /// the shader, which does the identical arithmetic) and the flat-colour path.
    ///
    /// The distance is measured the SHORT way round the wheel and UNSIGNED, so
    /// the fan is always to one side of the team hue. Short-path because
    /// measuring in a fixed direction puts the wrap right where a dominant colour
    /// usually sits; unsigned because a signed offset sends some robots' warm
    /// accents cool, straight into the home team's cyan.
    /// </summary>
    static float PaintedHue(float sourceHue, float teamHue, float anchorHue)
    {
        float delta = Mathf.Abs(Mathf.Repeat(sourceHue - anchorHue + 0.5f, 1f) - 0.5f);
        return Mathf.Repeat(teamHue + HueSpread * delta, 1f);
    }

    // Matched to the shader's own defaults; see PA_TeamRecolor.shader for what
    // each one does. Kept here as well so the colour-factor path (materials with
    // no albedo texture at all) shades identically to the texture path.
    const float Strength = 1f;
    const float GreyCutoff = 0.15f;
    const float GreySoftness = 0.25f;
    const float SaturationFloor = 0.5f;

    /// <summary>
    /// How much of a pixel's distance from the robot's dominant hue survives the
    /// repaint.
    ///
    /// This exists because driving every coloured pixel to the team hue COLLAPSED
    /// two-tone robots. The bolt is blue and yellow; both came out the same
    /// magenta, so the away-team bolt was a solid purple toy with no markings
    /// while the home-team bolt still had two colours.
    ///
    /// 0 restores that collapse. High values are also wrong, and less obviously:
    /// they push the accent so far round the wheel that it arrives back at the
    /// home team's own trim. At 0.7 the bolt's yellow came out yellow again, so
    /// both teams' markings matched. 0.45 puts it in orange — clearly a second
    /// colour, clearly not the original.
    /// </summary>
    const float HueSpread = 0.45f;

    /// <summary>
    /// How much team colour white and grey armour picks up, on top of the hue
    /// replacement above.
    ///
    /// The hue rule alone wants this at zero — neutrals staying neutral is what
    /// lets the repainted robot still read as the SAME robot as its factory-
    /// coloured twin rather than as a magenta silhouette. But it cannot repaint
    /// white, and the default robot (the ranger) is nearly all white plating
    /// with a handful of coloured accents: at zero, the two rangers across the
    /// two rows differ by a few scattered patches, and that is the exact case
    /// this whole thing exists to fix. It matters more now that only one team is
    /// moved at all — the entire difference between the rows rests on this side.
    /// A light wash separates them while leaving every panel line, shadow and
    /// highlight reading through it: the armour goes from white to tinted white,
    /// not to solid team colour.
    ///
    /// 0 restores strict neutrals; ~0.45 approaches a full team wash.
    /// </summary>
    const float NeutralWash = 0.22f;

    // glTFast first, then URP Lit, then built-in: the robots are the first of
    // these and the primitive-built props are the last.
    static readonly string[] AlbedoTextures = { "baseColorTexture", "_BaseMap", "_MainTex" };
    static readonly string[] AlbedoColors = { "baseColorFactor", "_BaseColor", "_Color" };

    static readonly Dictionary<(Texture source, int hue, int anchor, int size), RenderTexture>
        Painted = new Dictionary<(Texture, int, int, int), RenderTexture>();

    static Material _blit;

    /// <summary>Repaints every renderer under <paramref name="instance"/>.</summary>
    public static void Apply(GameObject instance, Color teamColor, int maxSize = DefaultSize,
        bool editTime = false, float anchorHue = -1f)
    {
        if (instance != null)
            Apply(instance.GetComponentsInChildren<Renderer>(true), teamColor, maxSize, editTime,
                anchorHue);
    }

    /// <summary>
    /// Repaints <paramref name="renderers"/>, copying their materials — never
    /// touching the shared import assets.
    ///
    /// <paramref name="editTime"/> is for editor diagnostics that render and
    /// throw the result away in the same call; see the no-op note below for why
    /// nothing that SAVES a scene may set it.
    ///
    /// <paramref name="anchorHue"/> is the robot's own dominant hue — the colour
    /// that comes out exactly the team colour. Negative means "unknown", which
    /// falls back to <see cref="DefaultAnchorHue"/>; that is right for a
    /// cool-dominant robot and leaves a warm-dominant one barely repainted, so
    /// anything holding a roster entry should pass its <c>paintAnchorHue</c>.
    /// </summary>
    public static void Apply(Renderer[] renderers, Color teamColor, int maxSize = DefaultSize,
        bool editTime = false, float anchorHue = -1f)
    {
        if (renderers == null)
            return;
        // Zero as well as negative: RobotRoster.Entry is a struct and cannot
        // carry a -1 initializer, so an unmeasured robot arrives as 0.
        if (anchorHue <= 0f)
            anchorHue = DefaultAnchorHue;

        // The factory team is left strictly alone — not even a material copy,
        // so its robots keep sharing the import assets and keep batching.
        if (KeepsFactoryColors(teamColor))
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
                    painted = Paint(source, teamColor, maxSize, anchorHue);
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
        var doomed = new List<(Texture, int, int, int)>();
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
    public static Color Recolor(Color source, Color teamColor, float anchorHue = -1f)
    {
        if (KeepsFactoryColors(teamColor))
            return source;
        // Zero as well as negative: RobotRoster.Entry is a struct and cannot
        // carry a -1 initializer, so an unmeasured robot arrives as 0.
        if (anchorHue <= 0f)
            anchorHue = DefaultAnchorHue;

        Color.RGBToHSV(source, out float sourceHue, out float saturation, out float value);
        Color.RGBToHSV(teamColor, out float teamHue, out _, out _);

        float weight = Strength * Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(GreyCutoff, GreyCutoff + GreySoftness, saturation));
        var painted = Color.HSVToRGB(
            PaintedHue(sourceHue, teamHue, anchorHue), Mathf.Max(saturation, SaturationFloor), value);

        var result = Color.Lerp(source, painted, weight);
        var wash = Color.HSVToRGB(teamHue, SaturationFloor, value);
        result = Color.Lerp(result, wash, NeutralWash * (1f - weight));
        result.a = source.a;
        return result;
    }

    static Material Paint(Material source, Color teamColor, int maxSize, float anchorHue)
    {
        var copy = new Material(source);

        string textureProperty = FirstProperty(copy, AlbedoTextures);
        if (textureProperty != null)
        {
            var albedo = copy.GetTexture(textureProperty);
            if (albedo != null)
                copy.SetTexture(textureProperty, Recolor(albedo, teamColor, maxSize, anchorHue));
        }

        // Applied whether or not there is a texture: glTF carries a base colour
        // factor alongside the map, and leaving it in the old hue would tint the
        // freshly repainted albedo straight back toward where it started.
        string colorProperty = FirstProperty(copy, AlbedoColors);
        if (colorProperty != null)
            copy.SetColor(colorProperty,
                Recolor(copy.GetColor(colorProperty), teamColor, anchorHue));

        return copy;
    }

    static string FirstProperty(Material material, string[] candidates)
    {
        foreach (var name in candidates)
            if (material.HasProperty(name))
                return name;
        return null;
    }

    static Texture Recolor(Texture source, Color teamColor, int maxSize, float anchorHue)
    {
        // Normalized before the key so a caller leaving the size unset shares
        // the default-size repaint rather than duplicating it.
        int budget = Resolve(maxSize);

        // The anchor is part of the identity: the same texture painted for the
        // same team against a different dominant hue is a different picture, and
        // leaving it out of the key would serve whichever robot painted first.
        int hue = HueKey(teamColor);
        int anchor = Mathf.RoundToInt(anchorHue * 360f);
        var key = (source, hue, anchor, budget);
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
            name = $"{source.name}_team{hue}_a{anchor}",
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            anisoLevel = source.anisoLevel,
            useMipMap = true,
            autoGenerateMips = false,
        };
        target.Create();

        // Per blit, not once on the shared material: the anchor is a property of
        // the robot being painted, and the material is reused for all of them.
        blit.SetColor("_TeamColor", teamColor);
        blit.SetFloat("_AnchorHue", anchorHue);
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
        // _AnchorHue is deliberately NOT set here — it varies per robot and is
        // written immediately before each blit.
        _blit.SetFloat("_HueSpread", HueSpread);
        return _blit;
    }
}

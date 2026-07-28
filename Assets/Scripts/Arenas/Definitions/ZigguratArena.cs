using UnityEngine;

/// <summary>
/// A sandstone step-pyramid under a hard desert sun: three tiers of laid brick
/// over strata-cut bedrock, ringed by a ruined wall.
///
/// This is the arena that exists to prove the surface archetypes are real. There
/// is not one panel line or glowing seam in it — the brick has courses, stagger
/// and mortar, the ground has sedimentary banding, and the only emission is the
/// turquoise glyph inlay. Recolouring the neon shader could not produce it.
/// </summary>
public class ZigguratArena : ArenaDefinition
{
    static readonly Color Glyph = new Color(0.16f, 0.88f, 0.76f);
    static readonly Color Terracotta = new Color(0.76f, 0.36f, 0.20f);

    // Tier footprints, half-extents. The pyramid owns the middle of the arena.
    const float Tier1Half = 9f, Tier1Top = 2.2f;
    const float Tier2Half = 6f, Tier2Top = 4.4f;
    const float Tier3Half = 3f, Tier3Top = 6.6f;

    public override string DisplayName => "ZIGGURAT";
    public override string Tagline => "Three tiers of sun-baked brick. Fight up, or hold the top.";
    public override int Levels => 3;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.60f, 0.50f, 0.35f),
        wall = new Color(0.58f, 0.46f, 0.32f),
        accentA = Glyph,
        accentB = Terracotta,
        ambient = new Color(0.44f, 0.41f, 0.35f),
        keyLight = new Color(1f, 0.95f, 0.82f),
        keyIntensity = 1.6f,
        sky = new Color(0.72f, 0.70f, 0.60f),
        fog = new Color(0.78f, 0.71f, 0.56f),
        fogDensity = 0.008f,
        // Bright arena: the house 2.2 would bloom the sunlit brick into mush.
        bloom = 0.9f,
    };

    public override Vector2 HalfExtent => new Vector2(16f, 16f);
    public override float CoverHalfExtent => 15f;
    public override int CoverCount => 16;
    public override float[] DropPlanes => new[] { 0f, Tier1Top, Tier2Top };

    // Cover is tumbled masonry, matching the walls it fell off.
    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Brick;
    public override float CoverFeatureSize => 1.1f;
    public override float CoverRoughness => 0.93f;

    /// <summary>Fallen masonry at the corners — the jump-up route onto tier one.</summary>
    static readonly Vector3[] Rubble =
    {
        new Vector3(11.5f, 0f, 11.5f), new Vector3(-11.5f, 0f, 11.5f),
        new Vector3(11.5f, 0f, -11.5f), new Vector3(-11.5f, 0f, -11.5f),
    };
    const float RubbleTop = 1.5f;

    /// <summary>
    /// Keep cover off the pyramid — it would be buried inside a tier — and off
    /// the rubble blocks, which cover would otherwise spawn inside.
    /// </summary>
    public override bool IsOpenFloor(Vector3 point)
    {
        if (Mathf.Abs(point.x) <= Tier1Half + 1.5f && Mathf.Abs(point.z) <= Tier1Half + 1.5f)
            return false;
        foreach (var block in Rubble)
        {
            var flat = new Vector2(point.x - block.x, point.z - block.z);
            if (flat.magnitude < 3.2f)
                return false;
        }
        return true;
    }

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-12f, 0f, -15f), new Vector3(-4f, 0f, -16f), new Vector3(12f, 0f, -15f) }
            : new[] { new Vector3(-12f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(12f, 0f, 15f) };
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var bedrock = ArenaMaterials.Style("Zig_Bedrock", ArenaMaterials.SurfaceStyle.Strata,
                                           new Color(0.63f, 0.53f, 0.37f), new Color(0.40f, 0.32f, 0.21f),
                                           4f, roughness: 0.96f, bump: 1.2f, cavity: 0.5f);
        var brick = ArenaMaterials.Style("Zig_Brick", ArenaMaterials.SurfaceStyle.Brick,
                                         new Color(0.68f, 0.55f, 0.38f), new Color(0.42f, 0.35f, 0.26f),
                                         1.2f, roughness: 0.93f, bump: 1.4f, cavity: 0.6f);
        var brickDark = ArenaMaterials.Style("Zig_BrickDark", ArenaMaterials.SurfaceStyle.Brick,
                                             new Color(0.52f, 0.40f, 0.27f), new Color(0.31f, 0.25f, 0.18f),
                                             1.3f, roughness: 0.95f, bump: 1.5f, cavity: 0.65f);
        var glyphMat = ArenaMaterials.Emissive("Zig_Glyph", Glyph, 1.7f);

        kit.Box("Ground", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), bedrock);

        // Ruined perimeter wall — brick, broken into uneven runs.
        var rng = new System.Random(4831);
        for (int side = 0; side < 4; side++)
        {
            for (int seg = 0; seg < 7; seg++)
            {
                float t = -17f + seg * 5.7f;
                float height = 2.5f + (float)rng.NextDouble() * 2.5f;
                Vector3 centre = side switch
                {
                    0 => new Vector3(t, height * 0.5f, 19.5f),
                    1 => new Vector3(t, height * 0.5f, -19.5f),
                    2 => new Vector3(19.5f, height * 0.5f, t),
                    _ => new Vector3(-19.5f, height * 0.5f, t),
                };
                Vector3 size = side < 2
                    ? new Vector3(5.4f, height, 1.2f)
                    : new Vector3(1.2f, height, 5.4f);
                kit.Box("RuinWall", centre, size, seg % 2 == 0 ? brick : brickDark);
            }
        }

        // The three tiers.
        Tier(kit, brick, Tier1Half, Tier1Top, 0f);
        Tier(kit, brickDark, Tier2Half, Tier2Top, Tier1Top);
        Tier(kit, brick, Tier3Half, Tier3Top, Tier2Top);

        // Glyph inlay bands around each tier's lip.
        GlyphBand(kit, glyphMat, Tier1Half, Tier1Top);
        GlyphBand(kit, glyphMat, Tier2Half, Tier2Top);
        GlyphBand(kit, glyphMat, Tier3Half, Tier3Top);

        // Grand ramps. Runs are long enough to stay well under the 40° ceiling,
        // and 3 m wide so two robots can pass.
        kit.Ramp("RampS", new Vector3(0f, 0f, -15f), new Vector3(0f, Tier1Top, -8.5f), 3f, brick);
        kit.Ramp("RampN", new Vector3(0f, 0f, 15f), new Vector3(0f, Tier1Top, 8.5f), 3f, brick);
        kit.Ramp("RampE", new Vector3(8.5f, Tier1Top, 0f), new Vector3(5.5f, Tier2Top, 0f), 2.6f, brickDark);
        kit.Ramp("RampW", new Vector3(-8.5f, Tier1Top, 0f), new Vector3(-5.5f, Tier2Top, 0f), 2.6f, brickDark);
        kit.Ramp("RampTop", new Vector3(0f, Tier2Top, 5.5f), new Vector3(0f, Tier3Top, 2.5f), 2.2f, brick);

        // Fallen masonry: a two-hop shortcut up onto tier one for anything
        // willing to jump. Each step is inside ArenaKit.MaxLeapUp, so the links
        // come out two-way and bots use them in both directions — up as a
        // shortcut, down as an escape.
        foreach (var block in Rubble)
        {
            kit.Box("Rubble", block + Vector3.up * (RubbleTop * 0.5f),
                    new Vector3(3f, RubbleTop, 3f), brickDark, 20f);

            Vector3 top = block + Vector3.up * RubbleTop;
            var outward = new Vector3(Mathf.Sign(block.x), 0f, Mathf.Sign(block.z));
            // Ground up onto the rubble (1.5 m)...
            kit.Link(top, block + outward * 2.6f);
            // ...and rubble across to the tier lip (0.7 m more).
            kit.Link(top, new Vector3(Mathf.Sign(block.x) * 8.2f, Tier1Top,
                                      Mathf.Sign(block.z) * 8.2f));
        }

        // Bots take the drop off a tier rather than walking back round to a ramp.
        kit.Link(new Vector3(0f, Tier1Top, 9.4f), new Vector3(0f, 0f, 10.8f));
        kit.Link(new Vector3(0f, Tier1Top, -9.4f), new Vector3(0f, 0f, -10.8f));
        kit.Link(new Vector3(6.4f, Tier2Top, 0f), new Vector3(7.8f, Tier1Top, 0f));
        kit.Link(new Vector3(-6.4f, Tier2Top, 0f), new Vector3(-7.8f, Tier1Top, 0f));

        // Corner obelisks.
        for (int i = 0; i < 4; i++)
        {
            float x = (i < 2 ? -1f : 1f) * 16f;
            float z = (i % 2 == 0 ? -1f : 1f) * 16f;
            kit.Box("Obelisk", new Vector3(x, 3f, z), new Vector3(1.6f, 6f, 1.6f), brickDark);
            kit.Decor("ObeliskCap", new Vector3(x, 6.2f, z), new Vector3(0.9f, 0.5f, 0.9f), glyphMat);
            kit.Point(new Vector3(x, 6.6f, z), Glyph, 1.6f, 12f);
        }

        // Hard desert sun plus warm bounce. No neon fills — this arena is lit
        // like an outdoor place, which is half of why it does not read sci-fi.
        kit.Directional("Sun", new Color(1f, 0.95f, 0.82f), 1.6f, new Vector3(52f, -40f, 0f), true);
        kit.Directional("Sky Bounce", new Color(0.55f, 0.62f, 0.78f), 0.35f, new Vector3(-40f, 150f, 0f));
    }

    /// <summary>One stepped tier: a solid block from the tier below up to its top.</summary>
    static void Tier(ArenaKit kit, Material mat, float half, float top, float bottom)
    {
        kit.Box("Tier", new Vector3(0f, (top + bottom) * 0.5f, 0f),
                new Vector3(half * 2f, top - bottom, half * 2f), mat);
    }

    static void GlyphBand(ArenaKit kit, Material mat, float half, float top)
    {
        float y = top - 0.35f;
        float span = half * 2f + 0.04f;
        kit.Decor("Glyph", new Vector3(0f, y, half + 0.02f), new Vector3(span, 0.18f, 0.04f), mat);
        kit.Decor("Glyph", new Vector3(0f, y, -half - 0.02f), new Vector3(span, 0.18f, 0.04f), mat);
        kit.Decor("Glyph", new Vector3(half + 0.02f, y, 0f), new Vector3(0.04f, 0.18f, span), mat);
        kit.Decor("Glyph", new Vector3(-half - 0.02f, y, 0f), new Vector3(0.04f, 0.18f, span), mat);
    }
}

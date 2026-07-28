using UnityEngine;

/// <summary>
/// A lava works: black basalt islands and glowing pour-channels under a grid of
/// steel catwalks. The heat comes from the emissive floor rather than the
/// lights, so the orange reads even on surfaces the key light never reaches.
/// </summary>
public class FoundryArena : ArenaDefinition
{
    static readonly Color Lava = new Color(1f, 0.35f, 0.06f);
    static readonly Color Ember = new Color(1f, 0.62f, 0.12f);

    public override string DisplayName => "FOUNDRY";
    public override string Tagline => "Steel catwalks over a lava lake. Watch your footing.";
    public override int Levels => 2;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.06f, 0.05f, 0.05f),
        wall = new Color(0.10f, 0.08f, 0.07f),
        accentA = Lava,
        accentB = Ember,
        ambient = new Color(0.20f, 0.12f, 0.08f),
        keyLight = new Color(1f, 0.72f, 0.45f),
        keyIntensity = 0.85f,
        sky = new Color(0.05f, 0.02f, 0.01f),
        fog = new Color(0.14f, 0.06f, 0.03f),
        fogDensity = 0.020f,
        bloom = 2.0f,
    };

    public override Vector2 HalfExtent => new Vector2(15f, 15f);
    public override float CoverHalfExtent => 13f;
    public override int CoverCount => 20;
    public override float[] DropPlanes => new[] { 0f, 3f };

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-8f, 0f, -15f), new Vector3(-4f, 0f, -16f), new Vector3(8f, 0f, -15f) }
            : new[] { new Vector3(-8f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(8f, 0f, 15f) };
    }

    // Nothing here is panelled sci-fi: the deck is quarried basalt, the walls
    // are firebrick, and the only metal is the tread plate underfoot.
    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Stone;
    public override float CoverFeatureSize => 1.8f;
    public override float CoverRoughness => 0.95f;

    public override void Build(Transform root, ArenaKit kit)
    {
        var basalt = ArenaMaterials.Style("Foundry_Basalt", ArenaMaterials.SurfaceStyle.Stone,
                                          new Color(0.12f, 0.10f, 0.10f), new Color(0.03f, 0.025f, 0.02f),
                                          3.5f, roughness: 0.97f, bump: 1.5f, cavity: 0.7f);
        // Riveted iron plate. Hull with NO emission: the panel-and-rivet pattern
        // without the glow is heavy industrial plating, and it keeps masonry as
        // ZIGGURAT's signature rather than something two arenas share.
        var ironPlate = ArenaMaterials.Style("Foundry_Iron", ArenaMaterials.SurfaceStyle.Hull,
                                             new Color(0.17f, 0.14f, 0.12f), new Color(0.06f, 0.05f, 0.04f),
                                             2.2f, roughness: 0.62f, emitStrength: 0f,
                                             bump: 1.2f, cavity: 0.6f);
        // Tread plate — glossy enough to catch the lava light and read as metal.
        var steel = ArenaMaterials.Style("Foundry_Tread", ArenaMaterials.SurfaceStyle.Tread,
                                         new Color(0.20f, 0.18f, 0.17f), new Color(0.07f, 0.06f, 0.06f),
                                         0.9f, roughness: 0.40f, bump: 1.1f, cavity: 0.5f);
        var wallMat = ironPlate;
        var lavaMat = ArenaMaterials.Emissive("Foundry_Lava", Lava, 2.3f);

        kit.Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), basalt);

        // Perimeter.
        kit.Box("WallN", new Vector3(0f, 2f, 20f), new Vector3(40f, 4f, 0.5f), wallMat);
        kit.Box("WallS", new Vector3(0f, 2f, -20f), new Vector3(40f, 4f, 0.5f), wallMat);
        kit.Box("WallE", new Vector3(20f, 2f, 0f), new Vector3(0.5f, 4f, 40f), wallMat);
        kit.Box("WallW", new Vector3(-20f, 2f, 0f), new Vector3(0.5f, 4f, 40f), wallMat);

        // Lava channels, inlaid flush with the deck so nothing blocks movement —
        // they are light sources and landmarks, not obstacles.
        var channels = new (Vector3 centre, Vector3 size)[]
        {
            (new Vector3(-12f, 0.02f, 0f), new Vector3(3.5f, 0.06f, 22f)),
            (new Vector3(12f, 0.02f, 0f), new Vector3(3.5f, 0.06f, 22f)),
            (new Vector3(0f, 0.02f, 0f), new Vector3(16f, 0.06f, 3f)),
        };
        foreach (var (centre, size) in channels)
        {
            kit.Decor("Lava", centre, size, lavaMat);
            kit.Point(centre + Vector3.up * 1.2f, Lava, 3f, 12f);
        }

        // Two catwalk spans at 3 m, crossing the channels below.
        kit.Platform("CatwalkW", new Vector2(-12f, 0f), new Vector2(4f, 24f), 3f, steel);
        kit.Platform("CatwalkE", new Vector2(12f, 0f), new Vector2(4f, 24f), 3f, steel);
        kit.Platform("CatwalkMid", new Vector2(0f, 0f), new Vector2(20f, 3.5f), 3f, steel);

        // Access from the deck. Long runs keep these under the 40° ceiling.
        kit.Ramp("RampSW", new Vector3(-12f, 0f, -16f), new Vector3(-12f, 3f, -11f), 2.4f, steel);
        kit.Ramp("RampSE", new Vector3(12f, 0f, -16f), new Vector3(12f, 3f, -11f), 2.4f, steel);
        kit.Ramp("RampNW", new Vector3(-12f, 0f, 16f), new Vector3(-12f, 3f, 11f), 2.4f, steel);
        kit.Ramp("RampNE", new Vector3(12f, 0f, 16f), new Vector3(12f, 3f, 11f), 2.4f, steel);

        // Bots drop off the catwalk ends instead of walking the long way back.
        kit.Link(new Vector3(-9.8f, 3f, 6f), new Vector3(-8f, 0f, 6f));
        kit.Link(new Vector3(9.8f, 3f, -6f), new Vector3(8f, 0f, -6f));

        // Smelter columns — the tall cover on the deck.
        var columns = new[]
        {
            new Vector3(-5f, 0f, 7f), new Vector3(5f, 0f, -7f),
            new Vector3(-5f, 0f, -7f), new Vector3(5f, 0f, 7f),
        };
        foreach (var c in columns)
        {
            kit.Cylinder("Smelter", c, 1.1f, 4.5f, steel);
            kit.Decor("SmelterGlow", c + Vector3.up * 4.6f, new Vector3(1.6f, 0.18f, 1.6f), lavaMat);
        }

        kit.Directional("Foundry Key", new Color(1f, 0.75f, 0.5f), 0.85f, new Vector3(55f, -35f, 0f), true);
        kit.Directional("Foundry Fill", Ember, 0.35f, new Vector3(20f, 150f, 0f));
    }
}

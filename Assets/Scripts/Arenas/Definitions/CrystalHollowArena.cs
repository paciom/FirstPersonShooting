using UnityEngine;

/// <summary>
/// A cavern grown through with violet crystal. Low ceiling over the middle,
/// rough rock underfoot, and great faceted spires leaning out of the floor.
///
/// The crystals are the only light source of consequence, and they are lit
/// objects rather than glow strips — flat cut facets, each catching the light at
/// its own angle. The arena is deliberately the darkest of the set, so the
/// silhouettes of robots against a lit spire do the reading.
/// </summary>
public class CrystalHollowArena : ArenaDefinition
{
    static readonly Color Violet = new Color(0.62f, 0.34f, 0.95f);
    static readonly Color IceBlue = new Color(0.38f, 0.78f, 0.95f);

    public override string DisplayName => "CRYSTAL HOLLOW";
    public override string Tagline => "A cave that grew teeth. Mind the low ceiling.";
    public override int Levels => 2;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.13f, 0.11f, 0.15f),
        wall = new Color(0.17f, 0.14f, 0.19f),
        accentA = Violet,
        accentB = IceBlue,
        ambient = new Color(0.16f, 0.14f, 0.20f),
        keyLight = new Color(0.62f, 0.66f, 0.85f),
        keyIntensity = 0.45f,
        sky = new Color(0.03f, 0.02f, 0.05f),
        fog = new Color(0.07f, 0.05f, 0.10f),
        fogDensity = 0.024f,
        bloom = 1.9f,
    };

    public override Vector2 HalfExtent => new Vector2(15f, 15f);
    public override float CoverHalfExtent => 13f;
    public override int CoverCount => 20;
    public override float[] DropPlanes => new[] { 0f, 2.8f };

    // Cover is broken crystal — faceted, and the only cover in the set that
    // catches a specular highlight.
    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Crystal;
    public override float CoverFeatureSize => 1.3f;
    public override float CoverRoughness => 0.30f;

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-9f, 0f, -15f), new Vector3(-3f, 0f, -16f), new Vector3(9f, 0f, -15f) }
            : new[] { new Vector3(-9f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(9f, 0f, 15f) };
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var rock = ArenaMaterials.Style("Cry_Rock", ArenaMaterials.SurfaceStyle.Stone,
                                        new Color(0.15f, 0.13f, 0.17f), new Color(0.05f, 0.04f, 0.07f),
                                        3.4f, roughness: 0.97f, bump: 1.6f, cavity: 0.72f);
        var rockPale = ArenaMaterials.Style("Cry_RockPale", ArenaMaterials.SurfaceStyle.Stone,
                                            new Color(0.22f, 0.19f, 0.25f), new Color(0.08f, 0.07f, 0.11f),
                                            2.6f, roughness: 0.95f, bump: 1.4f, cavity: 0.66f);
        var crystal = ArenaMaterials.Style("Cry_Violet", ArenaMaterials.SurfaceStyle.Crystal,
                                           new Color(0.34f, 0.18f, 0.52f), new Color(0.16f, 0.09f, 0.28f),
                                           1.5f, roughness: 0.18f, emit: Violet, emitStrength: 1.8f,
                                           bump: 0.8f, cavity: 0.35f);
        var crystalIce = ArenaMaterials.Style("Cry_Ice", ArenaMaterials.SurfaceStyle.Crystal,
                                              new Color(0.18f, 0.30f, 0.40f), new Color(0.08f, 0.15f, 0.22f),
                                              1.7f, roughness: 0.15f, emit: IceBlue, emitStrength: 1.5f,
                                              bump: 0.8f, cavity: 0.35f);

        kit.Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), rock);

        // Cave walls, and a ceiling slab over the middle. At 7.5 m it clears the
        // 2.2 m the NavMesh needs by a wide margin, but it is low enough to read
        // as a roof and to matter to the spectator camera.
        kit.Box("CaveN", new Vector3(0f, 4f, 20f), new Vector3(40f, 8f, 0.8f), rock);
        kit.Box("CaveS", new Vector3(0f, 4f, -20f), new Vector3(40f, 8f, 0.8f), rock);
        kit.Box("CaveE", new Vector3(20f, 4f, 0f), new Vector3(0.8f, 8f, 40f), rock);
        kit.Box("CaveW", new Vector3(-20f, 4f, 0f), new Vector3(0.8f, 8f, 40f), rock);
        kit.Box("Roof", new Vector3(0f, 7.9f, 0f), new Vector3(26f, 0.8f, 26f), rockPale);

        // Rock shelves — the upper level, tucked against two walls.
        kit.Platform("ShelfW", new Vector2(-15f, 0f), new Vector2(8f, 14f), 2.8f, rockPale, 0.6f);
        kit.Platform("ShelfE", new Vector2(15f, 0f), new Vector2(8f, 14f), 2.8f, rockPale, 0.6f);
        kit.Ramp("ShelfRampW", new Vector3(-10f, 0f, -9f), new Vector3(-13f, 2.8f, -5f), 2.6f, rockPale);
        kit.Ramp("ShelfRampE", new Vector3(10f, 0f, 9f), new Vector3(13f, 2.8f, 5f), 2.6f, rockPale);
        kit.Link(new Vector3(-11.2f, 2.8f, 5f), new Vector3(-9.6f, 0f, 5f));
        kit.Link(new Vector3(11.2f, 2.8f, -5f), new Vector3(9.6f, 0f, -5f));

        // --- crystal spires ---
        var rng = new System.Random(5517);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        for (int i = 0; i < 20; i++)
        {
            float angle = (i / 20f) * Mathf.PI * 2f + Next(-0.25f, 0.25f);
            float radius = Next(5f, 17f);
            var basePos = new Vector3(Mathf.Cos(angle) * radius, -0.4f, Mathf.Sin(angle) * radius);
            float tall = Next(2.5f, 6.5f);
            // Spires lean, which is most of what stops them reading as pillars.
            var tip = basePos + new Vector3(Next(-0.9f, 0.9f), tall, Next(-0.9f, 0.9f));
            var mat = (i % 4 == 0) ? crystalIce : crystal;

            kit.Beam("Spire", basePos, tip, Next(0.5f, 1.3f), mat);
            kit.Point(tip, (i % 4 == 0) ? IceBlue : Violet, 1.5f, 10f);

            // A cluster of smaller shards around the base.
            int shards = 2 + (int)Next(0f, 2.99f);
            for (int s = 0; s < shards; s++)
            {
                float sa = Next(0f, Mathf.PI * 2f);
                var sBase = basePos + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * Next(0.9f, 2f);
                var sTip = sBase + new Vector3(Next(-0.5f, 0.5f), Next(1f, 2.4f), Next(-0.5f, 0.5f));
                kit.Beam("Shard", sBase, sTip, Next(0.25f, 0.55f), mat);
            }
        }

        // Crystals growing down from the roof, so the ceiling is not a blank
        // slab when the camera ducks under it.
        for (int i = 0; i < 10; i++)
        {
            var p = new Vector3(Next(-11f, 11f), 7.5f, Next(-11f, 11f));
            kit.Beam("Stalactite", p, p + new Vector3(Next(-0.4f, 0.4f), -Next(1.5f, 3.5f), Next(-0.4f, 0.4f)),
                     Next(0.25f, 0.6f), (i % 3 == 0) ? crystalIce : crystal, collide: false);
        }

        // Barely any key light — the crystals are meant to be the light.
        kit.Directional("Cave Key", new Color(0.62f, 0.66f, 0.85f), 0.45f, new Vector3(58f, -30f, 0f), true);
        kit.Directional("Crystal Fill", Violet, 0.28f, new Vector3(20f, 160f, 0f));
        kit.Directional("Ice Fill", IceBlue, 0.20f, new Vector3(20f, 340f, 0f));
    }
}

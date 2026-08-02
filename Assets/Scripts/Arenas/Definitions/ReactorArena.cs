using UnityEngine;

/// <summary>
/// The heart of a capital ship: an eight-sided reactor chamber, tiered
/// down to a core that glows red through the deck grating, ringed by a
/// gallery of lit alcoves and roofed with girders that converge overhead.
///
/// Where DRYDOCK is a rectangular working bay, this one is RADIAL, and
/// that changes how it plays as much as how it looks: every approach to
/// the middle is a spoke, every spoke is covered by a bulkhead, and the
/// gallery looks down on all of it. Symmetry is the point — a reactor is
/// engineered, not stacked — but it is broken deliberately in three
/// places, because perfect eightfold symmetry reads as a screensaver.
///
/// Low-poly as ever: boxes, cylinders and beams. The expense is in
/// tiering, edge trim and the colour split — cold blue gallery, warm
/// amber walkways, red core.
/// </summary>
public class ReactorArena : ArenaDefinition
{
    static readonly Color Amber = new Color(1f, 0.68f, 0.26f);
    static readonly Color Ice = new Color(0.38f, 0.78f, 1f);
    static readonly Color CoreRed = new Color(1f, 0.22f, 0.16f);

    const int Sides = 8;
    const float DeckRadius = 17f;
    const float GalleryY = 3.6f;

    public override string DisplayName => "REACTOR";
    public override string Tagline => "Eight ways in, one core. The gallery sees everything.";
    public override int Levels => 3;
    public override bool HasHorizon => false;   // deep inside a hull

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.16f, 0.17f, 0.19f),
        wall = new Color(0.19f, 0.21f, 0.24f),
        accentA = Amber,
        accentB = Ice,
        ambient = new Color(0.12f, 0.14f, 0.18f),
        keyLight = new Color(0.66f, 0.76f, 0.92f),
        keyIntensity = 0.7f,
        sky = new Color(0.04f, 0.05f, 0.07f),
        fog = new Color(0.07f, 0.09f, 0.12f),
        fogDensity = 0.026f,
        bloom = 1.3f,
    };

    public override Vector2 HalfExtent => new Vector2(17f, 17f);
    public override float CoverHalfExtent => 14f;
    public override int CoverCount => 26;
    public override float[] DropPlanes => new[] { 0f, 1.2f, 3.6f };

    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Hull;
    public override float CoverFeatureSize => 1f;
    public override float CoverRoughness => 0.55f;

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-10f, 0f, -12f), new Vector3(0f, 0f, -14f), new Vector3(10f, 0f, -12f) }
            : new[] { new Vector3(-10f, 0f, 12f), new Vector3(0f, 0f, 14f), new Vector3(10f, 0f, 12f) };
    }

    /// <summary>The core dais places its own cover; keep the churn off it.</summary>
    public override bool IsOpenFloor(Vector3 point)
    {
        return new Vector2(point.x, point.z).magnitude > 6.5f;
    }

    static Vector3 Ring(float angleIndex, float radius, float y)
    {
        float a = angleIndex / Sides * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
    }

    static float Facing(float angleIndex) => -angleIndex / Sides * 360f + 90f;

    public override void Build(Transform root, ArenaKit kit)
    {
        var deck = ArenaMaterials.Style("Rx_Deck", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.18f, 0.20f, 0.22f), new Color(0.07f, 0.08f, 0.09f),
            1.5f, roughness: 0.5f, bump: 0.9f, cavity: 0.6f);
        var hull = ArenaMaterials.Style("Rx_Hull", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.20f, 0.22f, 0.26f), new Color(0.06f, 0.07f, 0.09f),
            2.4f, roughness: 0.45f, emit: Ice, emitStrength: 0.15f, bump: 1.1f);
        var dark = ArenaMaterials.Style("Rx_Dark", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.10f, 0.11f, 0.13f), new Color(0.03f, 0.04f, 0.05f),
            1.9f, roughness: 0.65f, bump: 1.2f);
        var grate = ArenaMaterials.Style("Rx_Grate", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.12f, 0.13f, 0.15f), new Color(0.04f, 0.04f, 0.05f),
            0.5f, roughness: 0.7f, bump: 1.4f);
        var hazard = ArenaMaterials.Emissive("Rx_Hazard", Amber, 1.1f);
        var trim = ArenaMaterials.Emissive("Rx_Trim", Amber, 1.7f);
        var coolTrim = ArenaMaterials.Emissive("Rx_Cool", Ice, 1.6f);
        var core = ArenaMaterials.Emissive("Rx_Core", CoreRed, 2.1f);

        kit.Box("Deck", new Vector3(0f, -0.5f, 0f), new Vector3(42f, 1f, 42f), deck);

        BuildShell(kit, hull, dark, coolTrim, trim);
        BuildGallery(kit, deck, dark, hazard, trim);
        BuildCore(kit, deck, dark, grate, hazard, core);
        BuildSpokes(kit, grate, hazard, dark);
        BuildCeiling(kit, dark, trim);
        BuildLights(kit);
    }

    // ---------------------------------------------------------------- shell

    /// <summary>Eight wall bays, each with a recessed lit alcove.</summary>
    static void BuildShell(ArenaKit kit, Material hull, Material dark,
                           Material coolTrim, Material trim)
    {
        for (int i = 0; i < Sides; i++)
        {
            float yaw = Facing(i);
            var at = Ring(i, DeckRadius + 1.6f, 0f);

            kit.Box($"Bay{i}Low", at + Vector3.up * 3f, new Vector3(15f, 6f, 1.6f), hull, yaw);
            kit.Box($"Bay{i}Mid", at + Vector3.up * 8.6f, new Vector3(15f, 5.2f, 1.6f), dark, yaw);
            kit.Box($"Bay{i}Top", at + Vector3.up * 13.4f, new Vector3(15f, 4.4f, 1.6f), hull, yaw);

            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 Local(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            // The alcove: a dark recess with a cold panel at the back. Two
            // per bay, off-centre, so the ring never reads as wallpaper.
            foreach (float side in new[] { -3.6f, 3.6f })
            {
                kit.Box($"Alcove{i}{side}", Local(side, 2.6f, -0.95f),
                        new Vector3(4.2f, 4.4f, 0.5f), dark, yaw);
                kit.Decor($"AlcoveLit{i}{side}", Local(side, 2.6f, -1.25f),
                          new Vector3(3.2f, 3.4f, 0.12f), coolTrim, yaw);
            }

            // Ribs framing the bay, and the lit band along its head.
            foreach (float side in new[] { -7f, 7f })
                kit.Box($"Rib{i}{side}", Local(side, 5.5f, -0.9f),
                        new Vector3(1.3f, 11f, 0.9f), dark, yaw);
            kit.Decor($"BayBand{i}", Local(0f, 6.15f, -0.95f),
                      new Vector3(14f, 0.12f, 0.1f), trim, yaw);
        }
    }

    // -------------------------------------------------------------- gallery

    /// <summary>
    /// The upper ring. Not continuous — four segments with gaps, so it is
    /// a vantage point to fight for rather than a safe lap of the room.
    /// </summary>
    static void BuildGallery(ArenaKit kit, Material deck, Material dark,
                             Material hazard, Material trim)
    {
        for (int i = 0; i < Sides; i++)
        {
            // Broken symmetry, first of three: only every other bay carries
            // a gallery segment.
            if (i % 2 == 1)
                continue;

            float yaw = Facing(i);
            var at = Ring(i, DeckRadius - 2.6f, 0f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 Local(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            kit.Box($"Gal{i}", Local(0f, GalleryY - 0.3f, 0f),
                    new Vector3(11f, 0.6f, 4.6f), deck, yaw);
            kit.Box($"GalRail{i}", Local(0f, GalleryY + 0.5f, -2.2f),
                    new Vector3(11f, 1f, 0.16f), dark, yaw);
            kit.Decor($"GalRailLit{i}", Local(0f, GalleryY + 1.02f, -2.2f),
                      new Vector3(11f, 0.07f, 0.22f), trim, yaw);
            kit.Decor($"GalLip{i}", Local(0f, GalleryY + 0.02f, -2.3f),
                      new Vector3(11f, 0.03f, 0.3f), hazard, yaw);

            foreach (float side in new[] { -4.6f, 4.6f })
                kit.Box($"GalLeg{i}{side}", Local(side, GalleryY * 0.5f, 1.6f),
                        new Vector3(1f, GalleryY, 1f), dark, yaw);

            // Stairs down into the room, angled off the spoke so the
            // landing is not in the open.
            kit.Stair($"GalStair{i}", Local(4.2f, 0f, -4.6f), Local(4.2f, GalleryY, -1.4f),
                      2.4f, deck);
        }
    }

    // ----------------------------------------------------------------- core

    /// <summary>
    /// Two octagonal tiers stepping up to the core housing, red light
    /// leaking out between them. Raised, never sunken: a pit in the deck
    /// is where terrain probes and cameras go wrong.
    /// </summary>
    static void BuildCore(ArenaKit kit, Material deck, Material dark,
                          Material grate, Material hazard, Material core)
    {
        kit.Cylinder("CoreTier1", new Vector3(0f, 0f, 0f), 6.2f, 0.6f, deck);
        kit.Cylinder("CoreTier2", new Vector3(0f, 0.6f, 0f), 4.2f, 0.6f, deck);

        // The glow escapes from under each tier lip, which is what makes
        // the core feel like it is BELOW rather than painted on top.
        kit.Cylinder("CoreGlow1", new Vector3(0f, 0.52f, 0f), 6.35f, 0.12f, core, collide: false);
        kit.Cylinder("CoreGlow2", new Vector3(0f, 1.12f, 0f), 4.35f, 0.12f, core, collide: false);

        // The housing: a squat drum with vent slots, capped in red.
        kit.Cylinder("CoreDrum", new Vector3(0f, 1.2f, 0f), 2.1f, 2.4f, dark);
        kit.Cylinder("CoreCap", new Vector3(0f, 3.6f, 0f), 1.7f, 0.35f, core, collide: false);
        for (int i = 0; i < Sides; i++)
            kit.Decor($"CoreVent{i}", Ring(i, 2.15f, 2.3f),
                      new Vector3(0.9f, 1.4f, 0.1f), core, Facing(i));

        // Bulkhead consoles around the rim: the cover that makes charging
        // the middle survivable.
        for (int i = 0; i < Sides; i++)
        {
            // Broken symmetry, second: one station is missing, and that
            // gap is the obvious way in.
            if (i == 3)
                continue;
            float yaw = Facing(i) + 22.5f;
            var at = Ring(i + 0.5f, 7.6f, 0f);
            kit.Box($"Console{i}", at + Vector3.up * 0.75f, new Vector3(3.4f, 1.5f, 1.3f), dark, yaw);
            kit.Decor($"ConsoleLit{i}", at + Vector3.up * 1.52f,
                      new Vector3(2.6f, 0.06f, 0.7f), hazard, yaw);
        }

        // Grated apron so the floor around the core is not blank.
        for (int i = 0; i < Sides; i++)
            kit.Decor($"Apron{i}", Ring(i + 0.5f, 10.5f, 0.02f),
                      new Vector3(5f, 0.04f, 3.2f), grate, Facing(i) + 22.5f);
    }

    // --------------------------------------------------------------- spokes

    /// <summary>Walkways from the rim to the core, edged in hazard paint.</summary>
    static void BuildSpokes(ArenaKit kit, Material grate, Material hazard, Material dark)
    {
        for (int i = 0; i < Sides; i++)
        {
            float yaw = Facing(i);
            var mid = Ring(i, 11.5f, 0.03f);
            kit.Decor($"Spoke{i}", mid, new Vector3(3.4f, 0.05f, 10f), grate, yaw);
            foreach (float side in new[] { -1.75f, 1.75f })
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                kit.Decor($"SpokeEdge{i}{side}", mid + rot * new Vector3(side, 0.01f, 0f),
                          new Vector3(0.26f, 0.05f, 10f), hazard, yaw);
            }
            // Step up onto the core apron.
            kit.Box($"SpokeStep{i}", Ring(i, 6.6f, 0.15f),
                    new Vector3(3.4f, 0.3f, 1.2f), dark, yaw);
        }
    }

    // -------------------------------------------------------------- ceiling

    /// <summary>Girders converging on a boss overhead — the roof of a hull.</summary>
    static void BuildCeiling(ArenaKit kit, Material dark, Material trim)
    {
        for (int i = 0; i < Sides; i++)
        {
            kit.Beam($"Girder{i}", Ring(i, DeckRadius, 11.5f), new Vector3(0f, 16.5f, 0f),
                     0.75f, dark, collide: false);
            kit.Beam($"Tie{i}", Ring(i, DeckRadius - 1f, 11f), Ring(i + 1, DeckRadius - 1f, 11f),
                     0.5f, dark, collide: false);
        }
        kit.Cylinder("Boss", new Vector3(0f, 16.2f, 0f), 2.6f, 1.4f, dark, collide: false);
        kit.Cylinder("BossLit", new Vector3(0f, 16.05f, 0f), 2f, 0.2f, trim, collide: false);
    }

    // --------------------------------------------------------------- lights

    static void BuildLights(ArenaKit kit)
    {
        kit.Directional("Rx Key", new Color(0.64f, 0.74f, 0.90f), 0.70f,
                        new Vector3(62f, 20f, 0f), true);
        kit.Directional("Rx Fill", new Color(0.26f, 0.38f, 0.55f), 0.28f,
                        new Vector3(18f, 200f, 0f));

        // The core: the only strong warm source low down, so it throws the
        // consoles into silhouette from anywhere on the deck.
        kit.Point(new Vector3(0f, 1.6f, 0f), CoreRed, 3.2f, 17f);
        kit.Point(new Vector3(0f, 3.9f, 0f), CoreRed, 1.6f, 11f);

        for (int i = 0; i < Sides; i++)
        {
            // Broken symmetry, third: one bay's lamp is dead, leaving a
            // dark corner worth using.
            if (i == 6)
                continue;
            kit.Point(Ring(i, DeckRadius - 3f, 5.2f), Ice, 1.35f, 12f);
            kit.Point(Ring(i + 0.5f, DeckRadius - 6.5f, 2.4f), Amber, 1.1f, 9f);
        }
    }
}

using UnityEngine;

/// <summary>
/// Cyberspace: a black void with an electric wireframe floor and platforms
/// floating over it. Built first among the generated arenas on purpose — it is
/// the furthest thing from a walled neon box, so it flushes out any lingering
/// assumption that an arena has walls and a solid floor.
///
/// The boundary is invisible collision behind glowing rails rather than walls,
/// so the arena reads as open void while nobody can actually walk off it.
/// </summary>
public class GridspaceArena : ArenaDefinition
{
    static readonly Color Cyan = new Color(0.2f, 0.95f, 1f);
    static readonly Color Magenta = new Color(1f, 0.25f, 0.9f);

    public override string DisplayName => "GRIDSPACE";
    public override string Tagline => "A black void, a neon grid, and platforms hanging in nothing.";
    public override int Levels => 2;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.015f, 0.02f, 0.03f),
        wall = new Color(0.02f, 0.03f, 0.05f),
        accentA = Cyan,
        accentB = Magenta,
        ambient = new Color(0.06f, 0.09f, 0.14f),
        keyLight = new Color(0.4f, 0.6f, 0.9f),
        keyIntensity = 0.35f,
        sky = Color.black,
        fog = new Color(0.01f, 0.02f, 0.04f),
        fogDensity = 0.012f,
        bloom = 2.6f,
    };

    public override Vector2 HalfExtent => new Vector2(15f, 15f);
    public override float CoverHalfExtent => 12f;
    public override int CoverCount => 14;
    public override float[] DropPlanes => new[] { 0f, 2.4f };

    public override Vector3 PlayerSpawn => new Vector3(0f, 0.1f, -15f);

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-7f, 0f, -15f), new Vector3(-3f, 0f, -16f), new Vector3(7f, 0f, -15f) }
            : new[] { new Vector3(-7f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(7f, 0f, 15f) };
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var grid = ArenaMaterials.Surface("Grid_Floor", new Color(0.015f, 0.02f, 0.03f),
                                          Cyan, 2f, 2.0f, 2);
        var slab = ArenaMaterials.Surface("Grid_Slab", new Color(0.03f, 0.05f, 0.08f),
                                          Cyan, 1.4f, 1.4f, 3);
        var railCyan = ArenaMaterials.Emissive("Grid_RailCyan", Cyan, 2.2f);
        var railMagenta = ArenaMaterials.Emissive("Grid_RailMagenta", Magenta, 2.2f);

        kit.Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), grid);

        // Glowing edge rails with invisible collision behind them: the look is
        // an open void, the behaviour is a closed arena.
        for (int i = 0; i < 4; i++)
        {
            bool northSouth = i < 2;
            float sign = (i % 2 == 0) ? 1f : -1f;
            Vector3 centre = northSouth ? new Vector3(0f, 0f, 19f * sign) : new Vector3(19f * sign, 0f, 0f);
            Vector3 railSize = northSouth ? new Vector3(38f, 0.12f, 0.12f) : new Vector3(0.12f, 0.12f, 38f);
            Vector3 wallSize = northSouth ? new Vector3(38f, 4f, 0.4f) : new Vector3(0.4f, 4f, 38f);

            kit.Decor("Rail", centre + Vector3.up * 0.35f, railSize, northSouth ? railCyan : railMagenta);
            var wall = kit.Box("Bound", centre + Vector3.up * 2f, wallSize, grid);
            wall.GetComponent<MeshRenderer>().enabled = false;
        }

        // Floating platforms, each reachable by a ramp so bots can use them.
        var decks = new (Vector2 centre, Vector2 size, float top)[]
        {
            (new Vector2(-9f, 0f), new Vector2(7f, 7f), 2.4f),
            (new Vector2(9f, 0f), new Vector2(7f, 7f), 2.4f),
            (new Vector2(0f, 8f), new Vector2(6f, 5f), 1.6f),
            (new Vector2(0f, -8f), new Vector2(6f, 5f), 1.6f),
            (new Vector2(0f, 0f), new Vector2(5f, 5f), 3.4f),
        };
        foreach (var (centre, size, top) in decks)
            kit.Platform("Deck", centre, size, top, slab);

        // Ramps up to each deck, kept shallow and wide enough for the bake.
        kit.Ramp("RampW", new Vector3(-14f, 0f, -4f), new Vector3(-10f, 2.4f, -1f), 2f, slab);
        kit.Ramp("RampE", new Vector3(14f, 0f, 4f), new Vector3(10f, 2.4f, 1f), 2f, slab);
        kit.Ramp("RampN", new Vector3(4f, 0f, 11f), new Vector3(1.5f, 1.6f, 9f), 2f, slab);
        kit.Ramp("RampS", new Vector3(-4f, 0f, -11f), new Vector3(-1.5f, 1.6f, -9f), 2f, slab);
        // Centre spire is reached from the two low decks.
        kit.Ramp("RampC1", new Vector3(0f, 1.6f, 6f), new Vector3(0f, 3.4f, 2.8f), 2f, slab);
        kit.Ramp("RampC2", new Vector3(0f, 1.6f, -6f), new Vector3(0f, 3.4f, -2.8f), 2f, slab);

        // Bots will step off a high deck rather than walk all the way around.
        kit.Link(new Vector3(-9f, 2.4f, -3.6f), new Vector3(-9f, 0f, -5.2f));
        kit.Link(new Vector3(9f, 2.4f, 3.6f), new Vector3(9f, 0f, 5.2f));

        // Light pylons — the only thing standing in the void.
        for (int i = 0; i < 4; i++)
        {
            float x = (i < 2 ? -1f : 1f) * 16f;
            float z = (i % 2 == 0 ? -1f : 1f) * 16f;
            var mat = (i % 2 == 0) ? railCyan : railMagenta;
            kit.Decor("Pylon", new Vector3(x, 3f, z), new Vector3(0.35f, 6f, 0.35f), mat);
            kit.Point(new Vector3(x, 4f, z), (i % 2 == 0) ? Cyan : Magenta, 2.2f, 16f);
        }

        kit.Directional("Grid Key", new Color(0.45f, 0.65f, 0.95f), 0.35f, new Vector3(55f, -35f, 0f), true);
        kit.Directional("Grid Fill Cyan", Cyan, 0.30f, new Vector3(25f, 200f, 0f));
        kit.Directional("Grid Fill Magenta", Magenta, 0.26f, new Vector3(25f, 20f, 0f));
    }
}

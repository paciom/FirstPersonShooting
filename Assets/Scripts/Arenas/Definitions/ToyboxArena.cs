using UnityEngine;

/// <summary>
/// A giant's desk, with the robots shrunk to toy scale: building blocks,
/// pencils, dice and a mug, plus a book stack you reach up a ruler.
///
/// Deliberately the tonal outlier of the set — flat primary colours and bright
/// even light instead of neon-on-black. Bloom is turned right down, because at
/// these brightnesses the house setting makes every surface bleed into every
/// other one.
/// </summary>
public class ToyboxArena : ArenaDefinition
{
    static readonly Color ToyRed = new Color(0.85f, 0.18f, 0.16f);
    static readonly Color ToyBlue = new Color(0.15f, 0.38f, 0.82f);
    static readonly Color ToyYellow = new Color(0.95f, 0.78f, 0.12f);
    static readonly Color ToyGreen = new Color(0.22f, 0.68f, 0.28f);

    public override string DisplayName => "TOYBOX";
    public override string Tagline => "You are three inches tall. The desk is the whole world.";
    public override int Levels => 2;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.55f, 0.38f, 0.24f),
        wall = new Color(0.72f, 0.60f, 0.45f),
        accentA = ToyRed,
        accentB = ToyBlue,
        ambient = new Color(0.55f, 0.54f, 0.52f),
        keyLight = new Color(1f, 0.97f, 0.90f),
        keyIntensity = 1.5f,
        sky = new Color(0.62f, 0.72f, 0.85f),
        fog = new Color(0.75f, 0.72f, 0.68f),
        fogDensity = 0f,
        bloom = 0.7f,
    };

    public override Vector2 HalfExtent => new Vector2(15f, 15f);
    public override float CoverHalfExtent => 12f;
    public override int CoverCount => 18;
    public override float[] DropPlanes => new[] { 0f, 2.4f };

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-8f, 0f, -15f), new Vector3(-4f, 0f, -16f), new Vector3(8f, 0f, -15f) }
            : new[] { new Vector3(-8f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(8f, 0f, 15f) };
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var desk = ArenaMaterials.Lit("Toy_Desk", new Color(0.55f, 0.38f, 0.24f), 0.15f);
        var rim = ArenaMaterials.Lit("Toy_Rim", new Color(0.40f, 0.27f, 0.17f), 0.2f);
        var paper = ArenaMaterials.Lit("Toy_Paper", new Color(0.93f, 0.91f, 0.86f), 0.05f);
        var red = ArenaMaterials.Lit("Toy_Red", ToyRed, 0.45f);
        var blue = ArenaMaterials.Lit("Toy_Blue", ToyBlue, 0.45f);
        var yellow = ArenaMaterials.Lit("Toy_Yellow", ToyYellow, 0.45f);
        var green = ArenaMaterials.Lit("Toy_Green", ToyGreen, 0.45f);
        var white = ArenaMaterials.Lit("Toy_White", new Color(0.94f, 0.94f, 0.94f), 0.3f);

        kit.Box("Desk", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), desk);

        // The desk edge — a raised lip rather than walls.
        kit.Box("LipN", new Vector3(0f, 1f, 20f), new Vector3(40f, 2f, 0.8f), rim);
        kit.Box("LipS", new Vector3(0f, 1f, -20f), new Vector3(40f, 2f, 0.8f), rim);
        kit.Box("LipE", new Vector3(20f, 1f, 0f), new Vector3(0.8f, 2f, 40f), rim);
        kit.Box("LipW", new Vector3(-20f, 1f, 0f), new Vector3(0.8f, 2f, 40f), rim);

        // A sheet of paper across the middle of the desk — a flat landmark.
        kit.Decor("Paper", new Vector3(2f, 0.01f, 0f), new Vector3(14f, 0.02f, 10f), paper, 8f);

        // Book stack: the upper level, reached by the ruler.
        var books = new[] { red, blue, green };
        for (int i = 0; i < books.Length; i++)
        {
            kit.Box($"Book{i}", new Vector3(-11f, 0.4f + i * 0.8f, 8f),
                    new Vector3(9f, 0.8f, 7f), books[i], 6f * i);
        }
        // Top of the stack sits at 2.4 m.
        kit.Ramp("Ruler", new Vector3(-4f, 0f, 3.5f), new Vector3(-8.5f, 2.4f, 6f), 1.6f, yellow, 0.22f);
        kit.Link(new Vector3(-13f, 2.4f, 11f), new Vector3(-13f, 0f, 12.6f));

        // Building blocks — the chunky cover.
        var blocks = new (Vector3 pos, Vector3 size, Material mat, float yaw)[]
        {
            (new Vector3(8f, 0.9f, -6f), new Vector3(3.4f, 1.8f, 3.4f), red, 20f),
            (new Vector3(12f, 1.3f, 3f), new Vector3(2.6f, 2.6f, 2.6f), blue, -15f),
            (new Vector3(4f, 0.7f, 9f), new Vector3(3f, 1.4f, 3f), green, 35f),
            (new Vector3(-6f, 1.1f, -10f), new Vector3(2.8f, 2.2f, 2.8f), yellow, 0f),
            (new Vector3(14f, 0.8f, -12f), new Vector3(3.2f, 1.6f, 3.2f), white, 45f),
        };
        foreach (var (pos, size, mat, yaw) in blocks)
            kit.Box("Block", pos, size, mat, yaw);

        // Pencils lying across the desk — long, low, shootable-around cover.
        kit.Box("Pencil1", new Vector3(-2f, 0.35f, -8f), new Vector3(16f, 0.7f, 0.7f), yellow, 18f);
        kit.Box("Pencil2", new Vector3(7f, 0.35f, 13f), new Vector3(13f, 0.7f, 0.7f), red, -32f);

        // A mug, and dice.
        kit.Cylinder("Mug", new Vector3(15f, 0f, 12f), 2.2f, 3f, blue);
        kit.Box("Die1", new Vector3(-14f, 0.75f, -4f), new Vector3(1.5f, 1.5f, 1.5f), white, 25f);
        kit.Box("Die2", new Vector3(-12.5f, 0.75f, -1.5f), new Vector3(1.5f, 1.5f, 1.5f), white, -10f);

        // Bright, even desk-lamp light. No neon fills — this arena is lit like a
        // room, which is most of why it does not read as sci-fi.
        kit.Directional("Desk Lamp", new Color(1f, 0.97f, 0.90f), 1.5f, new Vector3(50f, -30f, 0f), true);
        kit.Directional("Desk Bounce", new Color(0.75f, 0.80f, 0.95f), 0.35f, new Vector3(20f, 150f, 0f));
        kit.Point(new Vector3(0f, 9f, 0f), new Color(1f, 0.95f, 0.85f), 1.6f, 26f);
    }
}

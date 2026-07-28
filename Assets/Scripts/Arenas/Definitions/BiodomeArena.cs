using UnityEngine;

/// <summary>
/// An alien greenhouse gone feral: mossy ground, fleshy stalks the height of a
/// building, and bioluminescent pods that light the place from inside.
///
/// The light here comes from the plants themselves rather than from strip trim,
/// which is the difference between "bioluminescence" and "neon". Everything
/// organic is round — cylinders, spheres, drooping fronds — against a set of
/// arenas otherwise made of boxes.
/// </summary>
public class BiodomeArena : ArenaDefinition
{
    static readonly Color Lime = new Color(0.55f, 0.95f, 0.35f);
    static readonly Color Bloom = new Color(0.95f, 0.38f, 0.66f);

    public override string DisplayName => "BIODOME";
    public override string Tagline => "Something grew in here. It is still growing.";
    public override int Levels => 2;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.17f, 0.21f, 0.13f),
        wall = new Color(0.21f, 0.17f, 0.25f),
        accentA = Lime,
        accentB = Bloom,
        ambient = new Color(0.26f, 0.31f, 0.24f),
        keyLight = new Color(0.82f, 0.92f, 0.72f),
        keyIntensity = 0.85f,
        sky = new Color(0.09f, 0.13f, 0.09f),
        // Thick, humid air. The fog is doing real work here — it is what makes
        // the far side of the dome feel like somewhere you have not been yet.
        fog = new Color(0.13f, 0.19f, 0.14f),
        fogDensity = 0.030f,
        bloom = 1.5f,
    };

    public override Vector2 HalfExtent => new Vector2(15f, 15f);
    public override float CoverHalfExtent => 13f;
    public override int CoverCount => 18;
    public override float[] DropPlanes => new[] { 0f, 3.2f };

    // Cover is seed pods: soft, veined, faintly lit from within.
    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Organic;
    public override float CoverFeatureSize => 1.5f;
    public override float CoverRoughness => 0.75f;

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-9f, 0f, -15f), new Vector3(-3f, 0f, -16f), new Vector3(9f, 0f, -15f) }
            : new[] { new Vector3(-9f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(9f, 0f, 15f) };
    }

    /// <summary>Keep cover out from under the mother stalk.</summary>
    public override bool IsOpenFloor(Vector3 point)
    {
        return new Vector2(point.x, point.z).magnitude > 5.5f;
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var moss = ArenaMaterials.Style("Bio_Moss", ArenaMaterials.SurfaceStyle.Organic,
                                        new Color(0.20f, 0.26f, 0.15f), new Color(0.10f, 0.15f, 0.09f),
                                        2.5f, roughness: 0.88f, emit: Lime, emitStrength: 0.25f,
                                        bump: 1.1f, cavity: 0.5f);
        var rock = ArenaMaterials.Style("Bio_Rock", ArenaMaterials.SurfaceStyle.Stone,
                                        new Color(0.22f, 0.18f, 0.26f), new Color(0.09f, 0.07f, 0.12f),
                                        3.2f, roughness: 0.96f, bump: 1.4f, cavity: 0.65f);
        var stalk = ArenaMaterials.Style("Bio_Stalk", ArenaMaterials.SurfaceStyle.Organic,
                                         new Color(0.30f, 0.40f, 0.22f), new Color(0.14f, 0.22f, 0.12f),
                                         1.6f, roughness: 0.70f, emit: Lime, emitStrength: 0.55f,
                                         bump: 1.3f, cavity: 0.55f);
        var frond = ArenaMaterials.Style("Bio_Frond", ArenaMaterials.SurfaceStyle.Organic,
                                         new Color(0.26f, 0.36f, 0.18f), new Color(0.12f, 0.18f, 0.10f),
                                         1.2f, roughness: 0.75f, emit: Lime, emitStrength: 0.35f,
                                         bump: 0.9f, cavity: 0.4f);
        var pod = ArenaMaterials.Emissive("Bio_Pod", Bloom, 1.5f);
        var podLime = ArenaMaterials.Emissive("Bio_PodLime", Lime, 1.4f);

        kit.Box("Ground", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), moss);

        // Dome wall — rough rock, taller than the sci-fi arenas so the stalks
        // have somewhere to reach toward.
        kit.Box("RockN", new Vector3(0f, 3f, 20f), new Vector3(40f, 6f, 0.6f), rock);
        kit.Box("RockS", new Vector3(0f, 3f, -20f), new Vector3(40f, 6f, 0.6f), rock);
        kit.Box("RockE", new Vector3(20f, 3f, 0f), new Vector3(0.6f, 6f, 40f), rock);
        kit.Box("RockW", new Vector3(-20f, 3f, 0f), new Vector3(0.6f, 6f, 40f), rock);

        // --- the mother stalk, and the root bridge that rings it ---
        kit.Cylinder("MotherStalk", new Vector3(0f, 0f, 0f), 2.6f, 11f, stalk);
        kit.Sphere("MotherCrown", new Vector3(0f, 11.4f, 0f), 2.2f, pod, collide: false);
        kit.Point(new Vector3(0f, 10.5f, 0f), Bloom, 2.4f, 20f);

        // Four root platforms at 3.2 m, joined to the ground by sloping roots.
        var roots = new[]
        {
            new Vector2(-7.5f, -7.5f), new Vector2(7.5f, -7.5f),
            new Vector2(-7.5f, 7.5f), new Vector2(7.5f, 7.5f),
        };
        foreach (var r in roots)
        {
            kit.Platform("RootDeck", r, new Vector2(6f, 6f), 3.2f, stalk, 0.5f);
            // Ramp in from the outside, so the climb is away from the middle.
            var outward = r.normalized * 5.5f;
            kit.Ramp("RootRamp",
                     new Vector3(r.x + outward.x, 0f, r.y + outward.y),
                     new Vector3(r.x + outward.x * 0.35f, 3.2f, r.y + outward.y * 0.35f),
                     2.4f, stalk);
            kit.Link(new Vector3(r.x, 3.2f, r.y - 3.2f), new Vector3(r.x, 0f, r.y - 4.6f));
        }

        // --- scattered stalks with drooping fronds ---
        var rng = new System.Random(9142);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        for (int i = 0; i < 14; i++)
        {
            float angle = (i / 14f) * Mathf.PI * 2f + Next(-0.2f, 0.2f);
            float radius = Next(9f, 17f);
            var basePos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            float tall = Next(4.5f, 9f);
            float thick = Next(0.5f, 1.1f);

            kit.Cylinder("Stalk", basePos, thick, tall, stalk);

            // Fronds drooping outward from the crown. Beams, not ramps: nobody
            // walks on these, so the slope limits are irrelevant.
            var crown = basePos + Vector3.up * tall;
            int fronds = 3 + (int)Next(0f, 2.99f);
            for (int f = 0; f < fronds; f++)
            {
                float fa = angle + f * (Mathf.PI * 2f / fronds) + Next(-0.3f, 0.3f);
                var tip = crown + new Vector3(Mathf.Cos(fa), -0.55f, Mathf.Sin(fa)) * Next(1.8f, 3.2f);
                kit.Beam("Frond", crown, tip, Next(0.18f, 0.32f), frond, collide: false);
            }

            kit.Sphere("Pod", crown + Vector3.up * 0.4f, Next(0.45f, 0.8f),
                       (i % 3 == 0) ? pod : podLime, collide: false);
            kit.Point(crown, (i % 3 == 0) ? Bloom : Lime, 1.3f, 9f);
        }

        // Low ground bulbs, so the floor is lit too and the fog has something to
        // catch near eye level.
        for (int i = 0; i < 10; i++)
        {
            var p = new Vector3(Next(-16f, 16f), 0f, Next(-16f, 16f));
            if (new Vector2(p.x, p.z).magnitude < 6f)
                continue;
            kit.Sphere("GroundBulb", p + Vector3.up * 0.45f, Next(0.35f, 0.65f), podLime, collide: false);
            kit.Point(p + Vector3.up * 0.6f, Lime, 0.9f, 6f);
        }

        // Soft, diffuse, from above — light through a canopy, not a spotlight rig.
        kit.Directional("Canopy Light", new Color(0.82f, 0.92f, 0.72f), 0.85f,
                        new Vector3(68f, -25f, 0f), true);
        kit.Directional("Under Bounce", new Color(0.40f, 0.55f, 0.35f), 0.30f, new Vector3(-30f, 140f, 0f));
    }
}

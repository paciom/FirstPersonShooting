using UnityEngine;

/// <summary>
/// A working ship dock: a deep armoured bay with mezzanine catwalks, a
/// gantry crossing overhead, sunken service pits and cargo stacked
/// wherever there was room.
///
/// Built from a concept image, and the lesson of that image is that the
/// expensive look is NOT expensive geometry — everything in it is boxes.
/// It comes from four things, which are what this arena spends its budget
/// on:
///
/// 1. DENSITY. Real industrial space is cluttered. Long empty floor is
///    what makes a level read as a prototype (and gives a shooter nowhere
///    to hide).
/// 2. LAYERS. Floor, mezzanine, gantry — so every sightline crosses
///    something at a different height.
/// 3. MANY SMALL WARM LIGHTS against a cold ambient. A hundred pools of
///    amber in blue gloom is the entire industrial-sci-fi look. Most of
///    them here are EMISSIVE STRIPS rather than real lights, which cost
///    nothing and bloom just the same.
/// 4. EDGE MARKING. Hazard stripes and lit trim on every platform lip.
///    Painted edges are what say "people work here".
/// </summary>
public class DrydockArena : ArenaDefinition
{
    static readonly Color Amber = new Color(1f, 0.66f, 0.24f);
    static readonly Color Teal = new Color(0.26f, 0.86f, 0.96f);

    public override string DisplayName => "DRYDOCK";
    public override string Tagline => "Cargo, catwalks and gantries. Cover in every direction.";
    public override int Levels => 3;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.15f, 0.17f, 0.19f),
        wall = new Color(0.17f, 0.19f, 0.23f),
        accentA = Amber,
        accentB = Teal,
        // Cold and dark, so every warm practical reads. A bright ambient
        // would wash the whole effect out.
        ambient = new Color(0.13f, 0.16f, 0.20f),
        keyLight = new Color(0.72f, 0.82f, 0.95f),
        keyIntensity = 0.75f,
        sky = new Color(0.05f, 0.07f, 0.09f),
        fog = new Color(0.08f, 0.11f, 0.14f),
        fogDensity = 0.024f,
        bloom = 1.25f,
    };

    public override Vector2 HalfExtent => new Vector2(18f, 18f);
    public override float CoverHalfExtent => 15f;
    public override int CoverCount => 30;
    public override float[] DropPlanes => new[] { 0f, 3.4f, 6.8f };

    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Tread;
    public override float CoverFeatureSize => 0.9f;
    public override float CoverRoughness => 0.6f;

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-11f, 0f, -15f), new Vector3(-3f, 0f, -16f), new Vector3(9f, 0f, -15f) }
            : new[] { new Vector3(-9f, 0f, 15f), new Vector3(2f, 0f, 16f), new Vector3(11f, 0f, 15f) };
    }

    /// <summary>Keep churning cover out from under the gantry legs.</summary>
    public override bool IsOpenFloor(Vector3 point)
    {
        return Mathf.Abs(point.x) > 2.4f || Mathf.Abs(point.z) > 9f;
    }

    public override void Build(Transform root, ArenaKit kit)
    {
        var deck = ArenaMaterials.Style("Dock_Deck", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.17f, 0.19f, 0.22f), new Color(0.07f, 0.08f, 0.10f),
            1.6f, roughness: 0.55f, bump: 0.9f, cavity: 0.6f);
        var hull = ArenaMaterials.Style("Dock_Hull", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.18f, 0.20f, 0.24f), new Color(0.06f, 0.07f, 0.09f),
            2.2f, roughness: 0.5f, emit: Teal, emitStrength: 0.18f, bump: 1.1f);
        var dark = ArenaMaterials.Style("Dock_Dark", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.10f, 0.11f, 0.14f), new Color(0.04f, 0.05f, 0.06f),
            1.8f, roughness: 0.7f, bump: 1.2f);
        var crate = ArenaMaterials.Style("Dock_Crate", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.22f, 0.21f, 0.19f), new Color(0.08f, 0.08f, 0.07f),
            0.8f, roughness: 0.6f, bump: 1f);
        var hazard = ArenaMaterials.Emissive("Dock_Hazard", Amber, 1.1f);
        var strip = ArenaMaterials.Emissive("Dock_Strip", Amber, 1.7f);
        var coolStrip = ArenaMaterials.Emissive("Dock_Cool", Teal, 1.5f);

        kit.Box("Deck", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), deck);

        BuildWalls(kit, hull, dark, coolStrip, strip);
        BuildPits(kit, dark, hazard);
        BuildMezzanine(kit, deck, dark, hazard, strip);
        BuildGantry(kit, deck, dark, hazard);
        BuildCargo(kit, crate, hazard);
        BuildLights(kit);
    }

    // ---------------------------------------------------------------- walls

    /// <summary>
    /// The bay's shell, and the reason it reads as tall: the walls are
    /// built in three stacked courses with a lit service band between
    /// them, not one flat slab.
    /// </summary>
    static void BuildWalls(ArenaKit kit, Material hull, Material dark,
                           Material coolStrip, Material strip)
    {
        foreach (var side in new[] { 0, 1, 2, 3 })
        {
            float yaw = side * 90f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 At(float x, float y, float z) => rot * new Vector3(x, y, z);

            // Three courses, each set back a little from the one below.
            kit.Box($"WallLow{side}", At(0f, 2.2f, 19.5f), new Vector3(40f, 4.4f, 1.2f), hull, yaw);
            kit.Box($"WallMid{side}", At(0f, 6.6f, 19.9f), new Vector3(40f, 4.4f, 1.2f), dark, yaw);
            kit.Box($"WallTop{side}", At(0f, 11f, 19.6f), new Vector3(40f, 4.4f, 1.2f), hull, yaw);

            // The lit service band where two courses meet — the single
            // strongest depth cue on a flat wall.
            kit.Decor($"WallBand{side}", At(0f, 4.5f, 18.85f), new Vector3(38f, 0.14f, 0.1f), coolStrip, yaw);
            kit.Decor($"WallBand2_{side}", At(0f, 8.9f, 19.25f), new Vector3(38f, 0.1f, 0.1f), strip, yaw);

            // Ribs and pipe runs break the span vertically.
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 5.4f;
                kit.Box($"Rib{side}_{i}", At(x, 4.4f, 18.8f), new Vector3(1.1f, 8.8f, 0.5f), dark, yaw);
                kit.Cylinder($"Pipe{side}_{i}", At(x + 2.4f, 0f, 18.6f), 0.22f, 9.5f, dark);
                // Recessed windows, lit from somewhere behind.
                kit.Decor($"Port{side}_{i}", At(x, 6.6f, 19.2f), new Vector3(2.6f, 1.5f, 0.12f), coolStrip, yaw);
            }
        }
    }

    // ----------------------------------------------------------------- pits

    /// <summary>
    /// Sunken service bays. They cost nothing (a rim, a floor) and give
    /// the deck relief so it is not one unbroken plane.
    /// </summary>
    static void BuildPits(ArenaKit kit, Material dark, Material hazard)
    {
        var spots = new[]
        {
            new Vector2(-9.5f, -6f), new Vector2(9.5f, 6f),
            new Vector2(8f, -9.5f), new Vector2(-8f, 9.5f),
        };
        foreach (var spot in spots)
        {
            // A raised lip reads as "recessed" from inside the arena and
            // needs no hole in the deck — floors with holes in them are
            // where terrain probes go wrong.
            var size = new Vector2(5.5f, 4.5f);
            foreach (var edge in new[] { -1f, 1f })
            {
                kit.Box($"PitRimX{spot}{edge}",
                    new Vector3(spot.x + edge * size.x * 0.5f, 0.22f, spot.y),
                    new Vector3(0.5f, 0.44f, size.y), dark);
                kit.Box($"PitRimZ{spot}{edge}",
                    new Vector3(spot.x, 0.22f, spot.y + edge * size.y * 0.5f),
                    new Vector3(size.x, 0.44f, 0.5f), dark);
            }
            kit.Decor($"PitStripe{spot}", new Vector3(spot.x, 0.46f, spot.y),
                new Vector3(size.x + 0.1f, 0.03f, 0.28f), hazard);
        }
    }

    // ------------------------------------------------------------ mezzanine

    static void BuildMezzanine(ArenaKit kit, Material deck, Material dark,
                               Material hazard, Material strip)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * 13.5f;
            kit.Platform($"Mezz{side}", new Vector2(x, 0f), new Vector2(7f, 26f), 3.4f, deck, 0.6f);

            // Railing: two thin rails, lit along the top. Cheap, and it
            // stops the walkway reading as a floating slab.
            float inner = x - side * 3.4f;
            kit.Box($"MezzRail{side}", new Vector3(inner, 3.9f, 0f), new Vector3(0.16f, 1f, 26f), dark);
            kit.Decor($"MezzRailLit{side}", new Vector3(inner, 4.42f, 0f),
                new Vector3(0.22f, 0.07f, 26f), strip);
            kit.Decor($"MezzLip{side}", new Vector3(inner - side * 0.2f, 3.42f, 0f),
                new Vector3(0.3f, 0.03f, 26f), hazard);

            // Legs.
            for (int i = -2; i <= 2; i++)
                kit.Box($"MezzLeg{side}_{i}", new Vector3(x, 1.7f, i * 6f),
                    new Vector3(1.1f, 3.4f, 1.1f), dark);

            // Stairs down to the deck at both ends, so the level is not a
            // dead end for anything that walks up it.
            foreach (float end in new[] { -1f, 1f })
                kit.Stair($"MezzStair{side}{end}",
                    new Vector3(x - side * 4.5f, 0f, end * 9.5f),
                    new Vector3(x - side * 0.5f, 3.4f, end * 9.5f), 2.6f, deck);
        }
    }

    // --------------------------------------------------------------- gantry

    /// <summary>The bridge overhead: the third level, and the arena's ceiling interest.</summary>
    static void BuildGantry(ArenaKit kit, Material deck, Material dark, Material hazard)
    {
        kit.Platform("Gantry", new Vector2(0f, 0f), new Vector2(4.5f, 27f), 6.8f, deck, 0.5f);
        foreach (float side in new[] { -1f, 1f })
        {
            kit.Box($"GantryRail{side}", new Vector3(side * 2.1f, 7.3f, 0f),
                new Vector3(0.14f, 1f, 27f), dark);
            kit.Decor($"GantryLip{side}", new Vector3(side * 2.1f, 6.82f, 0f),
                new Vector3(0.28f, 0.03f, 27f), hazard);
        }
        // Trusses down to the mezzanines — supported, not floating.
        foreach (float z in new[] { -9f, 0f, 9f })
            foreach (float side in new[] { -1f, 1f })
                kit.Beam($"Truss{z}{side}", new Vector3(side * 2.2f, 6.6f, z),
                    new Vector3(side * 10.5f, 3.6f, z), 0.35f, dark);

        // Stairs up from each mezzanine.
        foreach (float side in new[] { -1f, 1f })
            kit.Stair($"GantryStair{side}",
                new Vector3(side * 9.5f, 3.4f, side * 4f),
                new Vector3(side * 2.4f, 6.8f, side * 4f), 2.4f, deck);
    }

    // ---------------------------------------------------------------- cargo

    /// <summary>
    /// Containers, stacked and scattered. These are STATIC cover, on top of
    /// the churning blocks — a shooter wants some cover it can memorise.
    /// </summary>
    static void BuildCargo(ArenaKit kit, Material crate, Material hazard)
    {
        var stacks = new (Vector3 pos, Vector3 size, float yaw)[]
        {
            (new Vector3(-5.5f, 0f, -12.5f), new Vector3(4.4f, 2.2f, 2.4f), 8f),
            (new Vector3(-5.2f, 2.2f, -12.2f), new Vector3(3.2f, 1.8f, 2.2f), -6f),
            (new Vector3(6f, 0f, -11f), new Vector3(2.6f, 2.6f, 2.6f), -14f),
            (new Vector3(12f, 0f, -3f), new Vector3(2.4f, 3.2f, 2.4f), 5f),
            (new Vector3(-12f, 0f, 4f), new Vector3(4.6f, 2.4f, 2.6f), -4f),
            (new Vector3(4.5f, 0f, 12f), new Vector3(4.2f, 2.2f, 2.4f), 12f),
            (new Vector3(4.2f, 2.2f, 12.3f), new Vector3(2.6f, 1.9f, 2.2f), -9f),
            (new Vector3(-3f, 0f, 8.5f), new Vector3(2.2f, 1.6f, 2.2f), 22f),
            (new Vector3(9.5f, 0f, 9.5f), new Vector3(2.8f, 2.8f, 2.8f), -18f),
        };

        foreach (var (pos, size, yaw) in stacks)
        {
            var centre = pos + Vector3.up * (size.y * 0.5f);
            kit.Box("Container", centre, size, crate, yaw);
            // A painted band along the top edge: the detail that separates
            // "cargo container" from "grey box".
            kit.Decor("ContainerBand", centre + Vector3.up * (size.y * 0.5f - 0.18f),
                new Vector3(size.x + 0.04f, 0.12f, size.z + 0.04f), hazard, yaw);
        }
    }

    // --------------------------------------------------------------- lights

    /// <summary>
    /// Few real lights, many bright surfaces. Every practical here is a
    /// pool of warm light in cold gloom — but only a dozen of them cost
    /// anything, because the rest of the glow is emissive geometry that
    /// blooms for free.
    /// </summary>
    static void BuildLights(ArenaKit kit)
    {
        // Cool key from high up, the only shadow caster.
        kit.Directional("Dock Key", new Color(0.70f, 0.80f, 0.95f), 0.75f,
                        new Vector3(55f, 35f, 0f), true);
        kit.Directional("Dock Fill", new Color(0.30f, 0.45f, 0.60f), 0.30f,
                        new Vector3(20f, 215f, 0f));

        // Wall practicals: amber, short range, low down where they rake
        // across the floor and the cargo.
        foreach (float side in new[] { -1f, 1f })
            for (int i = -2; i <= 2; i++)
            {
                kit.Point(new Vector3(side * 16f, 3.2f, i * 7.5f), Amber, 1.5f, 11f);
                kit.Point(new Vector3(i * 7.5f, 3.2f, side * 16f), Amber, 1.3f, 10f);
            }

        // Two teal accents under the gantry, for the colour contrast the
        // reference leans on.
        kit.Point(new Vector3(0f, 6f, -8f), Teal, 1.6f, 14f);
        kit.Point(new Vector3(0f, 6f, 8f), Teal, 1.6f, 14f);
    }
}

using UnityEngine;

/// <summary>
/// A nuclear power station under an open sky: a concrete yard split between a
/// turbine hall in the south and a HUGE cooling tower in the north — and the
/// tower is not scenery. Four doorways lead inside to a coolant basin, riser
/// pipes, and a broken gallery ring at three metres, so the set-piece of the
/// skyline is also the best room in the arena to fight over.
///
/// Where REACTOR is the inside of a machine, this is the OUTSIDE of one: the
/// sun does the key lighting, the machines are painted mint-green the way real
/// turbine halls are, and the only glow is coolant cyan and hazard amber.
/// Concrete, steel and paint — not one neon seam.
///
/// The middle of the field is ruled by the two condenser pipes running from
/// the turbine deck into the tower wall: waist-high cover lines that funnel a
/// charge toward the tower's south mouth.
/// </summary>
public class MeltdownArena : ArenaDefinition
{
    static readonly Color Coolant = new Color(0.30f, 0.85f, 1f);
    static readonly Color Amber = new Color(1f, 0.68f, 0.26f);

    // The tower owns the north half. Everything about it hangs off these.
    const float TowerZ = 11f;
    const float TowerR = 8.8f;       // base ring centreline radius
    const int ShellSides = 16;       // base + shell ring segments
    const int GallerySides = 8;      // interior gallery segments
    const float GalleryY = 3.2f;
    const float DeckTop = 1.6f;      // turbine deck walking surface

    public override string DisplayName => "MELTDOWN";
    public override string Tagline => "A power station with an open door. The cooling tower is the arena inside the arena.";
    public override int Levels => 3;

    public override ArenaPalette Palette => new ArenaPalette
    {
        floor = new Color(0.46f, 0.46f, 0.44f),
        wall = new Color(0.56f, 0.56f, 0.53f),
        accentA = Coolant,
        accentB = Amber,
        ambient = new Color(0.42f, 0.45f, 0.48f),
        keyLight = new Color(0.98f, 0.95f, 0.88f),
        keyIntensity = 1.35f,
        sky = new Color(0.56f, 0.64f, 0.73f),
        fog = new Color(0.62f, 0.67f, 0.72f),
        fogDensity = 0.007f,
        // Daylit concrete: the house 2.2 would bloom the sunlit tower to mush.
        bloom = 1.1f,
    };

    public override Vector2 HalfExtent => new Vector2(22f, 22f);
    public override float CoverHalfExtent => 20f;
    public override int CoverCount => 24;
    public override float[] DropPlanes => new[] { 0f, DeckTop, GalleryY };

    // Cover reads as painted steel crates and cabinets, kin to the machines.
    public override ArenaMaterials.SurfaceStyle CoverStyle => ArenaMaterials.SurfaceStyle.Hull;
    public override float CoverFeatureSize => 1.1f;
    public override float CoverRoughness => 0.6f;

    public override Vector3 PlayerSpawn => new Vector3(0f, 0.1f, -23f);
    public override Vector3 SpectatorPerch => new Vector3(0f, 10f, -24f);

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-10f, 0f, -22f), new Vector3(0f, 0f, -23f), new Vector3(10f, 0f, -22f) }
            : new[] { new Vector3(-10f, 0f, 22f), new Vector3(0f, 0f, 23f), new Vector3(10f, 0f, 22f) };
    }

    /// <summary>
    /// The default ±16 line lands inside the cooling tower — team one's
    /// reinforcements would materialize inside the shell wall. Push both
    /// lines out to the yards behind each side's spawns.
    /// </summary>
    public override Vector3 ReinforcementLine(int teamId)
    {
        return new Vector3(Random.Range(-8f, 8f), 0f, teamId == 0 ? -22.5f : 22.5f);
    }

    /// <summary>
    /// The tower, the turbine deck, the mezzanine, the dome, the pipes and
    /// the transformer pad all place their own geometry — keep churned
    /// cover out of every one of their footprints.
    /// </summary>
    public override bool IsOpenFloor(Vector3 point)
    {
        // Tower footprint, walls included. Its interior furnishes itself.
        if (new Vector2(point.x, point.z - TowerZ).magnitude < 10.4f)
            return false;
        // Turbine deck plus its end stairs.
        if (Mathf.Abs(point.x) < 12.8f && point.z > -11.8f && point.z < -4.2f)
            return false;
        // Control mezzanine and its stair, against the west wall.
        if (point.x < -16.5f && point.z > -12.5f && point.z < -2.5f)
            return false;
        // Transformer pad, south-east corner.
        if (point.x > 13f && point.z < -13.5f)
            return false;
        // Containment dome, east yard.
        if (new Vector2(point.x - 19.5f, point.z - 5f).magnitude < 6.6f)
            return false;
        // Condenser pipes.
        if (Mathf.Abs(Mathf.Abs(point.x) - 5.5f) < 1.5f && point.z > -7f && point.z < 5f)
            return false;
        // Vent stack, west yard.
        if (new Vector2(point.x + 16f, point.z - 16f).magnitude < 2.6f)
            return false;
        return true;
    }

    // Ring points around the tower axis. idx runs 0..sides, 0 = east (+X).
    static Vector3 Ring(float idx, int sides, float radius, float y)
    {
        float a = idx / sides * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a) * radius, y, TowerZ + Mathf.Sin(a) * radius);
    }

    static float Facing(float idx, int sides) => -idx / sides * 360f + 90f;

    public override void Build(Transform root, ArenaKit kit)
    {
        var yard = ArenaMaterials.Style("Md_Yard", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.46f, 0.46f, 0.44f), new Color(0.30f, 0.30f, 0.28f),
            3.5f, roughness: 0.92f, bump: 1.1f, cavity: 0.5f);
        var conc = ArenaMaterials.Style("Md_Conc", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.58f, 0.58f, 0.55f), new Color(0.38f, 0.38f, 0.36f),
            2.6f, roughness: 0.9f, bump: 1.2f, cavity: 0.55f);
        var concDark = ArenaMaterials.Style("Md_ConcDark", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.44f, 0.43f, 0.41f), new Color(0.28f, 0.28f, 0.26f),
            2.8f, roughness: 0.93f, bump: 1.2f, cavity: 0.6f);
        var tower = ArenaMaterials.Style("Md_Tower", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.63f, 0.64f, 0.62f), new Color(0.42f, 0.43f, 0.42f),
            3f, roughness: 0.88f, bump: 1.1f, cavity: 0.5f);
        var damp = ArenaMaterials.Style("Md_Damp", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.33f, 0.37f, 0.36f), new Color(0.19f, 0.23f, 0.22f),
            2.2f, roughness: 0.95f, bump: 1.2f, cavity: 0.6f);
        var steel = ArenaMaterials.Style("Md_Steel", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.30f, 0.33f, 0.37f), new Color(0.12f, 0.13f, 0.15f),
            1.6f, roughness: 0.5f, bump: 1.2f);
        var mint = ArenaMaterials.Style("Md_Mint", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.22f, 0.46f, 0.40f), new Color(0.10f, 0.20f, 0.18f),
            2f, roughness: 0.45f, bump: 1f);
        var tread = ArenaMaterials.Style("Md_Tread", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.20f, 0.21f, 0.23f), new Color(0.08f, 0.08f, 0.09f),
            0.6f, roughness: 0.6f, bump: 1.4f);
        var hazard = ArenaMaterials.Emissive("Md_Hazard", Amber, 1.1f);
        var coolant = ArenaMaterials.Emissive("Md_Coolant", Coolant, 1.6f);
        var window = ArenaMaterials.Emissive("Md_Window", new Color(1f, 0.85f, 0.55f), 1.35f);

        kit.Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(52f, 1f, 52f), yard);

        BuildPerimeter(kit, conc, concDark);
        BuildTowerShell(kit, tower, hazard);
        BuildTowerInterior(kit, damp, steel, tread, hazard, coolant);
        BuildTurbineHall(kit, tread, steel, mint, hazard, coolant);
        BuildMezzanine(kit, conc, tread, steel, window, hazard);
        BuildYardWorks(kit, conc, concDark, steel, hazard, coolant);
        BuildLights(kit);
    }

    // ------------------------------------------------------------ perimeter

    /// <summary>Site wall: concrete panels between pilasters, sky above.</summary>
    static void BuildPerimeter(ArenaKit kit, Material conc, Material concDark)
    {
        for (int side = 0; side < 4; side++)
        {
            for (int seg = 0; seg < 6; seg++)
            {
                float t = -21.25f + seg * 8.5f;
                Vector3 centre = side switch
                {
                    0 => new Vector3(t, 2.25f, 25.5f),
                    1 => new Vector3(t, 2.25f, -25.5f),
                    2 => new Vector3(25.5f, 2.25f, t),
                    _ => new Vector3(-25.5f, 2.25f, t),
                };
                Vector3 size = side < 2
                    ? new Vector3(8.6f, 4.5f, 1f)
                    : new Vector3(1f, 4.5f, 8.6f);
                kit.Box("SiteWall", centre, size, seg % 2 == 0 ? conc : concDark);

                Vector3 post = side switch
                {
                    0 => new Vector3(t - 4.25f, 2.5f, 25.5f),
                    1 => new Vector3(t - 4.25f, 2.5f, -25.5f),
                    2 => new Vector3(25.5f, 2.5f, t - 4.25f),
                    _ => new Vector3(-25.5f, 2.5f, t - 4.25f),
                };
                kit.Box("Pilaster", post, new Vector3(1.4f, 5f, 1.4f), concDark);
            }
        }
    }

    // ---------------------------------------------------------- tower shell

    /// <summary>
    /// The hyperboloid, stepped in low-poly rings: a collidable base storey
    /// with four doorways, then decor rings that narrow to the waist and
    /// flare back out at the crown. NOTHING above the base storey carries a
    /// collider — the upper shell leans in over walkable floor, and the
    /// terrain probe drops from 30 m; a solid ring up there would catch
    /// every spawn and reset under it. (See DrydockArena.BuildRoof.)
    /// </summary>
    static void BuildTowerShell(ArenaKit kit, Material tower, Material hazard)
    {
        // Base storey: 16 segments, gaps at the four compass points.
        for (int i = 0; i < ShellSides; i++)
        {
            bool doorway = i % 4 == 0;
            if (doorway)
            {
                // Lintel closes the arch from 3 m up — decor, no collider,
                // and 3 m of clearance is well past the 2.2 m bots need.
                kit.Decor($"TowerLintel{i}", Ring(i, ShellSides, TowerR, 4f),
                          new Vector3(3.8f, 2f, 1f), tower, Facing(i, ShellSides));
                kit.Decor($"TowerDoorTrim{i}", Ring(i, ShellSides, TowerR - 0.55f, 3.05f),
                          new Vector3(3.6f, 0.14f, 0.1f), hazard, Facing(i, ShellSides));
                continue;
            }
            kit.Box($"TowerBase{i}", Ring(i, ShellSides, TowerR, 2.5f),
                    new Vector3(3.6f, 5f, 1f), tower, Facing(i, ShellSides));
        }

        // Upper rings: radius pinches toward the waist, then flares.
        (float y, float h, float r)[] rings =
        {
            (6.5f, 3f, 8.35f),
            (9.5f, 3f, 7.75f),
            (12.25f, 2.5f, 7.4f),
            (14.75f, 2.5f, 7.75f),
        };
        foreach (var (y, h, r) in rings)
            for (int i = 0; i < ShellSides; i++)
                kit.Decor($"TowerShell{i}_{y}", Ring(i, ShellSides, r, y),
                          new Vector3(r * 0.42f, h, 0.9f), tower, Facing(i, ShellSides));

        // Crown lip, ringed in aviation amber.
        for (int i = 0; i < ShellSides; i++)
        {
            kit.Decor($"TowerLip{i}", Ring(i, ShellSides, 7.95f, 16.2f),
                      new Vector3(3.35f, 0.5f, 0.7f), tower, Facing(i, ShellSides));
            kit.Decor($"TowerLipBand{i}", Ring(i, ShellSides, 8.05f, 15.8f),
                      new Vector3(3.3f, 0.22f, 0.12f), hazard, Facing(i, ShellSides));
        }
    }

    // ------------------------------------------------------- tower interior

    /// <summary>
    /// The room inside: a raised coolant basin (raised, never sunken — pits
    /// are where terrain probes go wrong), riser pipes for hard cover, and a
    /// broken gallery ring with two stairs living in its gaps. Cyan light
    /// leaks from under the basin lip, REACTOR-core style.
    /// </summary>
    static void BuildTowerInterior(ArenaKit kit, Material damp, Material steel,
                                   Material tread, Material hazard, Material coolant)
    {
        var centre = new Vector3(0f, 0f, TowerZ);

        // Wet concrete apron across the whole interior floor.
        kit.Cylinder("BasinApron", centre, 8.1f, 0.06f, damp, collide: false);

        // The basin dais and the glow escaping under its lip.
        kit.Cylinder("Basin", centre, 4.6f, 0.4f, damp);
        kit.Cylinder("BasinGlow", centre + Vector3.up * 0.34f, 4.75f, 0.1f, coolant, collide: false);

        // Riser pipes and the pump hub — the cover that makes holding the
        // middle of the tower possible.
        kit.Cylinder("PumpHub", centre + Vector3.up * 0.4f, 1.1f, 1.8f, steel);
        kit.Cylinder("PumpCap", centre + Vector3.up * 2.2f, 0.8f, 0.12f, coolant, collide: false);
        for (int i = 0; i < 5; i++)
        {
            var at = Ring(i * (GallerySides / 5f) + 0.4f, GallerySides, 2.8f, 0.4f);
            kit.Cylinder($"Riser{i}", at, 0.35f, 3f, steel);
            kit.Cylinder($"RiserCap{i}", at + Vector3.up * 3f, 0.45f, 0.14f, coolant, collide: false);
        }

        // Gallery ring: six of eight segments — the gaps hold the stairs, so
        // the ring is a vantage to fight along, not a safe lap.
        for (int i = 0; i < GallerySides; i++)
        {
            if (i == 1 || i == 5)
                continue;

            float yaw = Facing(i, GallerySides);
            var at = Ring(i, GallerySides, 7.1f, 0f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 Local(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            kit.Box($"Gallery{i}", Local(0f, GalleryY - 0.25f, 0f),
                    new Vector3(5f, 0.5f, 2.2f), tread, yaw);
            // Legs sit under the OUTER edge, against the shell — the stairs
            // climb along the inner arc and would clip legs placed there.
            foreach (float side in new[] { -2.1f, 2.1f })
                kit.Box($"GalleryLeg{i}{side}", Local(side, GalleryY * 0.46f, 0.7f),
                        new Vector3(0.6f, GalleryY * 0.92f, 0.6f), steel, yaw);

            // Rails guard the inner edge — except on the two drop segments,
            // which get a hazard lip instead, and a one-way link down.
            if (i == 2 || i == 6)
            {
                kit.Decor($"GalleryLip{i}", Local(0f, GalleryY + 0.02f, -1.15f),
                          new Vector3(5f, 0.04f, 0.3f), hazard, yaw);
                kit.Link(Ring(i, GallerySides, 5.8f, GalleryY),
                         Ring(i, GallerySides, 5.4f, 0f), 1.5f, false);
            }
            else
            {
                kit.Box($"GalleryRail{i}", Local(0f, GalleryY + 0.45f, -1.1f),
                        new Vector3(5f, 0.9f, 0.14f), steel, yaw);
            }
        }

        // Stairs in the gallery gaps, wound along the wall like the tower's
        // own service stairs. Chords stay under the 40° ramp ceiling.
        kit.Stair("TowerStairNE", Ring(1.33f, GallerySides, 6.7f, 0f),
                  Ring(0.44f, GallerySides, 7.1f, GalleryY), 1.6f, tread);
        kit.Stair("TowerStairSW", Ring(5.33f, GallerySides, 6.7f, 0f),
                  Ring(4.44f, GallerySides, 7.1f, GalleryY), 1.6f, tread);
    }

    // --------------------------------------------------------- turbine hall

    /// <summary>
    /// The open-air turbine deck: a raised steel platform carrying the
    /// turbine drums and the generator, stairs at both ends, and a leap
    /// on and off the south edge that anything but a tank will take.
    /// </summary>
    static void BuildTurbineHall(ArenaKit kit, Material tread, Material steel,
                                 Material mint, Material hazard, Material coolant)
    {
        kit.Box("TurbineDeck", new Vector3(0f, DeckTop * 0.5f, -8f),
                new Vector3(18f, DeckTop, 5.2f), tread);

        kit.Stair("DeckStairE", new Vector3(11.8f, 0f, -8f), new Vector3(9f, DeckTop, -8f), 2.2f, tread);
        kit.Stair("DeckStairW", new Vector3(-11.8f, 0f, -8f), new Vector3(-9f, DeckTop, -8f), 2.2f, tread);

        // 1.6 m is inside MaxLeapUp, so these come out two-way: a shortcut
        // up for robots, a quick exit for everyone.
        kit.Link(new Vector3(4f, DeckTop, -10.4f), new Vector3(4f, 0f, -12.2f));
        kit.Link(new Vector3(-4f, DeckTop, -10.4f), new Vector3(-4f, 0f, -12.2f));

        // The turbine-generator line, painted mint the way real halls are.
        kit.Cylinder("TurbineHP", new Vector3(-5.2f, DeckTop, -8f), 1.35f, 1.5f, mint);
        kit.Cylinder("TurbineLP", new Vector3(-1.6f, DeckTop, -8f), 1.5f, 1.6f, mint);
        kit.Beam("TurbineShaft", new Vector3(-6.8f, DeckTop + 0.9f, -8f),
                 new Vector3(2.6f, DeckTop + 0.9f, -8f), 0.5f, steel, collide: false);
        kit.Box("Generator", new Vector3(4.6f, DeckTop + 0.9f, -8f),
                new Vector3(4.4f, 1.8f, 2.8f), mint);
        kit.Decor("GeneratorCap", new Vector3(7f, DeckTop + 0.9f, -8f),
                  new Vector3(0.5f, 1.4f, 2.2f), steel);
        kit.Decor("GeneratorLamp", new Vector3(4.6f, DeckTop + 1.86f, -8f),
                  new Vector3(1.8f, 0.08f, 0.5f), coolant);

        // Hazard paint along both working edges of the deck.
        kit.Decor("DeckEdgeN", new Vector3(0f, DeckTop + 0.03f, -5.55f),
                  new Vector3(18f, 0.05f, 0.3f), hazard);
        kit.Decor("DeckEdgeS", new Vector3(0f, DeckTop + 0.03f, -10.45f),
                  new Vector3(18f, 0.05f, 0.3f), hazard);
    }

    // ----------------------------------------------------------- mezzanine

    /// <summary>
    /// The control room: a mezzanine against the west wall with a lit
    /// window band, overlooking the hall and the tower's south mouth.
    /// </summary>
    static void BuildMezzanine(ArenaKit kit, Material conc, Material tread,
                               Material steel, Material window, Material hazard)
    {
        kit.Box("MezzDeck", new Vector3(-21.45f, GalleryY - 0.25f, -5f),
                new Vector3(6.9f, 0.5f, 4.6f), tread);
        foreach (float z in new[] { -7f, -3f })
            kit.Box($"MezzLeg{z}", new Vector3(-19f, GalleryY * 0.46f, z),
                    new Vector3(0.7f, GalleryY * 0.92f, 0.7f), conc);

        kit.Stair("MezzStair", new Vector3(-19.6f, 0f, -11.4f),
                  new Vector3(-19.6f, GalleryY, -7.3f), 2f, tread);

        // Rails on the exposed edges; the north edge stays open for the drop.
        kit.Box("MezzRailE", new Vector3(-18.1f, GalleryY + 0.45f, -5.6f),
                new Vector3(0.14f, 0.9f, 3.4f), steel);
        kit.Decor("MezzLip", new Vector3(-21.45f, GalleryY + 0.02f, -2.8f),
                  new Vector3(6.9f, 0.04f, 0.3f), hazard);
        kit.Link(new Vector3(-21.4f, GalleryY, -3f), new Vector3(-21.4f, 0f, -1.4f), 1.5f, false);

        // Consoles, and the control room's window band glowing on the wall.
        kit.Box("MezzConsole", new Vector3(-23.6f, GalleryY + 0.5f, -5f),
                new Vector3(1.2f, 1f, 3.6f), steel);
        kit.Decor("MezzWindow", new Vector3(-24.9f, GalleryY + 1.6f, -5f),
                  new Vector3(0.12f, 1.2f, 5.8f), window);
    }

    // ----------------------------------------------------------- yard works

    /// <summary>
    /// Everything that makes the yard a power station instead of a car
    /// park: condenser pipes, the containment dome, transformers, and the
    /// striped vent stack. Deliberately lopsided — dome east, stack west,
    /// transformers only in the south-east.
    /// </summary>
    static void BuildYardWorks(ArenaKit kit, Material conc, Material concDark,
                               Material steel, Material hazard, Material coolant)
    {
        // Condenser pipes: turbine deck to tower wall, waist-high cover
        // lines that shape every approach to the tower's south mouth.
        foreach (float x in new[] { -5.5f, 5.5f })
        {
            kit.Beam($"Pipe{x}", new Vector3(x, 0.85f, -5.6f),
                     new Vector3(x, 0.85f, 4.3f), 1.25f, steel);
            foreach (float z in new[] { -4f, -0.5f, 3f })
                kit.Box($"PipeSaddle{x}_{z}", new Vector3(x, 0.25f, z),
                        new Vector3(0.9f, 0.5f, 0.9f), concDark);
        }

        // Containment dome, half-buried against the east wall. A sphere is
        // "never floor", so nothing tries to stand on its crown.
        kit.Sphere("Dome", new Vector3(19.5f, -1.2f, 5f), 5.2f, conc);
        kit.Cylinder("DomeBand", new Vector3(19.5f, 0.9f, 5f), 4.9f, 0.25f, hazard, collide: false);

        // Transformer pad, south-east.
        kit.Decor("PadSlab", new Vector3(17f, 0.03f, -17f), new Vector3(7f, 0.06f, 6f), concDark);
        foreach (float x in new[] { 15.4f, 18.8f })
        {
            kit.Box($"Transformer{x}", new Vector3(x, 1.3f, -17f),
                    new Vector3(2.4f, 2.6f, 2f), steel);
            for (int b = 0; b < 3; b++)
                kit.Cylinder($"Bushing{x}_{b}", new Vector3(x - 0.7f + b * 0.7f, 2.6f, -17f),
                             0.12f, 0.7f, concDark, collide: false);
        }
        kit.Decor("PadStripe", new Vector3(17f, 0.07f, -13.9f),
                  new Vector3(7f, 0.05f, 0.3f), hazard);

        // Vent stack in the west yard, banded like every real one.
        kit.Cylinder("Stack", new Vector3(-16f, 0f, 16f), 1.15f, 13f, conc);
        foreach (float y in new[] { 10.4f, 12f })
            kit.Cylinder($"StackBand{y}", new Vector3(-16f, y, 16f), 1.22f, 0.6f, hazard, collide: false);
    }

    // --------------------------------------------------------------- lights

    static void BuildLights(ArenaKit kit)
    {
        // The sun does the work. High enough to throw a shaft down through
        // the tower's open crown.
        kit.Directional("Md Sun", new Color(0.98f, 0.95f, 0.88f), 1.35f,
                        new Vector3(54f, -32f, 0f), true);
        kit.Directional("Md Sky", new Color(0.55f, 0.62f, 0.75f), 0.4f,
                        new Vector3(-40f, 150f, 0f));

        // Coolant glow filling the tower from the basin up.
        kit.Point(new Vector3(0f, 2.4f, TowerZ), Coolant, 2.4f, 13f);
        kit.Point(new Vector3(0f, 6f, TowerZ), Coolant, 0.9f, 10f);

        // Warm practicals: the control room and the transformer pad. The
        // north-west yard gets nothing — the dark corner worth using.
        kit.Point(new Vector3(-21f, 4.8f, -5f), new Color(1f, 0.85f, 0.55f), 1.6f, 10f);
        kit.Point(new Vector3(17f, 3.2f, -17f), Amber, 1.2f, 9f);
    }
}

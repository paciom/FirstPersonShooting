using UnityEngine;

/// <summary>
/// Builds the Tower Defense battlefield: a 90 m plateau with a sunken canyon
/// lane snaking from a warp gate at the north edge down to the Photon Core
/// in a walled pocket at the south, and enough rocks, vents and crystal to
/// make the tableland look lived-on.
///
/// The canyon IS the pathing model. Raiders get exactly one order — march
/// to the Core — and the NavMesh bake can only route them down the lane,
/// because the plateau walls are taller than any agent can step. That is
/// also the whole placement law: towers build FREELY anywhere on the rim
/// (TDPlacer probes the footing), and because the buildable surface stands
/// above the route, no tower anywhere can ever block it.
///
/// Seeded like CommanderMap and under the same contract: one System.Random,
/// one fixed draw order, so a MAP CODE always rebuilds the same canyon.
/// </summary>
public static class TDMap
{
    /// <summary>Half-width of the battlefield. CommanderCamera clamps against this.</summary>
    public const float HalfExtent = 45f;

    public const float GroundY = 0f;

    /// <summary>
    /// Rim height. Three times an agent's 0.4 step so the bake can never
    /// connect lane to rim, low enough that towers shoot over their own
    /// cliff lip at every socket-to-lane angle the layout can produce.
    /// </summary>
    public const float PlateauY = 1.2f;

    /// <summary>The whole map is a grid of these; the lane is one cell wide.</summary>
    const float Cell = 5f;
    const int Cells = 18;   // 18 × 5 = 90 m

    /// <summary>The map code the current battlefield was rolled from.</summary>
    public static int CurrentSeed { get; private set; }

    /// <summary>Where raiders materialize — the warp gate's feet, on the lane.</summary>
    public static Vector3 PortalSite { get; private set; }

    /// <summary>Where the Photon Core stands — the raiders' one destination.</summary>
    public static Vector3 CoreSite { get; private set; }

    static float CellCenter(int i) => -HalfExtent + Cell * 0.5f + Cell * i;

    public static GameObject Build(Transform envRoot, int seed)
    {
        CurrentSeed = seed;

        var root = new GameObject("TowerDefenseCanyon");
        root.transform.SetParent(envRoot, false);
        var kit = new ArenaKit(root.transform, "TowerDefense");

        var rng = new System.Random(seed);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());
        int NextInt(int min, int maxInclusive) => min + rng.Next(maxInclusive - min + 1);

        var ground = ArenaMaterials.Style("TD_Ground", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.16f, 0.17f, 0.20f), new Color(0.10f, 0.11f, 0.14f), 3.5f, 0.95f);
        var rock = ArenaMaterials.Style("TD_Rock", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.13f, 0.13f, 0.16f), new Color(0.07f, 0.07f, 0.10f), 2.2f, 0.98f);
        var lane = ArenaMaterials.Style("TD_Lane", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.20f, 0.22f, 0.27f), new Color(0.11f, 0.12f, 0.15f), 1.4f, 0.6f);

        // Ground extends under the border cliffs so no seam shows at the edge.
        kit.Box("Ground", new Vector3(0f, GroundY - 0.5f, 0f), new Vector3(100f, 1f, 100f), ground);
        kit.Box("Cliff_W", new Vector3(-47f, 3f, 0f), new Vector3(4f, 6f, 98f), rock);
        kit.Box("Cliff_E", new Vector3(47f, 3f, 0f), new Vector3(4f, 6f, 98f), rock);
        kit.Box("Cliff_S", new Vector3(0f, 3f, -47f), new Vector3(98f, 6f, 4f), rock);
        kit.Box("Cliff_N", new Vector3(0f, 3f, 47f), new Vector3(98f, 6f, 4f), rock);

        // --- the seed decides the canyon ---
        // A serpentine: down from the gate, three cross-runs alternating
        // east/west, down again into the Core pocket. Rolled in one fixed
        // order — the reproducibility contract.
        int entryCol = NextInt(3, 14);
        int run1Row = NextInt(13, 14);
        int run2Row = NextInt(9, 10);
        int run3Row = NextInt(5, 6);
        bool firstRunEast = NextInt(0, 1) == 1;
        int run1Col = firstRunEast ? NextInt(13, 15) : NextInt(2, 4);
        int run2Col = firstRunEast ? NextInt(2, 4) : NextInt(13, 15);
        int coreCol = NextInt(7, 10);

        var isPath = new bool[Cells, Cells];
        void Carve(int col, int row) => isPath[col, row] = true;
        void CarveCol(int col, int rowFrom, int rowTo)
        {
            for (int r = Mathf.Max(rowFrom, rowTo); r >= Mathf.Min(rowFrom, rowTo); r--)
                Carve(col, r);
        }
        void CarveRow(int row, int colFrom, int colTo)
        {
            int step = colTo >= colFrom ? 1 : -1;
            for (int c = colFrom; c != colTo + step; c += step)
                Carve(c, row);
        }

        CarveCol(entryCol, 17, run1Row);
        CarveRow(run1Row, entryCol, run1Col);
        CarveCol(run1Col, run1Row, run2Row);
        CarveRow(run2Row, run1Col, run2Col);
        CarveCol(run2Col, run2Row, run3Row);
        CarveRow(run3Row, run2Col, coreCol);
        CarveCol(coreCol, run3Row, 2);
        // The Core pocket: a 3×3 room at the canyon's end.
        for (int c = coreCol - 1; c <= coreCol + 1; c++)
            for (int r = 0; r <= 2; r++)
                Carve(c, r);

        PortalSite = new Vector3(CellCenter(entryCol), GroundY, CellCenter(17));
        CoreSite = new Vector3(CellCenter(coreCol), GroundY, CellCenter(1));

        BuildPlateau(kit, rock, isPath);
        BuildLaneFloor(kit, lane, isPath);
        BuildRimTrim(kit, isPath);
        BuildPortal(root.transform, kit);
        BuildCorePad(kit);
        ScatterDressing(root.transform, kit, rock, isPath, Next);

        return root;
    }

    // ------------------------------------------------------------- terrain

    /// <summary>
    /// The tableland: every non-lane cell filled to rim height, merged into
    /// row-runs so 300 cells cost ~40 boxes.
    /// </summary>
    static void BuildPlateau(ArenaKit kit, Material rock, bool[,] isPath)
    {
        for (int row = 0; row < Cells; row++)
        {
            int runStart = -1;
            for (int col = 0; col <= Cells; col++)
            {
                bool plateau = col < Cells && !isPath[col, row];
                if (plateau && runStart < 0)
                    runStart = col;
                if (!plateau && runStart >= 0)
                {
                    float x0 = CellCenter(runStart) - Cell * 0.5f;
                    float x1 = CellCenter(col - 1) + Cell * 0.5f;
                    kit.Box("Plateau",
                        new Vector3((x0 + x1) * 0.5f, PlateauY * 0.5f, CellCenter(row)),
                        new Vector3(x1 - x0, PlateauY, Cell), rock);
                    runStart = -1;
                }
            }
        }
    }

    /// <summary>
    /// Tread-plate overlay on the lane so the raiders' route reads from any
    /// zoom. Merged per row — every lane cell appears in exactly one run, so
    /// no two overlays ever share a surface to z-fight on.
    /// </summary>
    static void BuildLaneFloor(ArenaKit kit, Material lane, bool[,] isPath)
    {
        for (int row = 0; row < Cells; row++)
        {
            int runStart = -1;
            for (int col = 0; col <= Cells; col++)
            {
                bool path = col < Cells && isPath[col, row];
                if (path && runStart < 0)
                    runStart = col;
                if (!path && runStart >= 0)
                {
                    float x0 = CellCenter(runStart) - Cell * 0.5f;
                    float x1 = CellCenter(col - 1) + Cell * 0.5f;
                    kit.Decor("LaneFloor",
                        new Vector3((x0 + x1) * 0.5f, GroundY + 0.03f, CellCenter(row)),
                        new Vector3(x1 - x0, 0.06f, Cell), lane);
                    runStart = -1;
                }
            }
        }
    }

    /// <summary>
    /// A thin teal running light along every rim edge that overlooks the
    /// lane — the canyon drawn in neon, one strip per lane/plateau border.
    /// Teal on purpose: the teams own cyan and magenta, amber is money.
    /// </summary>
    static void BuildRimTrim(ArenaKit kit, bool[,] isPath)
    {
        var trim = ArenaMaterials.Emissive("TD_RimTrim", new Color(0.25f, 0.85f, 0.75f), 1.5f);
        for (int col = 0; col < Cells; col++)
            for (int row = 0; row < Cells; row++)
            {
                if (!isPath[col, row])
                    continue;
                float x = CellCenter(col), z = CellCenter(row);
                float y = PlateauY + 0.02f;
                float half = Cell * 0.5f;
                if (col + 1 >= Cells || !isPath[col + 1, row])
                    kit.Decor("RimTrim", new Vector3(x + half + 0.12f, y, z),
                        new Vector3(0.22f, 0.1f, Cell), trim);
                if (col - 1 < 0 || !isPath[col - 1, row])
                    kit.Decor("RimTrim", new Vector3(x - half - 0.12f, y, z),
                        new Vector3(0.22f, 0.1f, Cell), trim);
                if (row + 1 >= Cells || !isPath[col, row + 1])
                    kit.Decor("RimTrim", new Vector3(x, y, z + half + 0.12f),
                        new Vector3(Cell, 0.1f, 0.22f), trim);
                if (row - 1 < 0 || !isPath[col, row - 1])
                    kit.Decor("RimTrim", new Vector3(x, y, z - half - 0.12f),
                        new Vector3(Cell, 0.1f, 0.22f), trim);
            }
    }

    // ------------------------------------------------------------- set pieces

    /// <summary>
    /// The warp gate: two pillars and a lintel over the lane mouth, glowing
    /// enemy-magenta, with a shimmer quad hung in the opening. Raiders
    /// materialize at its feet — the one place on the map that says THEY
    /// COME FROM HERE at every zoom.
    /// </summary>
    static void BuildPortal(Transform mapRoot, ArenaKit kit)
    {
        var magenta = new Color(1f, 0.3f, 0.9f);
        var pillar = ArenaMaterials.Style("TD_Portal", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.14f, 0.10f, 0.16f), new Color(0.08f, 0.06f, 0.10f), 1.2f, 0.6f);
        var glow = ArenaMaterials.Emissive("TD_PortalGlow", magenta, 1.7f);

        Vector3 site = new Vector3(PortalSite.x, GroundY, HalfExtent - 1.5f);
        kit.Box("PortalPillar_W", site + new Vector3(-3.1f, 2.75f, 0f), new Vector3(1.2f, 5.5f, 1.2f), pillar);
        kit.Box("PortalPillar_E", site + new Vector3(3.1f, 2.75f, 0f), new Vector3(1.2f, 5.5f, 1.2f), pillar);
        kit.Box("PortalLintel", site + new Vector3(0f, 5.8f, 0f), new Vector3(7.4f, 1f, 1.2f), pillar);
        kit.Decor("PortalTrim_W", site + new Vector3(-3.1f, 2.75f, 0.65f), new Vector3(0.3f, 5f, 0.1f), glow);
        kit.Decor("PortalTrim_E", site + new Vector3(3.1f, 2.75f, 0.65f), new Vector3(0.3f, 5f, 0.1f), glow);
        kit.Point(site + Vector3.up * 3f, magenta, 3f, 16f);

        // The shimmer: a vertical additive quad filling the arch. Built by
        // hand — GlowQuad lies flat on purpose, and this one must stand up.
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PortalShimmer";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(mapRoot, false);
        quad.transform.position = site + new Vector3(0f, 2.7f, 0f);
        quad.transform.localScale = new Vector3(5.6f, 4.9f, 1f);
        var mat = new Material(Shader.Find("PhotonArena/Additive"));
        mat.SetTexture("_MainTex", Resources.Load<Texture2D>("VFX/glow"));
        mat.SetColor("_Color", magenta);
        mat.SetFloat("_Intensity", 0.8f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>The Core's plinth: a cyan-trimmed pad in the pocket, home-team bright.</summary>
    static void BuildCorePad(ArenaKit kit)
    {
        var cyan = new Color(0.2f, 0.9f, 1f);
        var pad = ArenaMaterials.Style("TD_CorePad", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.22f, 0.24f, 0.28f), new Color(0.12f, 0.13f, 0.16f), 1.2f, 0.5f);
        var trim = ArenaMaterials.Emissive("TD_CoreTrim", cyan, 1.6f);

        kit.Platform("CorePad", new Vector2(CoreSite.x, CoreSite.z), new Vector2(10f, 10f),
            GroundY + 0.1f, pad);
        float y = GroundY + 0.14f;
        kit.Decor("CoreTrim_N", CoreSite + new Vector3(0f, y, 5.2f), new Vector3(10.6f, 0.1f, 0.35f), trim);
        kit.Decor("CoreTrim_S", CoreSite + new Vector3(0f, y, -5.2f), new Vector3(10.6f, 0.1f, 0.35f), trim);
        kit.Decor("CoreTrim_E", CoreSite + new Vector3(5.2f, y, 0f), new Vector3(0.35f, 0.1f, 10.6f), trim);
        kit.Decor("CoreTrim_W", CoreSite + new Vector3(-5.2f, y, 0f), new Vector3(0.35f, 0.1f, 10.6f), trim);
        kit.Point(CoreSite + Vector3.up * 6f, cyan, 2.5f, 18f);
    }

    // ------------------------------------------------------------- dressing

    /// <summary>
    /// Life on the tableland: rocks (with rubble-ring clusters), teal vents,
    /// and a few amber crystal outcrops. Real geometry only — flat decals
    /// read as artifacts on this renderer. No mirroring: a defense map has
    /// no fairness to keep, so the scatter is free-form.
    /// </summary>
    static void ScatterDressing(Transform mapRoot, ArenaKit kit, Material rock,
        bool[,] isPath, System.Func<float, float, float> next)
    {
        bool NearSomething(Vector3 pos)
        {
            Vector3 gate = new Vector3(PortalSite.x, 0f, HalfExtent - 1.5f);
            return (pos - gate).sqrMagnitude < 8f * 8f
                || (pos - CoreSite).sqrMagnitude < 10f * 10f;
        }

        bool OnPlateau(Vector3 pos)
        {
            int col = Mathf.FloorToInt((pos.x + HalfExtent) / Cell);
            int row = Mathf.FloorToInt((pos.z + HalfExtent) / Cell);
            return col >= 0 && col < Cells && row >= 0 && row < Cells && !isPath[col, row];
        }

        // Rocks stand on the rim, never in the lane — the lane is the one
        // piece of ground whose emptiness is a promise.
        int rocks = (int)next(12f, 18f);
        int placed = 0, attempts = 0;
        while (placed < rocks && attempts++ < 200)
        {
            var pos = new Vector3(next(-40f, 40f), 0f, next(-40f, 40f));
            if (!OnPlateau(pos) || NearSomething(pos))
                continue;
            float size = next(1.4f, 2.8f);
            var scale = new Vector3(size, size * 0.7f, size * next(0.7f, 1.1f));
            kit.Box("Rock", new Vector3(pos.x, PlateauY + size * 0.3f, pos.z), scale, rock,
                next(0f, 360f));
            if (next(0f, 1f) < 0.35f)
            {
                int pieces = (int)next(2f, 4f);
                for (int p = 0; p < pieces; p++)
                {
                    float angle = next(0f, Mathf.PI * 2f);
                    var piecePos = pos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                        * (size * next(0.9f, 1.5f));
                    if (!OnPlateau(piecePos))
                        continue;
                    float pieceSize = next(0.4f, 0.9f);
                    kit.Box("Rubble", new Vector3(piecePos.x, PlateauY + pieceSize * 0.3f, piecePos.z),
                        Vector3.one * pieceSize, rock, next(0f, 360f));
                }
            }
            placed++;
        }

        // Teal vents — the fiction's signature signs of life.
        var teal = new Color(0.3f, 1f, 0.8f);
        var vent = ArenaMaterials.Style("TD_Vent", ArenaMaterials.SurfaceStyle.Crystal,
            new Color(0.08f, 0.22f, 0.18f), new Color(0.04f, 0.12f, 0.10f), 0.6f, 0.4f,
            teal, 1.5f);
        int vents = (int)next(6f, 10f);
        placed = 0; attempts = 0;
        while (placed < vents && attempts++ < 120)
        {
            var pos = new Vector3(next(-40f, 40f), 0f, next(-40f, 40f));
            if (!OnPlateau(pos) || NearSomething(pos))
                continue;
            int shards = (int)next(2f, 4f);
            for (int s = 0; s < shards; s++)
            {
                var basePoint = pos + new Vector3(next(-1f, 1f), PlateauY, next(-1f, 1f));
                var tip = basePoint + new Vector3(next(-0.3f, 0.3f), next(0.5f, 1f), next(-0.3f, 0.3f));
                kit.Beam("Vent", basePoint - Vector3.up * 0.2f, tip, next(0.25f, 0.4f), vent,
                    collide: false);
            }
            placed++;
        }

        // Amber crystal outcrops: treasure-coloured landmarks (purely
        // scenery — no CrystalField component, nothing here harvests).
        var amber = new Color(1f, 0.65f, 0.2f);
        var crystal = ArenaMaterials.Style("TD_Crystal", ArenaMaterials.SurfaceStyle.Crystal,
            new Color(0.45f, 0.30f, 0.10f), new Color(0.25f, 0.15f, 0.05f), 0.8f, 0.35f,
            amber, 1.5f);
        int outcrops = (int)next(3f, 5f);
        placed = 0; attempts = 0;
        while (placed < outcrops && attempts++ < 80)
        {
            var pos = new Vector3(next(-38f, 38f), 0f, next(-38f, 38f));
            if (!OnPlateau(pos) || NearSomething(pos))
                continue;
            int shardCount = (int)next(4f, 7f);
            for (int s = 0; s < shardCount; s++)
            {
                float angle = next(0f, Mathf.PI * 2f);
                float dist = next(0f, 2.2f);
                var basePoint = pos + new Vector3(Mathf.Cos(angle) * dist, PlateauY,
                    Mathf.Sin(angle) * dist);
                float lean = next(0f, 0.4f);
                float leanDir = next(0f, Mathf.PI * 2f);
                float height = next(0.9f, 2f);
                var tip = basePoint + new Vector3(Mathf.Cos(leanDir) * lean * height, height,
                    Mathf.Sin(leanDir) * lean * height);
                kit.Beam("Crystal", basePoint - Vector3.up * 0.3f, tip, next(0.4f, 0.7f), crystal);
            }
            kit.Point(new Vector3(pos.x, PlateauY + 2f, pos.z), amber, 1.6f, 9f);
            placed++;
        }
    }
}

using UnityEngine;

/// <summary>
/// Builds the Commander battlefield: a 180 m map with a base site at each end,
/// ridge lines that force army traffic through chokepoints, and eight photon
/// crystal fields for the economy to fight over.
///
/// Runtime-generated like the arenas, and with the same tools (ArenaKit +
/// ArenaMaterials), but deliberately NOT an ArenaDefinition: arenas are 32 m
/// combat rooms sized for one firefight, and everything an ArenaDefinition
/// promises — cover churn bounds, treasure drop planes, spawn keep-outs — is
/// FPS-match vocabulary that means nothing here.
///
/// The layout is fixed, not randomized per match: the same map every time is
/// what lets a strategy be practiced, and (like the arenas' fixed cover seed)
/// what lets a layout problem found once be looked at again. It is mirrored by
/// 180° rotation so neither base gets the better half.
/// </summary>
public static class CommanderMap
{
    /// <summary>Half-width of the battlefield. The playfield is a square.</summary>
    public const float HalfExtent = 90f;

    public const float GroundY = 0f;

    /// <summary>Where each team's base pad sits: cyan south, magenta north.</summary>
    public static Vector3 BaseSite(int teamId) =>
        new Vector3(0f, GroundY, teamId == 0 ? -70f : 70f);

    /// <summary>
    /// Crystal field centres. The first four are the "safe" pair behind each
    /// base's ridge line; the last four sit in the contested middle, which is
    /// the point of them. Phase 2 hangs harvestable components off these; until
    /// then the layout is already decided so the geometry never shifts under a
    /// later phase.
    /// </summary>
    public static readonly Vector2[] CrystalFields =
    {
        new Vector2(-42f, -58f), new Vector2(42f, -58f),   // cyan's own
        new Vector2(42f, 58f), new Vector2(-42f, 58f),     // magenta's own
        new Vector2(-72f, 0f), new Vector2(72f, 0f),       // contested flanks
        new Vector2(-16f, 14f), new Vector2(16f, -14f),    // contested centre
    };

    /// <summary>Ridge lines cross the map at ±this z, gapped by chokepoints.</summary>
    const float RidgeZ = 28f;

    /// <summary>
    /// Everything past the world's edge — the camera clear colour, the fog it
    /// fades into, and the skirt below — is this one dark blue, so overshoot
    /// reads as night beyond the battlefield instead of three mismatched voids.
    /// </summary>
    public static readonly Color VoidColor = new Color(0.05f, 0.07f, 0.12f);

    public static GameObject Build(Transform envRoot)
    {
        var root = new GameObject("CommanderBattlefield");
        root.transform.SetParent(envRoot, false);
        var kit = new ArenaKit(root.transform, "Commander");

        // Fixed seed for the rock jitter — same reasoning as ArenaRuntime's
        // cover seed.
        var rng = new System.Random(9021);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        var ground = ArenaMaterials.Style("Cmd_Ground", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.16f, 0.17f, 0.20f), new Color(0.10f, 0.11f, 0.14f), 3.5f, 0.95f);
        var rock = ArenaMaterials.Style("Cmd_Ridge", ArenaMaterials.SurfaceStyle.Stone,
            new Color(0.13f, 0.13f, 0.16f), new Color(0.07f, 0.07f, 0.10f), 2.2f, 0.98f);

        // Ground extends under the border cliffs so no seam shows at the edge.
        kit.Box("Ground", new Vector3(0f, GroundY - 0.5f, 0f), new Vector3(184f, 1f, 184f), ground);

        // Border cliffs. Tall enough that the camera at max zoom still reads
        // them as the end of the world, not as cover.
        kit.Box("Cliff_W", new Vector3(-92f, 3f, 0f), new Vector3(4f, 6f, 188f), rock);
        kit.Box("Cliff_E", new Vector3(92f, 3f, 0f), new Vector3(4f, 6f, 188f), rock);
        kit.Box("Cliff_S", new Vector3(0f, 3f, -92f), new Vector3(188f, 6f, 4f), rock);
        kit.Box("Cliff_N", new Vector3(0f, 3f, 92f), new Vector3(188f, 6f, 4f), rock);

        BuildRidges(kit, rock, Next);
        BuildCrystalFields(root.transform, kit, Next);
        BuildBasePads(kit);
        ScatterRocks(kit, rock, Next);

        return root;
    }

    /// <summary>
    /// Two ridge lines, each broken by a west, a centre and an east chokepoint.
    /// A ridge is a chain of jittered rock blocks rather than one wall so it
    /// reads as terrain, but every block is tall enough (3.5 m+) that nothing
    /// paths over it — the gaps are the only ways through.
    ///
    /// Only the south ridge is generated; every block is emitted twice, as
    /// drawn and rotated 180° through the origin. Two ridges drawing from one
    /// RNG stream get different jitter, and with a fixed seed a chokepoint a
    /// couple of metres narrower than its mirror is a permanent bias, not a
    /// coin flip — the header's fairness promise has to hold at block level.
    /// </summary>
    static void BuildRidges(ArenaKit kit, Material rock, System.Func<float, float, float> next)
    {
        // Block-CENTRE ranges. The nominal gaps are (-52,-36), (-10,10),
        // (36,52); chokepoint-facing ends are inset 5.2 m — the worst-case
        // half-footprint of a yawed block (3.75·cos14° + 3·sin14° ≈ 4.36) plus
        // its 0.8 x-jitter — so no block bites into a 16 m gap. The ±90
        // map-edge ends are NOT inset: those blocks must keep overlapping the
        // border cliffs, or a walkable slit opens between ridge and cliff.
        var segments = new[]
        {
            new Vector2(-90f, -57.2f), new Vector2(-30.8f, -15.2f),
            new Vector2(15.2f, 30.8f), new Vector2(57.2f, 90f),
        };

        foreach (var seg in segments)
        {
            // Endpoint-inclusive spacing: the chain must reach the inset line
            // exactly, not stop a stride short and widen the gap it guards.
            float length = seg.y - seg.x;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / 5f)) + 1;
            float step = length / (count - 1);

            for (int i = 0; i < count; i++)
            {
                // Height drawn first, centre-y derived from it: every block
                // sits 0.3–1.0 m INTO the ground. Independent draws let a
                // third of the blocks float with daylight underneath, which
                // the 55° camera stares straight at.
                float height = next(3.5f, 5.5f);
                var pos = new Vector3(
                    seg.x + step * i + next(-0.8f, 0.8f),
                    height * 0.5f - next(0.3f, 1.0f),
                    -RidgeZ + next(-1.2f, 1.2f));
                var size = new Vector3(next(5.5f, 7.5f), height, next(4f, 6f));
                float yaw = next(-14f, 14f);

                // A box's footprint is invariant under yaw+180°, so the
                // rotated twin reuses the same yaw.
                kit.Box("Ridge", pos, size, rock, yaw);
                kit.Box("Ridge", new Vector3(-pos.x, pos.y, -pos.z), size, rock, yaw);
            }
        }
    }

    /// <summary>
    /// A crystal field is a dozen tilted amber shards. Amber on purpose: the
    /// teams own cyan and magenta, so the thing they fight over is neither.
    /// Shards keep their colliders — units pathing around a field they are not
    /// harvesting is correct, and the NavMesh bake handles it.
    ///
    /// Each field gets its own object carrying a CrystalField component, with
    /// the shards reparented under it — that is what collectors harvest, and
    /// what hides shards one by one as the field drains.
    /// </summary>
    static void BuildCrystalFields(Transform mapRoot, ArenaKit kit,
        System.Func<float, float, float> next)
    {
        var amber = new Color(1f, 0.65f, 0.2f);
        // Emission stays under 1.8 — the crystals are matter, not an energy
        // weapon, and hotter than that bloom washes them white.
        var crystal = ArenaMaterials.Style("Cmd_Crystal", ArenaMaterials.SurfaceStyle.Crystal,
            new Color(0.45f, 0.30f, 0.10f), new Color(0.25f, 0.15f, 0.05f), 0.8f, 0.35f,
            amber, 1.5f);

        foreach (var field in CrystalFields)
        {
            var fieldGo = new GameObject("CrystalField");
            fieldGo.transform.SetParent(mapRoot, false);
            fieldGo.transform.localPosition = new Vector3(field.x, GroundY, field.y);

            var shards = new System.Collections.Generic.List<Transform>(12);
            for (int i = 0; i < 12; i++)
            {
                float angle = next(0f, Mathf.PI * 2f);
                float dist = next(0f, 4.5f);
                var basePoint = new Vector3(field.x + Mathf.Cos(angle) * dist, GroundY,
                                            field.y + Mathf.Sin(angle) * dist);

                // Tip leans up to ~25° off vertical, so the cluster splays like
                // a geode rather than standing like a picket fence.
                float lean = next(0f, 0.45f);
                float leanDir = next(0f, Mathf.PI * 2f);
                float height = next(1.2f, 2.6f);
                var tip = basePoint + new Vector3(Mathf.Cos(leanDir) * lean * height, height,
                                                  Mathf.Sin(leanDir) * lean * height);

                var shard = kit.Beam("Crystal", basePoint - Vector3.up * 0.3f, tip,
                    next(0.5f, 0.9f), crystal);
                if (shard != null)
                {
                    shard.transform.SetParent(fieldGo.transform, true);
                    shards.Add(shard.transform);
                }
            }

            // One dim light per field so the amber reads on the ground around
            // it, which is what the camera mostly sees from 45 m up.
            kit.Point(new Vector3(field.x, 2.5f, field.y), amber, 1.8f, 10f);

            fieldGo.AddComponent<CrystalField>().Init(shards, 3000f);
        }
    }

    /// <summary>
    /// Each team's base site: a tread-plate pad with team-colour trim. Phase 3
    /// puts the Command Center here; until then the pad itself marks whose end
    /// is whose from any zoom.
    /// </summary>
    static void BuildBasePads(ArenaKit kit)
    {
        var pad = ArenaMaterials.Style("Cmd_Pad", ArenaMaterials.SurfaceStyle.Tread,
            new Color(0.22f, 0.24f, 0.28f), new Color(0.12f, 0.13f, 0.16f), 1.2f, 0.5f);

        for (int team = 0; team < 2; team++)
        {
            Vector3 site = BaseSite(team);
            Color tint = MatchAnnouncer.TeamColor(team);
            var trim = ArenaMaterials.Emissive($"Cmd_Trim{team}", tint, 1.6f);

            // 12 cm rise — a step, not a wall, to a 0.4 step-offset agent.
            kit.Platform($"BasePad{team}", new Vector2(site.x, site.z), new Vector2(16f, 16f),
                GroundY + 0.12f, pad);

            // Trim strips on all four edges, sitting just proud of the pad.
            float y = GroundY + 0.16f;
            kit.Decor($"PadTrim{team}_N", site + new Vector3(0f, y, 8.2f), new Vector3(16.8f, 0.1f, 0.4f), trim);
            kit.Decor($"PadTrim{team}_S", site + new Vector3(0f, y, -8.2f), new Vector3(16.8f, 0.1f, 0.4f), trim);
            kit.Decor($"PadTrim{team}_E", site + new Vector3(8.2f, y, 0f), new Vector3(0.4f, 0.1f, 16.8f), trim);
            kit.Decor($"PadTrim{team}_W", site + new Vector3(-8.2f, y, 0f), new Vector3(0.4f, 0.1f, 16.8f), trim);

            // Team light: makes the pad's end of the map read cyan or magenta
            // even when the trim itself is subpixel at max zoom.
            kit.Point(site + Vector3.up * 6f, tint, 2.5f, 18f);
        }
    }

    /// <summary>
    /// Loose rocks for visual texture, placed in 180°-rotated pairs like the
    /// ridges. Kept clear of the base pads, the crystal fields and the ridge
    /// lines so they never plug a chokepoint or squat on ground Phase 3 wants
    /// buildable — and since all of those keep-outs are themselves symmetric,
    /// one check clears both twins.
    /// </summary>
    static void ScatterRocks(ArenaKit kit, Material rock, System.Func<float, float, float> next)
    {
        int placed = 0, attempts = 0;
        while (placed < 7 && attempts++ < 200)
        {
            var pos = new Vector3(next(-80f, 80f), 0f, next(-80f, 80f));

            if (Vector3.Distance(pos, BaseSite(0)) < 22f) continue;
            if (Vector3.Distance(pos, BaseSite(1)) < 22f) continue;
            if (Mathf.Abs(Mathf.Abs(pos.z) - RidgeZ) < 7f) continue;
            bool nearField = false;
            foreach (var field in CrystalFields)
                if (Vector2.Distance(new Vector2(pos.x, pos.z), field) < 9f) { nearField = true; break; }
            if (nearField) continue;

            float size = next(1.6f, 3.4f);
            var scale = new Vector3(size, size * 0.7f, size * next(0.7f, 1.1f));
            float yaw = next(0f, 360f);
            kit.Box("Rock", new Vector3(pos.x, size * 0.35f, pos.z), scale, rock, yaw);
            kit.Box("Rock", new Vector3(-pos.x, size * 0.35f, -pos.z), scale, rock, yaw);
            placed++;
        }
    }

    /// <summary>
    /// A vast dark apron under the world's edge. Called by the controller
    /// AFTER the NavMesh bake, and must be: a 600 m surface 5 cm under the
    /// ground plane would otherwise bake into a huge (unreachable, but real)
    /// walkable island outside the cliffs. With it in place, the frustum
    /// overshooting the border at high zoom sees fogged wasteland fading to
    /// <see cref="VoidColor"/> instead of raw skybox.
    /// </summary>
    public static void BuildSkirt(Transform mapRoot)
    {
        var kit = new ArenaKit(mapRoot, "Commander");
        kit.Decor("Skirt", new Vector3(0f, GroundY - 0.55f, 0f), new Vector3(600f, 1f, 600f),
            ArenaMaterials.Lit("Cmd_Skirt", new Color(0.04f, 0.05f, 0.08f), 0.05f));
    }

    /// <summary>
    /// Battlefield atmosphere: a whisper of fog for depth over 180 m sightlines
    /// and the scene's flat ambient. ArenaRuntime.Load reapplies the arena's
    /// own atmosphere on the way out, so nothing here needs undoing by hand.
    /// </summary>
    public static void ApplyAtmosphere()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.42f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = VoidColor;
        // e^-(d·0.004)² ≈ 0.94 at typical camera range — a depth cue, not weather.
        RenderSettings.fogDensity = 0.004f;
    }
}

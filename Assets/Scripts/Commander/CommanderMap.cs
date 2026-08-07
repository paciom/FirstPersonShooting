using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the Commander battlefield: a 180 m map with a base site at each
/// end, ridge lines that force army traffic through chokepoints, ten photon
/// crystal fields for the economy to fight over, and a battlefield's worth
/// of dressing — craters, ruins, rocks, vents — so the ground between the
/// bases has stories on it.
///
/// Runtime-generated like the arenas, and with the same tools (ArenaKit +
/// ArenaMaterials), but deliberately NOT an ArenaDefinition: arenas are 32 m
/// combat rooms sized for one firefight, and everything an ArenaDefinition
/// promises is FPS-match vocabulary that means nothing here.
///
/// Every match rolls a different map from one MAP CODE (the seed): every
/// draw comes from a single System.Random in a fixed order, so the same
/// code always rebuilds the same battlefield — type a code you liked into
/// the menu's MAP CODE box and you are back on it. Layout stays mirrored by
/// 180° rotation so neither base gets the better half, and the bases
/// themselves never move: (0, ±70) is load-bearing for spawns, AI rallies
/// and the camera's opening shot.
/// </summary>
public static class CommanderMap
{
    /// <summary>Half-width of the battlefield. The playfield is a square.</summary>
    public const float HalfExtent = 90f;

    public const float GroundY = 0f;

    /// <summary>The map code the current battlefield was rolled from.</summary>
    public static int CurrentSeed { get; private set; }

    /// <summary>Where each team's base pad sits: cyan south, magenta north.</summary>
    public static Vector3 BaseSite(int teamId) =>
        new Vector3(0f, GroundY, teamId == 0 ? -70f : 70f);

    /// <summary>
    /// This battlefield's crystal field centres, rolled at build time. Index
    /// order: starter pair (beside each base), safe pair per base, contested
    /// flanks, contested centre. The minimap reads these for its amber dots.
    /// </summary>
    public static Vector2[] CrystalFields { get; private set; } = new Vector2[0];

    /// <summary>
    /// Everything past the world's edge — the camera clear colour, the fog it
    /// fades into, and the skirt below — is this one dark blue, so overshoot
    /// reads as night beyond the battlefield instead of three mismatched voids.
    /// </summary>
    public static readonly Color VoidColor = new Color(0.05f, 0.07f, 0.12f);

    // Rolled per build; consumed by the keep-out checks the dressing uses.
    static float _ridgeZ;
    static float[] _gapCenters = new float[0];

    public static GameObject Build(Transform envRoot, int seed)
    {
        CurrentSeed = seed;

        var root = new GameObject("CommanderBattlefield");
        root.transform.SetParent(envRoot, false);
        var kit = new ArenaKit(root.transform, "Commander");

        var rng = new System.Random(seed);
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

        // --- the seed decides the terrain ---
        _ridgeZ = Next(24f, 33f);
        RollGaps(Next);
        RollCrystalFields(Next);

        BuildRidges(kit, rock, Next);
        BuildCrystalFields(root.transform, kit, Next);
        BuildBasePads(kit);
        BuildRuins(kit, Next);
        BuildVents(kit, Next);
        BuildWrecks(root.transform, Next);
        ScatterRocks(kit, rock, Next);

        return root;
    }

    // ------------------------------------------------------------- layout rolls

    /// <summary>
    /// Three chokepoints per ridge line: a west, a centre-ish and an east
    /// gap, each rolled inside its own band so they can wander without ever
    /// merging into one super-gap or crowding a flank shut.
    /// </summary>
    static void RollGaps(System.Func<float, float, float> next)
    {
        _gapCenters = new[]
        {
            next(-58f, -34f),
            next(-10f, 10f),
            next(34f, 58f),
        };
    }

    /// <summary>Half-width of each chokepoint opening, per gap.</summary>
    static float[] _gapHalfWidths = new float[0];

    /// <summary>
    /// Ten fields in five mirrored pairs. The STARTER pair sits right beside
    /// each base — the fast first mine that gets the economy moving inside
    /// the opening minute — and is deliberately the smallest, so depleting
    /// it pushes the war outward to the contested ground, Red Alert style.
    /// </summary>
    static void RollCrystalFields(System.Func<float, float, float> next)
    {
        var fields = new List<Vector2>();

        // Starter: flanking the pad, a collector's stone's-throw from home.
        float starterX = next(12f, 16f) * (next(0f, 1f) > 0.5f ? 1f : -1f);
        float starterZ = -70f + next(2f, 8f);
        AddPair(fields, new Vector2(starterX, starterZ));

        // Safe: behind the ridge line, off to a flank.
        float safeX = next(30f, 52f) * (next(0f, 1f) > 0.5f ? 1f : -1f);
        float safeZ = next(-62f, -46f);
        AddPair(fields, new Vector2(safeX, safeZ));

        // A second safe-ish pair, wider and nearer the ridge.
        float midX = next(36f, 66f) * (next(0f, 1f) > 0.5f ? 1f : -1f);
        float midZ = next(-46f, -(_ridgeZ + 9f));
        AddPair(fields, new Vector2(midX, midZ));

        // Contested flank: out wide at midfield.
        float flankX = next(58f, 78f) * (next(0f, 1f) > 0.5f ? 1f : -1f);
        float flankZ = next(-12f, 12f);
        AddPair(fields, new Vector2(flankX, flankZ));

        // Contested centre: inside the ridge lines, near the middle.
        float angle = next(0f, Mathf.PI * 2f);
        float radius = next(8f, 24f);
        var centre = new Vector2(Mathf.Cos(angle) * radius,
            Mathf.Clamp(Mathf.Sin(angle) * radius, -(_ridgeZ - 10f), _ridgeZ - 10f));
        AddPair(fields, centre);

        // A second helping on each side of the ridge — seven pairs total,
        // because two commanders, their collectors AND their moonlighting
        // armies eat through five pairs before the war gets interesting.
        float outerX = next(18f, 42f) * (next(0f, 1f) > 0.5f ? 1f : -1f);
        float outerZ = next(-66f, -50f);
        AddPair(fields, new Vector2(outerX, outerZ));

        float ringAngle = next(0f, Mathf.PI * 2f);
        float ringRadius = next(18f, 34f);
        var ring = new Vector2(Mathf.Cos(ringAngle) * ringRadius,
            Mathf.Clamp(Mathf.Sin(ringAngle) * ringRadius, -(_ridgeZ - 10f), _ridgeZ - 10f));
        AddPair(fields, ring);

        // Nudge any pair that landed on another apart. Deterministic: fixed
        // iteration order, pure function of positions already rolled.
        for (int i = 2; i < fields.Count; i += 2)
            for (int j = 0; j < i; j += 2)
                if (Vector2.Distance(fields[i], fields[j]) < 16f)
                {
                    Vector2 push = (fields[i] - fields[j]).normalized * 16f;
                    if (push == Vector2.zero) push = new Vector2(16f, 0f);
                    fields[i] = fields[j] + push;
                    fields[i + 1] = -fields[i];
                }

        // A nudge can shove a field into the ridge band, where its crystal
        // would grow through rock a collector can't reach (1-in-8 maps, per
        // simulation). Snap those just clear, on whichever side they were.
        for (int i = 2; i < fields.Count; i += 2)
        {
            float band = Mathf.Abs(Mathf.Abs(fields[i].y) - _ridgeZ);
            if (band < 9f)
            {
                float sign = Mathf.Sign(fields[i].y);
                float absZ = Mathf.Abs(fields[i].y) > _ridgeZ ? _ridgeZ + 9f : _ridgeZ - 9f;
                fields[i] = new Vector2(fields[i].x, sign * absZ);
                fields[i + 1] = -fields[i];
            }
        }

        CrystalFields = fields.ToArray();
    }

    /// <summary>A field and its 180°-rotated twin, so the roll stays fair.</summary>
    static void AddPair(List<Vector2> fields, Vector2 southSide)
    {
        fields.Add(southSide);
        fields.Add(-southSide);
    }

    /// <summary>Capacity by pair: starter small, contested rich.</summary>
    static float FieldCapacity(int index)
    {
        int pair = index / 2;
        switch (pair)
        {
            case 0: return 1500f;    // starter — spends fast, pushes you out
            case 4: return 4000f;    // centre — worth the fight
            case 6: return 4000f;    // mid-ring — also worth the fight
            default: return 3000f;
        }
    }

    /// <summary>
    /// A small crystal field conjured mid-match — what a prospector's dig
    /// uncovers under a boulder. Same shard recipe as the rolled fields,
    /// unseeded: the MAP is reproducible from its code, but what happens
    /// during a war belongs to the war.
    /// </summary>
    public static void SpawnFieldAt(Transform mapRoot, Vector3 at, float capacity, int shardCount)
    {
        if (mapRoot == null)
            return;
        var kit = new ArenaKit(mapRoot, "Commander");
        var amber = new Color(1f, 0.65f, 0.2f);
        var crystal = ArenaMaterials.Style("Cmd_Crystal", ArenaMaterials.SurfaceStyle.Crystal,
            new Color(0.45f, 0.30f, 0.10f), new Color(0.25f, 0.15f, 0.05f), 0.8f, 0.35f,
            amber, 1.5f);

        var fieldGo = new GameObject("CrystalField");
        fieldGo.transform.SetParent(mapRoot, false);
        fieldGo.transform.position = new Vector3(at.x, GroundY, at.z);

        var shards = new List<Transform>(shardCount);
        for (int i = 0; i < shardCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(0f, 2.6f);
            var basePoint = new Vector3(at.x + Mathf.Cos(angle) * dist, GroundY,
                                        at.z + Mathf.Sin(angle) * dist);
            float lean = Random.Range(0f, 0.45f);
            float leanDir = Random.Range(0f, Mathf.PI * 2f);
            float height = Random.Range(1.0f, 2.2f);
            var tip = basePoint + new Vector3(Mathf.Cos(leanDir) * lean * height, height,
                                              Mathf.Sin(leanDir) * lean * height);
            var shard = kit.Beam("Crystal", basePoint - Vector3.up * 0.3f, tip,
                Random.Range(0.45f, 0.8f), crystal);
            if (shard != null)
            {
                shard.transform.SetParent(fieldGo.transform, true);
                shards.Add(shard.transform);
            }
        }

        kit.Point(new Vector3(at.x, 2.2f, at.z), amber, 1.6f, 8f);
        fieldGo.AddComponent<CrystalField>().Init(shards, capacity);
    }

    // ------------------------------------------------------------- terrain

    /// <summary>
    /// Two ridge lines at ±ridgeZ, broken by this map's rolled chokepoints.
    /// Only the south ridge is generated; every block is emitted twice, as
    /// drawn and rotated 180° through the origin — one RNG stream for two
    /// ridges would give the mirror different jitter, and with a fixed seed
    /// a chokepoint narrower than its twin is a permanent bias.
    /// </summary>
    static void BuildRidges(ArenaKit kit, Material rock, System.Func<float, float, float> next)
    {
        // Gap half-widths rolled here (RNG order is the reproducibility
        // contract — everything draws in one fixed sequence).
        _gapHalfWidths = new float[_gapCenters.Length];
        for (int i = 0; i < _gapCenters.Length; i++)
            _gapHalfWidths[i] = next(7.5f, 10f);

        // Block-CENTRE segment ranges between the gaps. Chokepoint-facing
        // ends are inset 5.2 m — the worst-case half-footprint of a yawed
        // block plus its jitter — so no block bites into an opening. The ±90
        // map-edge ends are NOT inset: those blocks must keep overlapping
        // the border cliffs, or a walkable slit opens between ridge and cliff.
        var edges = new List<Vector2>();
        float cursor = -90f;
        for (int i = 0; i < _gapCenters.Length; i++)
        {
            float left = _gapCenters[i] - _gapHalfWidths[i] - 5.2f;
            if (left - cursor > 4f)
                edges.Add(new Vector2(cursor, left));
            cursor = Mathf.Max(cursor, _gapCenters[i] + _gapHalfWidths[i] + 5.2f);
        }
        if (90f - cursor > 4f)
            edges.Add(new Vector2(cursor, 90f));

        foreach (var seg in edges)
        {
            // Endpoint-inclusive spacing: the chain must reach the inset line
            // exactly, not stop a stride short and widen the gap it guards.
            float length = seg.y - seg.x;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / 5f)) + 1;
            float step = length / (count - 1);

            for (int i = 0; i < count; i++)
            {
                // Height first, centre-y derived from it: every block sits
                // 0.3–1.0 m INTO the ground, never floating on daylight.
                float height = next(3.5f, 5.5f);
                var pos = new Vector3(
                    seg.x + step * i + next(-0.8f, 0.8f),
                    height * 0.5f - next(0.3f, 1.0f),
                    -_ridgeZ + next(-1.2f, 1.2f));
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
    /// Shards keep their colliders — units pathing around a field they are
    /// not harvesting is correct, and the NavMesh bake handles it.
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

        for (int index = 0; index < CrystalFields.Length; index++)
        {
            var field = CrystalFields[index];
            var fieldGo = new GameObject("CrystalField");
            fieldGo.transform.SetParent(mapRoot, false);
            fieldGo.transform.localPosition = new Vector3(field.x, GroundY, field.y);

            // Starter fields are compact — a claim, not a province.
            float spread = index < 2 ? 3.2f : 4.5f;
            int shardCount = index < 2 ? 9 : 12;

            var shards = new List<Transform>(shardCount);
            for (int i = 0; i < shardCount; i++)
            {
                float angle = next(0f, Mathf.PI * 2f);
                float dist = next(0f, spread);
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

            fieldGo.AddComponent<CrystalField>().Init(shards, FieldCapacity(index));
        }
    }

    /// <summary>
    /// Each team's base site: a tread-plate pad with team-colour trim. The
    /// Command Center stands here; the pad marks whose end is whose from any
    /// zoom.
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

    // ------------------------------------------------------------- dressing

    /// <summary>
    /// Dead war machines — the nine Meshy vehicle models, painted the colour
    /// of ash, sunk to the axles and left where they died. The richest props
    /// on the field, and they were already paid for. Each gets a box collider
    /// sized to its hull, so wrecks are hard cover the bake routes around.
    /// </summary>
    static void BuildWrecks(Transform mapRoot, System.Func<float, float, float> next)
    {
        var roster = Object.FindFirstObjectByType<RobotRoster>();
        if (roster == null || !roster.HasRobots)
            return;
        var prefabs = new List<GameObject>();
        foreach (var entry in roster.robots)
            if (entry.vehiclePrefab != null)
                prefabs.Add(entry.vehiclePrefab);
        if (prefabs.Count == 0)
            return;

        var ash = new Color(0.32f, 0.33f, 0.36f);
        int pairs = (int)next(3f, 6f);
        int placed = 0, attempts = 0;
        while (placed < pairs && attempts++ < 60)
        {
            var pos = new Vector3(next(-74f, 74f), 0f, next(-74f, 74f));
            if (Blocked(pos, baseKeepOut: 26f, fieldKeepOut: 11f, gapKeepOut: 9f))
                continue;

            var prefab = prefabs[Mathf.Min(prefabs.Count - 1, (int)next(0f, prefabs.Count))];
            float yaw = next(0f, 360f);
            float pitch = next(-7f, 7f);
            float roll = next(-9f, 9f);
            float length = next(3.2f, 4.6f);

            BuildWreck(mapRoot, prefab, pos, yaw, pitch, roll, length, ash);
            // The 180° twin: mirrored position, yaw spun half a turn.
            BuildWreck(mapRoot, prefab, -pos, yaw + 180f, pitch, roll, length, ash);
            placed++;
        }
    }

    static void BuildWreck(Transform mapRoot, GameObject prefab, Vector3 pos,
        float yaw, float pitch, float roll, float length, Color ash)
    {
        var holder = new GameObject("Wreck");
        holder.transform.SetParent(mapRoot, false);
        holder.transform.position = new Vector3(pos.x, GroundY, pos.z);

        var instance = Object.Instantiate(prefab, holder.transform);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

        float scale = length / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
        instance.transform.localScale *= scale;
        Vector3 centre = holder.transform.InverseTransformPoint(bounds.center);
        Vector3 bottom = holder.transform.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        // Sunk 0.25 below grade: a wreck sits IN the dirt, not on it.
        instance.transform.localPosition = new Vector3(
            -centre.x * scale, -bottom.y * scale - 0.25f, -centre.z * scale);

        // Dead-machine paint over whatever livery it wore.
        TeamPaint.Apply(renderers, ash);

        holder.transform.rotation = Quaternion.Euler(pitch, yaw, roll);

        var hull = holder.AddComponent<BoxCollider>();
        float height = Mathf.Max(0.8f, bounds.size.y * scale - 0.25f);
        hull.center = new Vector3(0f, height * 0.5f, 0f);
        hull.size = new Vector3(bounds.size.x * scale * 0.9f, height, bounds.size.z * scale * 0.9f);
    }

    /// <summary>
    /// Ruined half-walls between the ridge lines — two low slabs in an L,
    /// chest-high on a robot. Midfield cover that armies actually trade
    /// around, on the ground where the waves meet.
    /// </summary>
    static void BuildRuins(ArenaKit kit, System.Func<float, float, float> next)
    {
        var wall = ArenaMaterials.Style("Cmd_Ruin", ArenaMaterials.SurfaceStyle.Brick,
            new Color(0.17f, 0.16f, 0.19f), new Color(0.09f, 0.09f, 0.11f), 1.1f, 0.9f);

        int pairs = (int)next(4f, 7f);
        int placed = 0, attempts = 0;
        while (placed < pairs && attempts++ < 90)
        {
            float band = _ridgeZ - 7f;
            var pos = new Vector3(next(-70f, 70f), 0f, next(-band, band));
            if (Blocked(pos, baseKeepOut: 26f, fieldKeepOut: 12f, gapKeepOut: 10f))
                continue;

            float yaw = next(0f, 360f);
            float height = next(1.5f, 2.2f);
            var longArm = new Vector3(next(4f, 7f), height, next(0.7f, 1f));
            var shortArm = new Vector3(next(0.7f, 1f), height * next(0.6f, 0.9f), next(2.5f, 4.5f));
            var armOffset = Quaternion.Euler(0f, yaw, 0f)
                * new Vector3(longArm.x * 0.5f, 0f, shortArm.z * 0.5f);

            EmitRuin(kit, wall, pos, yaw, height, longArm, shortArm, armOffset, 1f);
            EmitRuin(kit, wall, -pos, yaw, height, longArm, shortArm, armOffset, -1f);
            placed++;
        }
    }

    static void EmitRuin(ArenaKit kit, Material wall, Vector3 pos, float yaw, float height,
        Vector3 longArm, Vector3 shortArm, Vector3 armOffset, float mirror)
    {
        var at = new Vector3(pos.x, 0f, pos.z);
        kit.Box("Ruin", at + new Vector3(0f, height * 0.5f - 0.15f, 0f), longArm, wall, yaw);
        kit.Box("Ruin", at + armOffset * mirror + new Vector3(0f, shortArm.y * 0.5f - 0.15f, 0f),
            shortArm, wall, yaw);
    }

    /// <summary>
    /// Teal energy vents: little glowing shard clusters, no collider, no
    /// light — pure signs of life on the plain, in the one hot colour
    /// nothing else on this map uses.
    /// </summary>
    static void BuildVents(ArenaKit kit, System.Func<float, float, float> next)
    {
        var teal = new Color(0.3f, 1f, 0.8f);
        var vent = ArenaMaterials.Style("Cmd_Vent", ArenaMaterials.SurfaceStyle.Crystal,
            new Color(0.08f, 0.22f, 0.18f), new Color(0.04f, 0.12f, 0.10f), 0.6f, 0.4f,
            teal, 1.5f);

        int pairs = (int)next(10f, 16f);
        int placed = 0, attempts = 0;
        while (placed < pairs && attempts++ < 120)
        {
            var pos = new Vector3(next(-78f, 78f), 0f, next(-78f, 78f));
            if (Blocked(pos, baseKeepOut: 18f, fieldKeepOut: 10f, gapKeepOut: 7f))
                continue;

            int shards = (int)next(2f, 4f);
            for (int s = 0; s < shards; s++)
            {
                var basePoint = pos + new Vector3(next(-1.2f, 1.2f), 0f, next(-1.2f, 1.2f));
                float height = next(0.5f, 1.1f);
                var tip = basePoint + new Vector3(next(-0.3f, 0.3f), height, next(-0.3f, 0.3f));
                var mirrorBase = new Vector3(-basePoint.x, 0f, -basePoint.z);
                var mirrorTip = new Vector3(-tip.x, tip.y, -tip.z);
                float thickness = next(0.25f, 0.45f);
                kit.Beam("Vent", basePoint - Vector3.up * 0.2f, tip, thickness, vent, collide: false);
                kit.Beam("Vent", mirrorBase - Vector3.up * 0.2f, mirrorTip, thickness, vent, collide: false);
            }
            placed++;
        }
    }

    /// <summary>
    /// Loose rocks for texture, in 180°-rotated pairs like everything else.
    /// A third of placements are CLUSTERS — a big stone with broken pieces
    /// around its feet — which is what flat scorch decals wanted to be and
    /// couldn't: on this renderer only real geometry reads as terrain, so
    /// the ground story is told entirely in stone.
    /// </summary>
    static void ScatterRocks(ArenaKit kit, Material rock, System.Func<float, float, float> next)
    {
        int target = (int)next(18f, 26f);
        int placed = 0, attempts = 0;
        while (placed < target && attempts++ < 300)
        {
            var pos = new Vector3(next(-80f, 80f), 0f, next(-80f, 80f));
            if (Blocked(pos, baseKeepOut: 22f, fieldKeepOut: 9f, gapKeepOut: 7f))
                continue;

            float size = next(1.6f, 3.4f);
            // Every full-size boulder is a PROSPECT — a robot can dig it open,
            // and what the map rolled underneath (crystal, or honest nothing)
            // is the same on both twins.
            bool hasCrystal = next(0f, 1f) < 0.45f;
            float digRequired = next(10f, 16f);
            RockPair(kit, rock, pos, size, next, prospect: true, hasCrystal, digRequired);

            // Rubble around the boulder's feet, a stride out in random
            // directions — a cluster reads as a place, a lone cube as a prop.
            if (next(0f, 1f) < 0.35f)
            {
                int pieces = (int)next(2f, 5f);
                for (int p = 0; p < pieces; p++)
                {
                    float angle = next(0f, Mathf.PI * 2f);
                    float dist = size * next(0.8f, 1.6f);
                    var piecePos = pos + new Vector3(Mathf.Cos(angle) * dist, 0f,
                        Mathf.Sin(angle) * dist);
                    // Rubble is just rubble — nobody prospects gravel.
                    RockPair(kit, rock, piecePos, next(0.5f, 1.2f), next, false, false, 0f);
                }
            }
            placed++;
        }
    }

    static void RockPair(ArenaKit kit, Material rock, Vector3 pos, float size,
        System.Func<float, float, float> next, bool prospect, bool hasCrystal, float digRequired)
    {
        var scale = new Vector3(size, size * 0.7f, size * next(0.7f, 1.1f));
        float yaw = next(0f, 360f);
        var south = kit.Box("Rock", new Vector3(pos.x, size * 0.35f, pos.z), scale, rock, yaw);
        var north = kit.Box("Rock", new Vector3(-pos.x, size * 0.35f, -pos.z), scale, rock, yaw);
        // EVERY boulder can be dug; whether crystal waits underneath is the
        // secret the digging exists to answer.
        if (prospect)
        {
            south.AddComponent<RockDeposit>().Init(hasCrystal, digRequired);
            north.AddComponent<RockDeposit>().Init(hasCrystal, digRequired);
        }
    }

    // ------------------------------------------------------------- keep-outs

    /// <summary>
    /// Whether a prop at <paramref name="pos"/> would crowd a base, a
    /// crystal field, a ridge line or a chokepoint lane. All keep-out
    /// geometry is itself symmetric, so one check clears both twins.
    /// </summary>
    static bool Blocked(Vector3 pos, float baseKeepOut, float fieldKeepOut, float gapKeepOut)
    {
        if (NearBase(pos, baseKeepOut))
            return true;
        foreach (var field in CrystalFields)
            if (Vector2.Distance(new Vector2(pos.x, pos.z), field) < fieldKeepOut)
                return true;
        // The ridge band itself...
        if (Mathf.Abs(Mathf.Abs(pos.z) - _ridgeZ) < 7f)
            return true;
        // ...and the traffic lanes through it: a chokepoint with a rock in
        // its mouth is a chokepoint the design lied about.
        foreach (var gap in _gapCenters)
            if (Mathf.Abs(Mathf.Abs(pos.z) - _ridgeZ) < 14f
                && (Mathf.Abs(pos.x - gap) < gapKeepOut || Mathf.Abs(pos.x + gap) < gapKeepOut))
                return true;
        return false;
    }

    static bool NearBase(Vector3 pos, float range) =>
        Vector3.Distance(pos, BaseSite(0)) < range || Vector3.Distance(pos, BaseSite(1)) < range;

    // ------------------------------------------------------------- extras

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
    /// Battlefield atmosphere: a whisper of fog for depth over 180 m
    /// sightlines and the scene's flat ambient. ArenaRuntime.Load reapplies
    /// the arena's own atmosphere on the way out, so nothing here needs
    /// undoing by hand.
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

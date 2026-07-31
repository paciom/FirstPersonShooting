using UnityEngine;

/// <summary>
/// The Brawl set: a floating strip over the void with glowing edge rails,
/// corner pylons and a distant backdrop, plus its own key and fill lights.
/// Own lights on purpose — the arena's key light points away from a side-on
/// framing (the robot-select preview rigs learned the same lesson), and it
/// may be deactivated with the environment anyway.
///
/// Built at runtime and parented under Environment so the Commander-style
/// world swap owns it; nothing here serializes into the scene.
/// </summary>
public static class BrawlStage
{
    /// <summary>Half-length of the authored stage's long axis.</summary>
    public const float LaneHalf = 8f;

    /// <summary>
    /// The fight AREA's half extents (x, z) — the brawl is a plane, not a
    /// line. Authored stages fence a 16×12 floor; remix stages open the
    /// whole arena, 28×28.
    /// </summary>
    public static Vector2 BoundsHalf { get; private set; } = new Vector2(LaneHalf, 6f);

    /// <summary>
    /// Where the fighters START (world z). Remix stages scan candidate
    /// lines and open on the most traversable one; the fight roams free
    /// from there.
    /// </summary>
    public static float SpawnZ { get; private set; }

    /// <summary>Where each fighter starts, either side of centre.</summary>
    public const float StartOffset = 3f;

    /// <summary>What the camera clears to past the stage — deep space navy.</summary>
    public static readonly Color VoidColor = new Color(0.012f, 0.028f, 0.062f);

    static readonly Color EdgeCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color DeckDark = new Color(0.10f, 0.13f, 0.18f);

    public static GameObject Build(Transform environment)
    {
        return Build(environment, default, null);
    }

    public static GameObject Build(Transform environment, BrawlArenaDef def, RobotRoster roster)
    {
        var root = new GameObject("BrawlStage");
        if (environment != null)
            root.transform.SetParent(environment, false);

        // A remix stage builds NO strip at all — no deck, rails or
        // backdrop merging awkwardly into arena floors. The root is just
        // the container for the stage's toys; the arena is the stage.
        if (def.remixArena)
        {
            BoundsHalf = new Vector2(14f, 14f);
            SpawnZ = PickFightLine();
            FinishFeatures(root, def, roster);
            return root;
        }
        BoundsHalf = new Vector2(LaneHalf, 6f);
        SpawnZ = 0f;

        // The deck: a FLOOR now, not a strip — the fight is a plane. Top
        // surface at exactly y = 0. Panel seams so motion reads anywhere.
        Box(root, "Deck",
            new Vector3(0f, -0.45f, 0f), new Vector3(LaneHalf * 2f + 4f, 0.9f, 13f),
            ArenaMaterials.Surface("brawl-deck", DeckDark, EdgeCyan, 3.5f, 0.5f));

        // Glowing rails around the whole floor — the ring, drawn in light.
        // 2.2 emission: hot enough to bloom, under the 2.5 whiteout line.
        var rail = ArenaMaterials.Emissive("brawl-rail", EdgeCyan, 2.2f);
        Box(root, "RailNear", new Vector3(0f, 0.03f, -6.55f),
            new Vector3(LaneHalf * 2f + 4f, 0.06f, 0.12f), rail);
        Box(root, "RailFar", new Vector3(0f, 0.03f, 6.55f),
            new Vector3(LaneHalf * 2f + 4f, 0.06f, 0.12f), rail);
        Box(root, "RailLeft", new Vector3(-(LaneHalf + 2f), 0.03f, 0f),
            new Vector3(0.12f, 0.06f, 13f), rail);
        Box(root, "RailRight", new Vector3(LaneHalf + 2f, 0.03f, 0f),
            new Vector3(0.12f, 0.06f, 13f), rail);

        // Corner pylons: the ring's posts. Dark column, hot cap.
        var pylon = ArenaMaterials.Lit("brawl-pylon", new Color(0.06f, 0.08f, 0.11f), 0.4f);
        var cap = ArenaMaterials.Emissive("brawl-pylon-cap", EdgeCyan, 2.0f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                float x = sx * (LaneHalf + 1.9f);
                Box(root, "Pylon", new Vector3(x, 1.5f, sz * 6.4f),
                    new Vector3(0.35f, 3f, 0.35f), pylon);
                Box(root, "PylonCap", new Vector3(x, 3.1f, sz * 6.4f),
                    new Vector3(0.45f, 0.2f, 0.45f), cap);
            }

        // Backdrop wall, far enough to blur into scenery, wide enough that
        // the camera never sees its edge at maximum pull-back.
        Box(root, "Backdrop", new Vector3(0f, 8f, 16f), new Vector3(80f, 24f, 0.6f),
            ArenaMaterials.Surface("brawl-backdrop", VoidColor * 1.8f, EdgeCyan * 0.6f, 10f, 0.25f));

        // Key from the camera's side of the lane, fill from behind — the
        // fighters' camera-facing surfaces are the ones that matter. Only the
        // key casts shadows: URP renders one shadowing directional, and a
        // second one silently wins or loses by intensity.
        Sun(root, "KeyLight", new Vector3(40f, 25f, 0f), 1.15f, new Color(1f, 0.97f, 0.92f), true);
        Sun(root, "FillLight", new Vector3(30f, 210f, 0f), 0.35f, new Color(0.55f, 0.75f, 1f), false);

        FinishFeatures(root, def, roster);
        return root;
    }

    /// <summary>
    /// Scan candidate fight lines across the arena and take the one with
    /// the fewest impassable steps. Tall solids read as height 99 via an
    /// embedded-sphere check — the vertical ray alone is blind to
    /// anything rising past its start, which is exactly a pillar.
    /// </summary>
    static float PickFightLine()
    {
        float bestZ = 0f;
        float bestScore = float.MinValue;
        for (float z = -8f; z <= 8f; z += 2f)
        {
            int breaks = 0;
            float roughness = 0f;
            float previous = SampleLine(-14f, z);
            for (float x = -13f; x <= 14f; x += 1f)
            {
                float height = SampleLine(x, z);
                float step = Mathf.Abs(height - previous);
                if (step > 1.2f)
                    breaks++;
                roughness += Mathf.Min(step, 2f);
                previous = height;
            }
            float score = -breaks * 10f - roughness - Mathf.Abs(z) * 0.3f;
            if (score > bestScore)
            {
                bestScore = score;
                bestZ = z;
            }
        }
        return bestZ;
    }

    static float SampleLine(float x, float z)
    {
        if (Physics.CheckSphere(new Vector3(x, 2.6f, z), 0.35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return 99f;   // a tall solid stands here
        if (Physics.Raycast(new Vector3(x, 3.4f, z), Vector3.down, out var hit, 4.4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return Mathf.Max(0f, hit.point.y);
        return 0f;
    }

    /// <summary>Dressing and toys — shared by authored and remix stages.</summary>
    static void FinishFeatures(GameObject root, BrawlArenaDef def, RobotRoster roster)
    {
        def.dress?.Invoke(root, roster);
        if (def.lifts)
        {
            BrawlLift.Spawn(root.transform, -3.5f, 0f);
            BrawlLift.Spawn(root.transform, 3.5f, 0.5f);
        }
        // Every stage runs the hazard scheduler now — repair kits, mines
        // and falling fire are universal; only the cargo rain is a per-def
        // toy.
        var hazards = root.AddComponent<BrawlHazards>();
        hazards.crates = def.crates;
        hazards.stageRoot = root.transform;

        SpawnGeysers(root.transform);
    }

    /// <summary>
    /// Two steam vents per stage, planted on clear flat ground near the
    /// fight line — far enough apart to matter, never inside a wall or on
    /// a ledge lip.
    /// </summary>
    static void SpawnGeysers(Transform stageRoot)
    {
        int placed = 0;
        for (int attempt = 0; attempt < 24 && placed < 2; attempt++)
        {
            float x = (placed == 0 ? -1f : 1f) * Random.Range(2.5f, 6f);
            float z = SpawnZ + Random.Range(-3f, 3f);
            var half = BoundsHalf;
            x = Mathf.Clamp(x, -(half.x - 2f), half.x - 2f);
            z = Mathf.Clamp(z, -(half.y - 2f), half.y - 2f);

            float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
            // Clear air at body height, and genuinely flat around the vent.
            if (Physics.CheckSphere(new Vector3(x, ground + 1.1f, z), 0.45f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;
            bool flat = true;
            for (float dx = -0.9f; dx <= 0.9f && flat; dx += 0.9f)
                for (float dz = -0.9f; dz <= 0.9f && flat; dz += 0.9f)
                    if (Mathf.Abs(BrawlGround.HeightAt(x + dx, z + dz, aboveY: 30f) - ground) > 0.25f)
                        flat = false;
            if (!flat)
                continue;

            BrawlGeyser.Spawn(stageRoot, x, z, placed * 0.5f);
            placed++;
        }
    }

    static GameObject Box(GameObject root, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static void Sun(GameObject root, string name, Vector3 euler, float intensity, Color color, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }
}

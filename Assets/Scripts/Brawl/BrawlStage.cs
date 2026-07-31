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
    /// <summary>Half-length of the walkable lane in metres — the corners.</summary>
    public const float LaneHalf = 8f;

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

        // The deck: top surface at exactly y = 0, where the fighters stand.
        // Panel-seam surface rather than flat lit, so lateral motion reads
        // even in the middle of an empty floor.
        Box(root, "Deck",
            new Vector3(0f, -0.45f, 0f), new Vector3(LaneHalf * 2f + 4f, 0.9f, 6f),
            ArenaMaterials.Surface("brawl-deck", DeckDark, EdgeCyan, 3.5f, 0.5f));

        // Glowing rails along the long edges — the lane, drawn in light.
        // 2.2 emission: hot enough to bloom, under the 2.5 whiteout line.
        var rail = ArenaMaterials.Emissive("brawl-rail", EdgeCyan, 2.2f);
        Box(root, "RailNear", new Vector3(0f, 0.03f, -3.05f),
            new Vector3(LaneHalf * 2f + 4f, 0.06f, 0.12f), rail);
        Box(root, "RailFar", new Vector3(0f, 0.03f, 3.05f),
            new Vector3(LaneHalf * 2f + 4f, 0.06f, 0.12f), rail);

        // Corner pylons: the ends of the world, so being cornered is visible
        // from across the room. Dark column, hot cap.
        var pylon = ArenaMaterials.Lit("brawl-pylon", new Color(0.06f, 0.08f, 0.11f), 0.4f);
        var cap = ArenaMaterials.Emissive("brawl-pylon-cap", EdgeCyan, 2.0f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                float x = sx * (LaneHalf + 1.4f);
                Box(root, "Pylon", new Vector3(x, 1.5f, sz * 2.7f),
                    new Vector3(0.35f, 3f, 0.35f), pylon);
                Box(root, "PylonCap", new Vector3(x, 3.1f, sz * 2.7f),
                    new Vector3(0.45f, 0.2f, 0.45f), cap);
            }

        // Backdrop wall, far enough to blur into scenery, wide enough that
        // the camera never sees its edge at maximum pull-back. A remix
        // stage skips it — the surrounding arena IS the scenery.
        if (!def.remixArena)
            Box(root, "Backdrop", new Vector3(0f, 8f, 16f), new Vector3(80f, 24f, 0.6f),
                ArenaMaterials.Surface("brawl-backdrop", VoidColor * 1.8f, EdgeCyan * 0.6f, 10f, 0.25f));

        // Key from the camera's side of the lane, fill from behind — the
        // fighters' camera-facing surfaces are the ones that matter. Only the
        // key casts shadows: URP renders one shadowing directional, and a
        // second one silently wins or loses by intensity.
        Sun(root, "KeyLight", new Vector3(40f, 25f, 0f), 1.15f, new Color(1f, 0.97f, 0.92f), true);
        Sun(root, "FillLight", new Vector3(30f, 210f, 0f), 0.35f, new Color(0.55f, 0.75f, 1f), false);

        // The stage's own personality: backdrop dressing and its toys.
        def.dress?.Invoke(root, roster);
        if (def.lifts)
        {
            BrawlLift.Spawn(root.transform, -3.5f, 0f);
            BrawlLift.Spawn(root.transform, 3.5f, 0.5f);
        }
        if (def.crates || def.balls)
        {
            var hazards = root.AddComponent<BrawlHazards>();
            hazards.crates = def.crates;
            hazards.balls = def.balls;
            hazards.stageRoot = root.transform;
        }

        return root;
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

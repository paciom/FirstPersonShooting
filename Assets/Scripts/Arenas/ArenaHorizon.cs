using UnityEngine;

/// <summary>
/// The world outside the arena: ridgelines, spires and a hanging moon.
///
/// This is the difference between the painted mode cards and the game that
/// used to sit behind them. Those images do not look expensive because the
/// props are detailed — they look expensive because they have DEPTH: a
/// near silhouette, a middle distance, a far ridge, each one paler than the
/// last as the air stacks up in front of it, under a sky that is not a
/// single flat colour. An arena that stops at its own boundary wall is a
/// diorama on a table, however good the rocks in it are.
///
/// Everything here is silhouette work: no colliders, no gameplay, three
/// bands of distance, and colours pushed toward the fog so the ranges read
/// as far away rather than merely dark. Costs a few hundred triangles.
/// </summary>
public static class ArenaHorizon
{
    /// <summary>Ridge rings, mid-ground spires and a moon, from the palette.</summary>
    public static void Build(Transform parent, ArenaDefinition def)
    {
        var p = def.Palette;
        var rng = new System.Random(def.DisplayName.GetHashCode() ^ 0x5EED);
        var root = new GameObject("Horizon");
        root.transform.SetParent(parent, false);

        // Atmospheric perspective, the whole trick in one line: the further
        // a band sits, the closer its colour is to the fog it hangs in.
        Color Haze(float distance01, float darken)
        {
            var body = Color.Lerp(p.wall, p.floor, 0.35f) * darken;
            var air = def.Palette.fogDensity > 0f ? p.fog : p.sky;
            return Color.Lerp(body, air, Mathf.Lerp(0.35f, 0.88f, distance01));
        }

        // --- far ridge: a full ring, tall, almost the colour of the air ---
        var farMat = ArenaMaterials.Lit("Horizon_Far", Haze(1f, 0.55f), 0.05f);
        Ring(root.transform, rng, farMat, count: 34, radius: 96f, radiusJitter: 22f,
             heightMin: 26f, heightMax: 62f, widthMin: 20f, widthMax: 46f, sink: 6f);

        // --- middle distance: reads as real terrain, still hazed ---
        var midMat = ArenaMaterials.Lit("Horizon_Mid", Haze(0.55f, 0.7f), 0.08f);
        Ring(root.transform, rng, midMat, count: 22, radius: 52f, radiusJitter: 12f,
             heightMin: 12f, heightMax: 30f, widthMin: 9f, widthMax: 20f, sink: 3f);

        // --- near spires: sharp, nearly full colour, just past the walls ---
        var nearMat = ArenaMaterials.Style("Horizon_Near", def.CoverStyle,
            Haze(0.2f, 0.85f), p.floor * 0.5f, 2.4f, 0.9f);
        Ring(root.transform, rng, nearMat, count: 14, radius: 30f, radiusJitter: 5f,
             heightMin: 7f, heightMax: 18f, widthMin: 3.5f, widthMax: 8f, sink: 1.5f);

        Moon(root.transform, rng, p);
    }

    /// <summary>One band of the horizon, scattered around the arena.</summary>
    static void Ring(Transform parent, System.Random rng, Material material,
                     int count, float radius, float radiusJitter,
                     float heightMin, float heightMax,
                     float widthMin, float widthMax, float sink)
    {
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        for (int i = 0; i < count; i++)
        {
            // Jittered angle, never evenly spaced — a ring of equal gaps
            // reads as a fence, and the eye catches it immediately.
            float angle = (i + Next(-0.38f, 0.38f)) / count * Mathf.PI * 2f;
            float distance = radius + Next(-radiusJitter, radiusJitter);
            float height = Next(heightMin, heightMax);
            float width = Next(widthMin, widthMax);

            var peak = new GameObject("Ridge");
            peak.transform.SetParent(parent, false);
            peak.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * distance,
                // Sunk into the ground so the bases never float, and the
                // arena floor hides where they start.
                height * 0.5f - sink,
                Mathf.Sin(angle) * distance);
            peak.transform.localRotation = Quaternion.Euler(
                Next(-3f, 3f), Next(0f, 360f), Next(-3f, 3f));
            peak.transform.localScale = new Vector3(width, height, width * Next(0.7f, 1.3f));

            var form = Next(0f, 1f) < 0.45f ? ArenaRock.Form.Shard : ArenaRock.Form.Buttress;
            peak.AddComponent<MeshFilter>().sharedMesh = ArenaRock.Unit(form, rng.Next());
            peak.AddComponent<MeshRenderer>().sharedMaterial = material;
            // Scenery only: nothing here casts shadows into the fight or
            // answers a raycast, or the terrain probes would find a
            // mountain where the floor should be.
            peak.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>A moon low over the ridge — the cheapest "alien planet" cue there is.</summary>
    static void Moon(Transform parent, System.Random rng, ArenaPalette p)
    {
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moon.name = "Moon";
        Object.Destroy(moon.GetComponent<Collider>());
        moon.transform.SetParent(parent, false);
        float angle = Next(0f, Mathf.PI * 2f);
        moon.transform.localPosition = new Vector3(
            Mathf.Cos(angle) * 150f, Next(38f, 70f), Mathf.Sin(angle) * 150f);
        moon.transform.localScale = Vector3.one * Next(28f, 46f);
        // Well under the 2.5 whiteout: a moon should read as a disc with a
        // face, not a bloom blob.
        moon.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Emissive(
            "Horizon_Moon", Color.Lerp(p.accentA, p.sky, 0.55f), 0.9f);
        moon.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

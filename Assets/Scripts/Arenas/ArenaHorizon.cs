using UnityEngine;

/// <summary>
/// The world outside the arena: a hanging moon under an open sky.
///
/// This used to be three rings of generated rock — near spires, a middle
/// range, a far ridge — for atmospheric depth. They are gone by request:
/// the lumpy organic silhouettes read as "strange rocks" against the
/// hard-edged arenas, both over the walls and, on low-walled maps, seemingly
/// inside them. What remains is the one horizon prop that never read as a
/// rock. If depth outside the walls comes back, it comes back BUILT —
/// rectilinear structure, not geology.
/// </summary>
public static class ArenaHorizon
{
    /// <summary>The moon, from the palette.</summary>
    public static void Build(Transform parent, ArenaDefinition def)
    {
        var rng = new System.Random(def.DisplayName.GetHashCode() ^ 0x5EED);
        var root = new GameObject("Horizon");
        root.transform.SetParent(parent, false);
        Moon(root.transform, rng, def.Palette);
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

using UnityEngine;

/// <summary>
/// Builds a low-poly hover-robot from primitives: armored torso, glowing chest
/// core and visor, antenna, arms, hover ring and thruster glow. A big visual
/// step up from capsules until Meshy-generated models replace it — the factory
/// interface stays the same either way.
/// </summary>
public static class RobotFactory
{
    /// <summary>Creates the "Body" rig under <paramref name="root"/> and returns its transform.</summary>
    public static Transform Build(GameObject root, Material armor, Material glow, Material darkMetal)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);

        // Torso
        Part(body, PrimitiveType.Cube, new Vector3(0, 0.15f, 0), new Vector3(0.55f, 0.65f, 0.38f), armor);
        // Chest core (glow)
        Part(body, PrimitiveType.Cube, new Vector3(0, 0.22f, 0.20f), new Vector3(0.20f, 0.20f, 0.04f), glow);
        // Pelvis taper
        Part(body, PrimitiveType.Cube, new Vector3(0, -0.28f, 0), new Vector3(0.40f, 0.22f, 0.30f), darkMetal);

        // Head + visor + antenna
        Part(body, PrimitiveType.Cube, new Vector3(0, 0.70f, 0), new Vector3(0.36f, 0.28f, 0.34f), armor);
        Part(body, PrimitiveType.Cube, new Vector3(0, 0.72f, 0.175f), new Vector3(0.28f, 0.09f, 0.02f), glow);
        Part(body, PrimitiveType.Cylinder, new Vector3(0.13f, 0.94f, 0), new Vector3(0.03f, 0.10f, 0.03f), darkMetal);
        Part(body, PrimitiveType.Sphere, new Vector3(0.13f, 1.06f, 0), Vector3.one * 0.07f, glow);

        // Shoulders + arms
        Part(body, PrimitiveType.Cube, new Vector3(0.40f, 0.38f, 0), Vector3.one * 0.18f, darkMetal);
        Part(body, PrimitiveType.Cube, new Vector3(-0.40f, 0.38f, 0), Vector3.one * 0.18f, darkMetal);
        Part(body, PrimitiveType.Cube, new Vector3(0.42f, 0.02f, 0.04f), new Vector3(0.11f, 0.48f, 0.11f), armor,
            Quaternion.Euler(8f, 0f, -6f));
        Part(body, PrimitiveType.Cube, new Vector3(-0.42f, 0.02f, 0.04f), new Vector3(0.11f, 0.48f, 0.11f), armor,
            Quaternion.Euler(8f, 0f, 6f));

        // Hover ring + thruster glow (no legs — hovering reads great with zero animation work)
        Part(body, PrimitiveType.Cylinder, new Vector3(0, -0.52f, 0), new Vector3(0.52f, 0.035f, 0.52f), darkMetal);
        Part(body, PrimitiveType.Sphere, new Vector3(0, -0.52f, 0), new Vector3(0.30f, 0.16f, 0.30f), glow);

        return body;
    }

    /// <summary>
    /// Builds the "Body" rig from an imported 3D model (e.g. Meshy-generated GLB)
    /// instead of primitives. Normalizes the model to character height, applies a
    /// subtle team tint, and adds a glowing team ring underneath. Same rig
    /// contract as Build(), so DeRezEffect/HoverBob/ShieldBubble work unchanged.
    /// </summary>
    public static Transform BuildFromModel(GameObject root, GameObject modelPrefab, Color teamTint, Material ringGlow)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);

        var instance = Object.Instantiate(modelPrefab, body);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            // Multiply (never replace) the prefab's own scale — glTF roots often
            // carry a unit-conversion scale factor that must be preserved.
            float scale = 1.6f / Mathf.Max(0.01f, bounds.size.y);
            instance.transform.localScale *= scale;
            Vector3 localCenter = body.InverseTransformPoint(bounds.center);
            instance.transform.localPosition = -localCenter * scale;

            // Tint copies of the imported materials — never the shared import assets.
            foreach (var r in renderers)
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                        continue;
                    var copy = new Material(materials[i]);
                    string prop = copy.HasProperty("_BaseColor") ? "_BaseColor"
                        : copy.HasProperty("_Color") ? "_Color" : null;
                    if (prop != null)
                        copy.SetColor(prop, copy.GetColor(prop) * Color.Lerp(Color.white, teamTint, 0.35f));
                    materials[i] = copy;
                }
                r.sharedMaterials = materials;
            }
        }

        // Glowing team ring under the robot for instant team readability on camera.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "TeamRing";
        var ringCollider = ring.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(ringCollider);
        else Object.DestroyImmediate(ringCollider);
        ring.transform.SetParent(body, false);
        ring.transform.localPosition = new Vector3(0f, -0.85f, 0f);
        ring.transform.localScale = new Vector3(0.6f, 0.025f, 0.6f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ringGlow;

        return body;
    }

    static void Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale,
        Material material, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = type.ToString();

        var collider = go.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(collider);
        else Object.DestroyImmediate(collider);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (rotation.HasValue)
            go.transform.localRotation = rotation.Value;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}

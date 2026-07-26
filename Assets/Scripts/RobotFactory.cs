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

        InstantiateNormalized(modelPrefab, body, teamTint);

        // Team marker under the robot for instant team readability on camera:
        // a crisp team-colored donut ring plus a faint wider glow wash — flat
        // sprites on the floor instead of the old solid cylinder, which bloom
        // blew out into a plain white blob. The additive shader is two-sided
        // and ZWrite Off, so the flat quads can't z-fight the floor.
        // (ringGlow material kept in the signature for the primitive-robot path.)
        var ringRoot = new GameObject("TeamRing");
        ringRoot.transform.SetParent(body, false);
        ringRoot.transform.localPosition = new Vector3(0f, -0.85f, 0f);
        FlatGlowQuad(ringRoot.transform, "Ring", "VFX/ring", teamTint, 1.5f, 1.5f, 0.02f);
        FlatGlowQuad(ringRoot.transform, "Wash", "VFX/glow", teamTint, 0.35f, 2.4f, 0f);

        return body;
    }

    /// <summary>A floor-flat additive sprite quad (team rings, hover washes).</summary>
    static void FlatGlowQuad(Transform parent, string name, string texturePath, Color color,
        float intensity, float size, float yOffset)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        var collider = quad.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(collider);
        else Object.DestroyImmediate(collider);
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, yOffset, 0f);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat
        quad.transform.localScale = Vector3.one * size;

        var mat = new Material(Shader.Find("PhotonArena/Additive"));
        mat.SetTexture("_MainTex", Resources.Load<Texture2D>(texturePath));
        mat.SetColor("_Color", color);
        mat.SetFloat("_Intensity", intensity);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>
    /// Runtime robot swap: replaces the model inside an existing "Body" rig
    /// with a different roster robot, keeping the Blaster (weapon components
    /// live on it) and the TeamRing untouched. Everything DeRezEffect /
    /// HoverBob / ShieldBubble reference is the Body transform itself, so the
    /// swap is invisible to them.
    /// </summary>
    public static void Reskin(Transform body, GameObject modelPrefab, Color teamTint)
    {
        for (int i = body.childCount - 1; i >= 0; i--)
        {
            var child = body.GetChild(i);
            // VehicleRig belongs to TransformMode, which builds it once and
            // hands out no way to rebuild it — losing it to a reskin would
            // leave a robot that transforms with no wheels.
            if (child.name == "Blaster" || child.name == "TeamRing" || child.name == "VehicleRig")
                continue;
            Object.Destroy(child.gameObject);
        }
        InstantiateNormalized(modelPrefab, body, teamTint);
    }

    /// <summary>
    /// Instantiates a model under <paramref name="body"/> as "Model",
    /// normalizes it to character height (multiplying — never replacing — the
    /// glTF unit-conversion scale), recenters it, and tints copies of its
    /// materials toward the team color.
    /// </summary>
    public static GameObject InstantiateNormalized(GameObject modelPrefab, Transform body, Color teamTint)
    {
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

            // Hovering robots sit centred on the Body pivot; legged ones (the
            // rigged walkers, which carry a RobotLocomotion) must have their
            // feet on the floor instead, i.e. at the character root's own y.
            if (instance.GetComponentInChildren<RobotLocomotion>(true) != null)
            {
                Vector3 localBottom = body.InverseTransformPoint(
                    new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                instance.transform.localPosition = new Vector3(
                    -localCenter.x * scale,
                    -localBottom.y * scale - body.localPosition.y,
                    -localCenter.z * scale);
            }
            else
            {
                instance.transform.localPosition = -localCenter * scale;
            }

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
        return instance;
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

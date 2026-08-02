using UnityEngine;

/// <summary>
/// Builds a low-poly hover-robot from primitives: armored torso, glowing chest
/// core and visor, antenna, arms, hover ring and thruster glow. A big visual
/// step up from capsules until Meshy-generated models replace it — the factory
/// interface stays the same either way.
/// </summary>
public static class RobotFactory
{
    /// <summary>
    /// Height every roster robot is fitted to, in metres. The roster's models
    /// arrive at wildly different sizes; normalizing here is what lets one set
    /// of arena, camera and jump numbers apply to all of them.
    /// </summary>
    public const float NormalizedHeight = 1.6f;

    /// <summary>
    /// How high a robot leaps, as a multiple of its own height: its own head
    /// plus 20%. One constant because both jump paths must agree — the
    /// player's motor (CharacterMotor.Jump) and the bots' link leaps
    /// (RobotJump) — or the same robot would jump two different heights
    /// depending on who was driving it.
    /// </summary>
    public const float JumpHeights = 1.2f;

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
    /// instead of primitives. Normalizes the model to character height and applies
    /// a subtle team tint. Same rig contract as Build(), so DeRezEffect/HoverBob/
    /// ShieldBubble work unchanged.
    ///
    /// No floor marker: the robots are painted in their team's colour, which is
    /// what tells the teams apart now. A ring under every pair of feet only
    /// repeated what the paint already said.
    /// </summary>
    public static Transform BuildFromModel(GameObject root, GameObject modelPrefab, Color teamTint,
        float paintAnchorHue = -1f)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);

        InstantiateNormalized(modelPrefab, body, teamTint, paintAnchorHue);

        return body;
    }

    /// <summary>
    /// Destroys the team ring an older ArenaBuilder baked under a robot.
    ///
    /// The rings are serialized into the shipped arena scene, so dropping the
    /// code that makes them would leave the existing six bots wearing theirs
    /// until somebody rebuilt the scene. Sweeping them at load costs nothing on
    /// a rebuilt scene, where there is nothing to find.
    /// </summary>
    public static void StripTeamRing(Transform root)
    {
        if (root == null)
            return;
        var children = root.GetComponentsInChildren<Transform>(true);
        foreach (var child in children)
        {
            if (child == null || child.name != TeamRingName)
                continue;
            if (Application.isPlaying) Object.Destroy(child.gameObject);
            else Object.DestroyImmediate(child.gameObject);
        }
    }

    const string TeamRingName = "TeamRing";

    /// <summary>
    /// Runtime robot swap: replaces the model inside an existing "Body" rig
    /// with a different roster robot, keeping the Blaster untouched (weapon
    /// components live on it). Everything DeRezEffect / HoverBob / ShieldBubble
    /// reference is the Body transform itself, so the swap is invisible to them.
    /// </summary>
    public static void Reskin(Transform body, GameObject modelPrefab, Color teamTint,
        float paintAnchorHue = -1f)
    {
        for (int i = body.childCount - 1; i >= 0; i--)
        {
            var child = body.GetChild(i);
            // VehicleRig belongs to TransformMode, which builds it once and
            // hands out no way to rebuild it — losing it to a reskin would
            // leave a robot that transforms with no wheels.
            if (child.name == "Blaster" || child.name == TransformMode.RigName)
                continue;
            Object.Destroy(child.gameObject);
        }
        InstantiateNormalized(modelPrefab, body, teamTint, paintAnchorHue);
    }

    /// <summary>
    /// Instantiates a model under <paramref name="body"/> as "Model",
    /// normalizes it to character height (multiplying — never replacing — the
    /// glTF unit-conversion scale), recenters it, and repaints copies of its
    /// materials into the team's colors.
    /// </summary>
    public static GameObject InstantiateNormalized(GameObject modelPrefab, Transform body,
        Color teamTint, float paintAnchorHue = -1f)
    {
        var instance = Object.Instantiate(modelPrefab, body);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = MeasureWorldBounds(renderers);

            // Multiply (never replace) the prefab's own scale — glTF roots often
            // carry a unit-conversion scale factor that must be preserved.
            float scale = NormalizedHeight / Mathf.Max(0.01f, bounds.size.y);
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

            // Repaint copies of the imported materials into the team's colours —
            // never the shared import assets. See TeamPaint for why this is a
            // hue replacement rather than the colour multiply it grew out of.
            TeamPaint.Apply(renderers, teamTint, TeamPaint.DefaultSize, false, paintAnchorHue);
        }
        return instance;
    }

    /// <summary>
    /// How tall the robot standing on <paramref name="character"/> actually is,
    /// in metres.
    ///
    /// Measures the "Model" child specifically rather than everything under the
    /// character: the held blaster, and any floor decal a scene still carries,
    /// are renderers too, and a flat 2.4-unit quad on the ground would answer a
    /// question nobody asked. Falls back to <see cref="NormalizedHeight"/> — which is
    /// what the model will be fitted to anyway — for a character whose model
    /// has not been built yet, which is every pawn before the roster screen
    /// dresses it.
    /// </summary>
    public static float MeasureHeight(Transform character)
    {
        var model = FindDeep(character, "Model");
        if (model == null)
            return NormalizedHeight;
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return NormalizedHeight;
        float height = MeasureWorldBounds(renderers).size.y;
        return height > 0.01f ? height : NormalizedHeight;
    }

    /// <summary>
    /// Apex a robot standing on <paramref name="character"/> must clear: its
    /// own height plus 20%. See <see cref="JumpHeights"/>.
    /// </summary>
    public static float JumpApex(Transform character)
    {
        return MeasureHeight(character) * JumpHeights;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// World-space box that actually contains a model's geometry.
    ///
    /// WHY NOT Renderer.bounds. On the roster's rigged robots it is not a box
    /// around the robot. Unity derives a SkinnedMeshRenderer's box from its
    /// localBounds placed at the ROOT BONE, and the Meshy rigs arrive with no
    /// skin.skeleton, so glTFast never assigns one (GltfImport only sets
    /// rootJoint when skeleton >= 0) and the box is left hanging off whichever
    /// transform Unity falls back to. Close enough for culling — which is all
    /// Unity uses it for — and half a robot out for code that measures where
    /// the feet are. That gap is the floating.
    ///
    /// The mesh's own bounds ARE in mesh space, and bones[i].localToWorldMatrix
    /// * bindposes[i] is precisely the matrix skinning uses to put mesh space
    /// into the world. Measuring through it gives the box the vertices are
    /// really in, and gives it immediately — Renderer.bounds only becomes
    /// trustworthy (via updateWhenOffscreen) after a frame of skinning has gone
    /// by, which is always one frame after a model is instantiated and fitted.
    /// </summary>
    public static Bounds MeasureWorldBounds(Renderer[] renderers)
    {
        Bounds total = default;
        bool any = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            Bounds b = MeasureWorldBounds(renderer);
            if (!any) { total = b; any = true; }
            else total.Encapsulate(b);
        }
        return any ? total : new Bounds(Vector3.zero, Vector3.zero);
    }

    /// <summary>One renderer's true world box. Falls back to Renderer.bounds for
    /// anything that isn't a skinned mesh with usable bind poses.</summary>
    public static Bounds MeasureWorldBounds(Renderer renderer)
    {
        var skin = renderer as SkinnedMeshRenderer;
        var mesh = skin != null ? skin.sharedMesh : null;
        if (mesh == null)
            return renderer.bounds;

        var bones = skin.bones;
        if (bones == null || bones.Length == 0 || bones[0] == null)
            return renderer.bounds;

        var bindPoses = mesh.bindposes;
        if (bindPoses == null || bindPoses.Length == 0)
            return renderer.bounds;

        return TransformBounds(bones[0].localToWorldMatrix * bindPoses[0], mesh.bounds);
    }

    /// <summary>Axis-aligned world box of a local box under an arbitrary matrix.</summary>
    static Bounds TransformBounds(Matrix4x4 m, Bounds local)
    {
        Vector3 e = local.extents;
        var extents = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        return new Bounds(m.MultiplyPoint3x4(local.center), extents * 2f);
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

using UnityEngine;

/// <summary>
/// The imported Missiles Pack, turned into something a projectile can wear.
///
/// Three things stand between that pack and the arena, and all six missile
/// weapons would otherwise each have to solve them:
///
/// 1. Its prefabs live outside a Resources folder, where a runtime load cannot
///    reach them — copies sit in Assets/Resources/Missiles.
/// 2. Its material is a BUILT-IN pipeline Standard shader, which renders solid
///    magenta under URP. The texture reference on that material is still good,
///    so we read it back off and rebuild the material on URP/Lit.
/// 3. Nothing declares which way a missile points. Rather than hand-measuring
///    six meshes, <see cref="Measure"/> takes the longest axis of the mesh and
///    works out which end is the nose from the silhouette — a nose tapers to a
///    point where a tail spreads into fins, so the narrower half is the front.
///    A seventh missile would need no new numbers.
///
/// Everything here is cached: one material and one measurement per model, no
/// matter how many missiles are in the air.
/// </summary>
public static class MissileModels
{
    /// <summary>Models in the pack, numbered as the pack numbers them (1-6).</summary>
    public const int Count = 6;

    /// <summary>How the mesh has to be turned and shrunk to fly nose-first.</summary>
    struct Fit
    {
        public Quaternion nose;    // mesh's own nose axis → +Z
        public float unitScale;    // ×length gives a missile that long
        public Vector3 centre;     // mesh centre, so the body rides on the bolt
        public bool measured;
    }

    static readonly GameObject[] Prefabs = new GameObject[Count];
    static readonly Fit[] Fits = new Fit[Count];
    static Material _skin;

    /// <summary>
    /// Hide the bolt's own primitive and fly this missile mesh instead.
    ///
    /// The body is a child that steers itself (see MissileBodyEntity) rather
    /// than inheriting the bolt's rotation: GenericBolt aims its capsule with a
    /// 90° pitch that belongs to the capsule mesh, and borrowing that convention
    /// here would tie six weapons to an implementation detail of a class fifty
    /// others share.
    /// </summary>
    public static void Dress(GenericBolt bolt, int model, float length)
    {
        if (bolt == null)
            return;

        var pivot = new GameObject("Missile");
        pivot.transform.SetParent(bolt.transform, false);
        pivot.AddComponent<MissileBodyEntity>().bolt = bolt;

        // Hide the bolt's own primitive only once there is something to hide it
        // BEHIND. If the pack ever goes missing from the project, the weapon
        // falls back to firing a plain glowing bolt rather than nothing at all.
        if (Attach(pivot.transform, model, length) == null)
        {
            Object.Destroy(pivot);
            return;
        }
        var primitive = bolt.GetComponent<MeshRenderer>();
        if (primitive != null)
            primitive.enabled = false;
    }

    /// <summary>
    /// Instantiate model <paramref name="model"/> under <paramref name="parent"/>,
    /// nose down +Z and <paramref name="length"/> metres long. Used by the bolts
    /// and by anything that wants a missile standing still.
    /// </summary>
    public static Transform Attach(Transform parent, int model, float length)
    {
        var prefab = Prefab(model);
        if (prefab == null)
            return null;

        var instance = Object.Instantiate(prefab, parent);
        instance.name = "MissileBody";

        // Pack prefabs carry no colliders, but an imported mesh that grew one
        // would deflect the shots of whoever fired it.
        foreach (var collider in instance.GetComponentsInChildren<Collider>())
            Object.Destroy(collider);

        var skin = Skin();
        if (skin != null)
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = skin;

        Fit fit = FitOf(model);
        float scale = length * fit.unitScale;
        instance.transform.localScale = Vector3.one * scale;
        instance.transform.localRotation = fit.nose;
        // Turn and shrink the centre the same way the mesh was turned and
        // shrunk, then cancel it: the missile rides ON the bolt rather than
        // orbiting a point somewhere off its nose.
        instance.transform.localPosition = -(fit.nose * fit.centre) * scale;
        return instance.transform;
    }

    static GameObject Prefab(int model)
    {
        if (model < 0 || model >= Count)
            return null;
        if (Prefabs[model] == null)
            Prefabs[model] = Resources.Load<GameObject>($"Missiles/Missil_{model + 1:00}");
        return Prefabs[model];
    }

    static Fit FitOf(int model)
    {
        if (model < 0 || model >= Count)
            return new Fit { nose = Quaternion.identity, unitScale = 1f };
        if (!Fits[model].measured)
            Fits[model] = Measure(Prefab(model));
        return Fits[model];
    }

    /// <summary>
    /// Which way this mesh points and how big it is, read off the mesh itself.
    /// See the class summary for why the narrow end is taken to be the nose.
    /// </summary>
    static Fit Measure(GameObject prefab)
    {
        var fit = new Fit { nose = Quaternion.identity, unitScale = 1f, measured = true };

        var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
        var mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null)
            return fit;

        Bounds bounds = mesh.bounds;
        Vector3 size = bounds.size;
        int axis = 0;
        if (size.y > size[axis]) axis = 1;
        if (size.z > size[axis]) axis = 2;
        Vector3 along = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        fit.centre = bounds.center;
        fit.unitScale = 1f / Mathf.Max(0.0001f, size[axis]);

        // Read once: mesh.vertices copies the whole buffer on every access.
        var vertices = mesh.vertices;
        if (vertices.Length == 0)
        {
            // Empty means the FBX's "Read/Write Enabled" was turned off, which
            // is silent in the editor and only bites in a build. Bounds still
            // give us the axis, so the missile at least flies lengthways; which
            // END leads becomes a coin toss, so say so rather than let a
            // backwards missile look like a physics bug.
            Debug.LogWarning("MissileModels: no readable vertices on the missile mesh " +
                             "(Read/Write Enabled off on Missiles_Pack.FBX?) — nose direction is a guess.");
            fit.nose = Quaternion.FromToRotation(along, Vector3.forward);
            return fit;
        }

        float extent = size[axis];
        float middle = Vector3.Dot(bounds.center, along);
        float ahead = EndRadius(vertices, bounds, along, middle + extent * 0.3f, true);
        float behind = EndRadius(vertices, bounds, along, middle - extent * 0.3f, false);

        // Measured over the outer fifth of each end rather than each half: the
        // halves differ by only a few per cent on these meshes (a cone's base
        // ring sits near the middle and drags its average up), where the tips
        // differ by two to one. Verified against all six meshes in the pack.
        bool positiveIsNose = ahead <= behind;
        fit.nose = Quaternion.FromToRotation(positiveIsNose ? along : -along, Vector3.forward);
        return fit;
    }

    /// <summary>
    /// Mean distance from the long axis of the vertices past <paramref name="cut"/>
    /// — how fat this end of the mesh is. A missile's nose tapers to a point;
    /// its tail spreads into fins.
    /// </summary>
    static float EndRadius(Vector3[] vertices, Bounds bounds, Vector3 along, float cut, bool positive)
    {
        float total = 0f;
        int count = 0;
        foreach (var vertex in vertices)
        {
            float t = Vector3.Dot(vertex, along);
            if (positive ? t < cut : t > cut)
                continue;
            total += Vector3.ProjectOnPlane(vertex - bounds.center, along).magnitude;
            count++;
        }
        // An empty cap can't be compared; treat it as infinitely fat so the
        // other end wins rather than the tie going to whichever is "positive".
        return count == 0 ? float.MaxValue : total / count;
    }

    /// <summary>
    /// One shared URP material for every missile in the air, painted with the
    /// pack's own texture — read off the pack's unusable Standard material,
    /// which still holds a perfectly good texture reference.
    ///
    /// Emissive at 0.4: enough that a missile reads against a dark arena
    /// without the bloom cooking it white (see the arena's post-FX notes — a
    /// solid body wants to stay well under the energy weapons' glow).
    /// </summary>
    static Material Skin()
    {
        if (_skin != null)
            return _skin;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return null;

        _skin = new Material(shader) { name = "MissileSkin" };
        _skin.SetFloat("_Smoothness", 0.35f);

        var texture = PackTexture();
        if (texture != null)
        {
            _skin.SetTexture("_BaseMap", texture);
            _skin.EnableKeyword("_EMISSION");
            _skin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            _skin.SetTexture("_EmissionMap", texture);
            _skin.SetColor("_EmissionColor", Color.white * 0.4f);
        }
        else
        {
            // No texture found: a plain gunmetal body still reads as a missile.
            _skin.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.6f));
        }
        return _skin;
    }

    static Texture PackTexture()
    {
        for (int model = 0; model < Count; model++)
        {
            var prefab = Prefab(model);
            var renderer = prefab != null ? prefab.GetComponentInChildren<Renderer>() : null;
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null)
                continue;
            if (material.HasProperty("_MainTex") && material.mainTexture != null)
                return material.mainTexture;
            if (material.HasProperty("_BaseMap"))
            {
                var texture = material.GetTexture("_BaseMap");
                if (texture != null)
                    return texture;
            }
        }
        return null;
    }
}

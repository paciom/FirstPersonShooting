using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What each weapon LOOKS like: the prop the player holds in first person, and
/// the same prop again as the picture on its card in the ARMS rack.
///
/// One registry rather than two, because a weapon whose icon and whose viewmodel
/// disagree is worse than one with no picture at all — you pick the thing in the
/// rack and something else appears in your hands.
///
/// Models are looked up BY NAME rather than through a table: a weapon called
/// PrismSplitter wears Assets/Resources/Weapons/prism-splitter.glb. Sixty
/// hand-maintained rows would be sixty chances to point a weapon at the wrong
/// gun, and the convention costs nothing to follow — the generator writes the
/// files under the names it reads from this same catalogue.
///
/// A weapon with no model of its own gets <see cref="BuildFallback"/>: a plain
/// blaster silhouette in the weapon's own colour. That is what the whole arsenal
/// looked like before any model existed, and it stays the floor — every weapon
/// has a picture from the first frame, and the ones with real models simply look
/// better.
/// </summary>
public static class WeaponArt
{
    /// <summary>Where the generated models live, under a Resources folder.</summary>
    const string ModelRoot = "Weapons/";

    /// <summary>
    /// How long the prop is, in metres, whatever the model arrives as. Generated
    /// models come out of the pipeline at unrelated scales, and a viewmodel that
    /// changes size with the weapon reads as the camera moving.
    /// </summary>
    public const float PropLength = 0.5f;

    static readonly Dictionary<System.Type, GameObject> Loaded = new Dictionary<System.Type, GameObject>();

    /// <summary>
    /// The model asset for a weapon, or null when it has none yet. Cached
    /// including the misses — a Resources.Load that fails is not free, and this
    /// is asked once per weapon per icon and once per weapon swap.
    /// </summary>
    public static GameObject ModelFor(Weapon weapon)
    {
        if (weapon == null)
            return null;

        var type = weapon.GetType();
        if (Loaded.TryGetValue(type, out var cached))
            return cached;

        var model = Resources.Load<GameObject>(ModelRoot + KeyFor(type));
        Loaded[type] = model;
        return model;
    }

    /// <summary>True when this weapon has a model of its own rather than the fallback.</summary>
    public static bool HasModel(Weapon weapon) => ModelFor(weapon) != null;

    /// <summary>
    /// The file name a weapon's model is expected under: PrismSplitter becomes
    /// "prism-splitter". Shared with the generator, which names its output the
    /// same way — see Tools/meshyweapons.py.
    /// </summary>
    public static string KeyFor(System.Type type)
    {
        string name = type.Name;
        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Build the visible prop for a weapon under <paramref name="parent"/>,
    /// normalized to <see cref="PropLength"/> down +Z and painted in the
    /// weapon's colour. Never returns null — a weapon with no model gets the
    /// fallback silhouette.
    /// </summary>
    public static GameObject BuildProp(Weapon weapon, Transform parent)
    {
        var model = ModelFor(weapon);
        Color tint = weapon != null ? weapon.color : new Color(0.2f, 0.9f, 1f);

        if (model == null)
            return BuildFallback(parent, tint);

        var instance = Object.Instantiate(model, parent);
        instance.name = "Prop";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        NormalizeAlongZ(instance);
        // No tint pass on a real model: it arrives with its own texture, and
        // that texture IS the weapon's identity once it exists. Emission is left
        // where the material has it — a blanket glow would wash the texture out,
        // and generated GLBs come in over-emissive already (repaired offline by
        // Tools/fixmeshymaterials.py, not here).
        return instance;
    }

    /// <summary>
    /// Turn a model's longest axis down +Z and scale it to
    /// <see cref="PropLength"/>, centred on its own bounds.
    ///
    /// Generated models do not agree on which way they point — the same problem
    /// VehicleSkin.FitToRobot solves for tanks — and a gun is by far longest
    /// along its barrel, so the long axis IS the barrel. Which END is the muzzle
    /// is still a coin flip; the generator is asked for barrel-forward models,
    /// and a weapon that comes out backwards is one 180 in its own prefab.
    /// </summary>
    static void NormalizeAlongZ(GameObject instance)
    {
        var bounds = Measure(instance);
        if (bounds.size.sqrMagnitude < 1e-6f)
            return;

        Vector3 size = bounds.size;
        // Rotate BEFORE measuring again: turning a model afterwards moves it off
        // the centring solved for its old orientation.
        if (size.x > size.z && size.x > size.y)
            instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        else if (size.y > size.z && size.y > size.x)
            instance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        bounds = Measure(instance);
        float longest = Mathf.Max(0.01f, bounds.size.z);
        instance.transform.localScale *= PropLength / longest;

        bounds = Measure(instance);
        Vector3 centre = instance.transform.parent != null
            ? instance.transform.parent.InverseTransformPoint(bounds.center)
            : bounds.center;
        instance.transform.localPosition -= centre;
    }

    static Bounds Measure(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        return RobotFactory.MeasureWorldBounds(renderers);
    }

    /// <summary>
    /// The stand-in gun: a body, a barrel and a glowing muzzle ring, in the
    /// weapon's colour. Deliberately one shape for every weapon — the colour and
    /// the name on the card are what tell them apart until the real models land,
    /// and a set of vaguely-different boxes would only look like a bug.
    /// </summary>
    static GameObject BuildFallback(Transform parent, Color tint)
    {
        var root = new GameObject("Prop");
        root.transform.SetParent(parent, false);

        var body = Block(root.transform, new Vector3(0f, 0f, -0.10f),
                         new Vector3(0.11f, 0.13f, 0.22f), new Color(0.10f, 0.11f, 0.14f));
        body.name = "Body";
        Block(root.transform, new Vector3(0f, 0.005f, 0.13f),
              new Vector3(0.055f, 0.055f, 0.26f), new Color(0.16f, 0.17f, 0.21f)).name = "Barrel";
        Block(root.transform, new Vector3(0f, -0.11f, -0.13f),
              new Vector3(0.07f, 0.14f, 0.09f), new Color(0.10f, 0.11f, 0.14f)).name = "Grip";

        // The one part that carries the weapon's identity, so it is the part
        // that glows: a lit ring at the muzzle rather than a tinted body, which
        // at these sizes just reads as a coloured brick.
        var muzzle = Block(root.transform, new Vector3(0f, 0.005f, 0.27f),
                           new Vector3(0.075f, 0.075f, 0.03f), tint);
        muzzle.name = "Muzzle";
        Glow(muzzle, tint);
        return root;
    }

    static GameObject Block(Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var collider = block.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.4f);
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
        return block;
    }

    /// <summary>
    /// Light the weapon's own colour into a prop's emission.
    ///
    /// Kept below the whiteout line on purpose: emission over about 2.5 blows
    /// through the bloom and every weapon's glow comes out white, which is the
    /// one thing that would make the rack unreadable — the colour is how you
    /// tell one gun from another in there.
    /// </summary>
    static void Glow(GameObject instance, Color tint)
    {
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.material;   // an instance, never the shared asset
            if (!material.HasProperty("_EmissionColor"))
                continue;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", tint * 1.6f);
        }
    }
}

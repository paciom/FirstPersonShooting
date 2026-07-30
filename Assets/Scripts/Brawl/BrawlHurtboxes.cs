using UnityEngine;

/// <summary>
/// Builds a fighter's hurtbox rig — trigger colliders on the skeleton — and
/// answers strike queries against it.
///
/// Colliders on BONES, not a column on the root: a crumpled, crouched or
/// kicking robot is hittable exactly where its body actually is (the fixed
/// capsule visibly disagreed with any non-standing pose). Torso and head
/// are Vital; limbs are graze. Cost is nothing that matters: two fighters,
/// a dozen trigger colliders each, and one small OverlapSphere per active
/// strike frame.
/// </summary>
public static class BrawlHurtboxes
{
    static readonly Collider[] QueryResults = new Collider[24];

    /// <summary>
    /// Attach the rig. Quietly does nothing when the model has no skeleton
    /// (the capsule-fallback robots) — the fighter then keeps the old
    /// column check.
    /// </summary>
    public static bool Build(BrawlFighter owner, GameObject model)
    {
        Transform Find(string name) => FindDeep(model.transform, name);

        var hips = Find("Hips");
        if (hips == null)
            return false;
        var neck = Find("Neck") ?? Find("Spine2") ?? Find("Spine");
        var head = Find("Head");

        // Torso: one capsule from the hips up through the chest. Vital.
        Capsule(owner, hips, neck, 0.26f, true, "torso");
        // Head: a sphere, slightly above the bone's pivot. Vital.
        if (head != null)
        {
            var sphere = head.gameObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            float scale = Mathf.Max(1e-4f, head.lossyScale.x);
            sphere.radius = 0.20f / scale;
            sphere.center = head.InverseTransformPoint(head.position + Vector3.up * 0.06f);
            Tag(owner, sphere, true, "head");
        }

        // Limbs: grazes. A punch into a guarding forearm sparks, not hurts.
        Capsule(owner, Find("RightArm"), Find("RightForeArm"), 0.11f, false, "arm");
        Capsule(owner, Find("RightForeArm"), Find("RightHand"), 0.10f, false, "arm");
        Capsule(owner, Find("LeftArm"), Find("LeftForeArm"), 0.11f, false, "arm");
        Capsule(owner, Find("LeftForeArm"), Find("LeftHand"), 0.10f, false, "arm");
        Capsule(owner, Find("RightUpLeg"), Find("RightLeg"), 0.16f, false, "leg");
        Capsule(owner, Find("RightLeg"), Find("RightFoot"), 0.13f, false, "leg");
        Capsule(owner, Find("LeftUpLeg"), Find("LeftLeg"), 0.16f, false, "leg");
        Capsule(owner, Find("LeftLeg"), Find("LeftFoot"), 0.13f, false, "leg");
        return true;
    }

    /// <summary>
    /// What a strike at this point touches on this fighter: the Vital part
    /// if any is in reach (damage beats graze when both overlap), else a
    /// graze part, else null.
    /// </summary>
    public static BrawlBodyPart Query(Vector3 point, float radius, BrawlFighter target)
    {
        int count = Physics.OverlapSphereNonAlloc(point, radius, QueryResults,
            ~0, QueryTriggerInteraction.Collide);
        BrawlBodyPart graze = null;
        for (int i = 0; i < count; i++)
        {
            var part = QueryResults[i].GetComponent<BrawlBodyPart>();
            if (part == null || part.Owner != target)
                continue;
            if (part.Vital)
                return part;
            graze = part;
        }
        return graze;
    }

    static void Capsule(BrawlFighter owner, Transform bone, Transform toward,
        float worldRadius, bool vital, string label)
    {
        if (bone == null || toward == null)
            return;
        float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
        Vector3 local = bone.InverseTransformPoint(toward.position);

        var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.center = local * 0.5f;
        capsule.direction = Dominant(local);
        capsule.height = local.magnitude + 2f * worldRadius / scale;
        capsule.radius = worldRadius / scale;
        Tag(owner, capsule, vital, label);
    }

    static int Dominant(Vector3 v)
    {
        float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
        return x > y && x > z ? 0 : y > z ? 1 : 2;
    }

    static void Tag(BrawlFighter owner, Collider collider, bool vital, string label)
    {
        var part = collider.gameObject.GetComponent<BrawlBodyPart>();
        if (part == null)
            part = collider.gameObject.AddComponent<BrawlBodyPart>();
        part.Owner = owner;
        part.Vital = part.Vital || vital;
        part.Label = part.Label ?? label;
        if (part.Label != label && vital)
            part.Label = label;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var hit = FindDeep(child, name);
            if (hit != null)
                return hit;
        }
        return null;
    }
}

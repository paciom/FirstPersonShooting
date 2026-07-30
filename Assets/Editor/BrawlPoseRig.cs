using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The posing harness BrawlMoveForge bakes through: joint lookup by the
/// Meshy/Mixamo names all nine rigs share, rest-pose capture/restore, pose
/// helpers that work in measured directions rather than assumed axes, and a
/// curve recorder.
///
/// The load-bearing idea: poses are applied to the LIVE Transform hierarchy
/// and the resulting local rotations are what gets recorded. The hierarchy
/// does all the frame math, so a template never needs to know a joint's
/// local axis convention — which differs between rigs (rigrest.py measured
/// up to 20° of rest-frame disagreement) and would make hand-authored local
/// curves robot-specific.
/// </summary>
public class BrawlPoseRig
{
    public Transform Root;
    public Transform Hips, Chest, Head;
    public Transform ArmR, ForeArmR, HandR, ArmL, ForeArmL, HandL;
    public Transform UpLegR, LegR, FootR, UpLegL, LegL, FootL;

    List<Transform> _recorded;
    Dictionary<Transform, Vector3> _restPosition;
    Dictionary<Transform, Quaternion> _restRotation;
    Dictionary<Transform, string> _paths;

    /// <summary>
    /// Null when the instance has no recognizable skeleton. The instance must
    /// be at the world origin with identity rotation — pose directions are
    /// authored in character space (+Z toward the opponent) and applied as
    /// world space.
    /// </summary>
    public static BrawlPoseRig Discover(GameObject instance)
    {
        var rig = new BrawlPoseRig { Root = instance.transform };
        rig.Hips = Find(instance.transform, "Hips");
        if (rig.Hips == null)
            return null;

        rig.Chest = Find(instance.transform, "Spine2")
                    ?? Find(instance.transform, "Spine1")
                    ?? Find(instance.transform, "Spine");
        rig.Head = Find(instance.transform, "Head");
        rig.ArmR = Find(instance.transform, "RightArm");
        rig.ForeArmR = Find(instance.transform, "RightForeArm");
        rig.HandR = Find(instance.transform, "RightHand");
        rig.ArmL = Find(instance.transform, "LeftArm");
        rig.ForeArmL = Find(instance.transform, "LeftForeArm");
        rig.HandL = Find(instance.transform, "LeftHand");
        rig.UpLegR = Find(instance.transform, "RightUpLeg");
        rig.LegR = Find(instance.transform, "RightLeg");
        rig.FootR = Find(instance.transform, "RightFoot");
        rig.UpLegL = Find(instance.transform, "LeftUpLeg");
        rig.LegL = Find(instance.transform, "LeftLeg");
        rig.FootL = Find(instance.transform, "LeftFoot");

        // Record the whole skeleton — the subtree that contains Hips —
        // including joints no pose touches: a constant curve on an untouched
        // joint pins it during transition blends instead of letting it drift
        // toward whatever the other state left there.
        Transform skeletonRoot = rig.Hips.parent != null ? rig.Hips.parent : rig.Hips;
        rig._recorded = new List<Transform>();
        Collect(skeletonRoot, rig._recorded);

        rig._restPosition = new Dictionary<Transform, Vector3>();
        rig._restRotation = new Dictionary<Transform, Quaternion>();
        rig._paths = new Dictionary<Transform, string>();
        foreach (var joint in rig._recorded)
        {
            rig._restPosition[joint] = joint.localPosition;
            rig._restRotation[joint] = joint.localRotation;
            rig._paths[joint] = PathTo(joint, rig.Root);
        }
        return rig;
    }

    static void Collect(Transform node, List<Transform> into)
    {
        into.Add(node);
        foreach (Transform child in node)
            Collect(child, into);
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var hit = Find(child, name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    static string PathTo(Transform joint, Transform root)
    {
        if (joint == root)
            return "";
        var parts = new List<string>();
        for (var node = joint; node != null && node != root; node = node.parent)
            parts.Add(node.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    public void RestoreRest()
    {
        foreach (var joint in _recorded)
        {
            joint.localPosition = _restPosition[joint];
            joint.localRotation = _restRotation[joint];
        }
    }

    // ------------------------------------------------------------- posing

    /// <summary>
    /// Swing <paramref name="joint"/> so the bone toward
    /// <paramref name="child"/> points along a character-space direction.
    /// Measured from where the bone IS, so it composes with rotations
    /// already applied further up the chain this frame.
    /// </summary>
    public void Aim(Transform joint, Transform child, Vector3 direction, float weight = 1f)
    {
        if (joint == null || child == null || weight == 0f)
            return;
        Vector3 current = child.position - joint.position;
        if (current.sqrMagnitude < 1e-8f)
            return;
        var delta = Quaternion.FromToRotation(current.normalized, direction.normalized);
        if (weight < 0f)
        {
            // Negative weight swings the bone AWAY from the target — the
            // anticipation frame of the same template.
            delta = Quaternion.Inverse(delta);
            weight = -weight;
        }
        if (weight < 1f)
            delta = Quaternion.Slerp(Quaternion.identity, delta, weight);
        joint.rotation = delta * joint.rotation;
    }

    /// <summary>World-axis rotation (character space, instance at identity).</summary>
    public void Rotate(Transform joint, Vector3 axis, float degrees, float weight = 1f)
    {
        if (joint == null)
            return;
        joint.rotation = Quaternion.AngleAxis(degrees * weight, axis) * joint.rotation;
    }

    /// <summary>World-space translation — Hips only, in practice.</summary>
    public void Shift(Transform joint, Vector3 delta, float weight = 1f)
    {
        if (joint == null)
            return;
        joint.position += delta * weight;
    }

    // ---------------------------------------------------------- recording

    public class Recorder
    {
        public readonly Dictionary<EditorCurveBinding, AnimationCurve> Curves =
            new Dictionary<EditorCurveBinding, AnimationCurve>();
    }

    public Recorder NewRecorder() => new Recorder();

    public void Record(Recorder recorder, float time)
    {
        foreach (var joint in _recorded)
        {
            string path = _paths[joint];
            var rotation = joint.localRotation;
            Key(recorder, path, "m_LocalRotation.x", time, rotation.x);
            Key(recorder, path, "m_LocalRotation.y", time, rotation.y);
            Key(recorder, path, "m_LocalRotation.z", time, rotation.z);
            Key(recorder, path, "m_LocalRotation.w", time, rotation.w);
            if (joint == Hips)
            {
                var position = joint.localPosition;
                Key(recorder, path, "m_LocalPosition.x", time, position.x);
                Key(recorder, path, "m_LocalPosition.y", time, position.y);
                Key(recorder, path, "m_LocalPosition.z", time, position.z);
            }
        }
    }

    void Key(Recorder recorder, string path, string property, float time, float value)
    {
        var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
        if (!recorder.Curves.TryGetValue(binding, out var curve))
        {
            curve = new AnimationCurve();
            recorder.Curves[binding] = curve;
        }
        curve.AddKey(new Keyframe(time, value));
    }

    public void Write(AnimationClip clip, Recorder recorder)
    {
        foreach (var pair in recorder.Curves)
            AnimationUtility.SetEditorCurve(clip, pair.Key, pair.Value);
    }
}

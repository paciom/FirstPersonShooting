using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Bakes the robot → ground-vehicle transformation clips onto the Meshy-rigged
/// walker prefabs.
///
/// WHY THIS SOLVES INSTEAD OF COPYING A CLIP. All nine robots came out of
/// Meshy's rigger with byte-identical joint PATHS (Tools/rigpaths.py proves it),
/// which is why one Animator layout fits the fleet. Their REST POSES, though,
/// disagree by up to 117 degrees (Tools/rigrest.py) — the auto-rigger orients
/// each bone to the mesh it found. An animation curve stores an ABSOLUTE local
/// rotation, so a clip hand-authored on ranger would fold titan inside out.
///
/// So the fold is authored once as a set of CHARACTER-SPACE AIMS ("the thigh
/// should point backward and down") and solved per robot into local rotations.
/// For each bone, parents first: take the direction the bone currently points
/// (bone → its named child, after ancestors have already folded), rotate that
/// onto the authored direction, and convert the result back to a local
/// rotation. Rest pose differences cancel out because nothing is copied — only
/// the destination is shared.
///
/// Three clips per robot: ToVehicle, a one-key Vehicle hold, and ToRobot (the
/// same solve, reversed). Every joint appears in every clip, including the ones
/// the fold does not move: an Animator state leaves bones it does not animate
/// wherever the previous state left them, so an unwritten toe would freeze
/// mid-stride for as long as the robot stayed a vehicle.
///
/// Run: menu "Photon Arena → Forge Robot Transform Clips" (Build Greybox Arena
/// also forges any that are missing).
/// </summary>
public static class TransformRigForge
{
    const string PrefabDir = "Assets/Models/Generated";
    const string AnimDir = "Assets/Animation";

    /// <summary>Sampling rate of the baked clips.</summary>
    const float FrameRate = 60f;

    /// <summary>
    /// How far the head shrinks as it retracts into the chassis. Scaling the
    /// bone is how the head "disappears" — there is no separate cockpit mesh to
    /// swap in, and a robot head left sitting on the nose of a tank reads as a
    /// bug rather than a design.
    /// </summary>
    const float HeadRetractScale = 0.45f;

    /// <summary>
    /// A bone and the child that defines which way it points, plus where that
    /// direction should end up in character space (+Z forward, +Y up). Bones are
    /// addressed by name because Meshy's names are unique within these rigs and
    /// a name survives a hierarchy change that a path would not.
    /// </summary>
    struct Aim
    {
        public string Bone;
        public string Child;
        public Vector3 Direction;

        public Aim(string bone, string child, float x, float y, float z)
        {
            Bone = bone;
            Child = child;
            Direction = new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// The vehicle: a low forward-pointing wedge. The torso pitches down to
    /// become the chassis, the thighs sweep back and the shins Z-fold underneath
    /// it, the arms clamp to the flanks with the hands thrown forward as front
    /// prongs, and the head retracts under the nose.
    ///
    /// Left-side entries are mirrored onto the right automatically, so the pose
    /// cannot drift out of symmetry.
    /// </summary>
    static readonly Aim[] VehicleAims =
    {
        // Spine: vertical column → horizontal chassis, with a slight nose-down rake.
        new Aim("Hips",    "Spine02", 0f,  0.25f,  0.97f),
        new Aim("Spine02", "Spine01", 0f,  0.12f,  0.99f),
        new Aim("Spine01", "Spine",   0f,  0.05f,  1.00f),

        // Head tips down and forward, ending up beneath the leading edge.
        new Aim("Spine", "neck", 0f, -0.15f, 0.99f),
        new Aim("neck",  "Head", 0f, -0.50f, 0.87f),
        new Aim("Head",  "head_end", 0f, -0.80f, 0.60f),

        // Arms clamp alongside the chassis; forearms point dead ahead.
        new Aim("LeftShoulder", "LeftArm",     -0.90f, -0.20f, 0.39f),
        new Aim("LeftArm",      "LeftForeArm", -0.55f, -0.35f, 0.76f),
        new Aim("LeftForeArm",  "LeftHand",    -0.30f, -0.15f, 0.94f),

        // Legs Z-fold: thigh back and down, shin forward and down, foot flat.
        new Aim("LeftUpLeg", "LeftLeg",     -0.15f, -0.25f, -0.95f),
        new Aim("LeftLeg",   "LeftFoot",     0.00f, -0.55f,  0.83f),
        new Aim("LeftFoot",  "LeftToeBase",  0.00f, -0.15f,  0.99f),
    };

    [MenuItem("Photon Arena/Forge Robot Transform Clips")]
    public static void ForgeAll() => Forge(true);

    /// <summary>Build-time hook: only forges robots that have no clips yet.</summary>
    public static void ForgeIfMissing() => Forge(false);

    static void Forge(bool rebuildExisting)
    {
        if (!Directory.Exists(PrefabDir))
            return;

        int forged = 0;
        foreach (string raw in Directory.GetFiles(PrefabDir, "*-robot.prefab"))
        {
            string prefabPath = raw.Replace('\\', '/');
            string robot = Path.GetFileNameWithoutExtension(prefabPath);
            robot = robot.Substring(0, robot.Length - "-robot".Length);
            string title = char.ToUpperInvariant(robot[0]) + robot.Substring(1);

            if (!rebuildExisting && !IsStale(title))
                continue;

            if (ForgeOne(robot, title, prefabPath))
                forged++;
        }

        if (forged > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TransformRigForge] Forged transform clips for {forged} robot(s).");
        }
    }

    /// <summary>
    /// Stale when the clips are missing OR the controller has lost its vehicle
    /// states. That second half matters: MeshyWalkerForge deletes and rebuilds
    /// the controller whenever a robot's source GLBs change, which silently
    /// strips the states patched in here. Checking only for the clip assets
    /// would leave a robot with a perfectly good fold it can never enter.
    /// </summary>
    static bool IsStale(string title)
    {
        foreach (string suffix in new[] { "_ToVehicle", "_Vehicle", "_ToRobot" })
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{title}{suffix}.anim") == null)
                return true;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AnimDir}/{title}.controller");
        if (controller == null)
            return false;   // no controller to patch; MeshyWalkerForge owns that
        foreach (var parameter in controller.parameters)
            if (parameter.name == TransformMode.VehicleParameter)
                return false;
        return true;
    }

    static bool ForgeOne(string robot, string title, string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return false;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            // Start from the pose the Animator actually idles in. The idle clip
            // writes absolute rotations, so reading the prefab's raw transforms
            // instead would leave the fold starting from a pose the robot is
            // never in — a visible snap on the first frame.
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{title}_Idle.anim");
            if (idle != null)
                idle.SampleAnimation(instance, 0f);

            var root = instance.transform;
            var bones = MapBones(root);
            if (!bones.ContainsKey("Hips"))
            {
                Debug.LogWarning($"[TransformRigForge] {robot}: no 'Hips' bone — not a Meshy humanoid rig, skipping.");
                return false;
            }

            var solved = Solve(root, bones, out Vector3 hipsOffset, out string failure);
            if (failure != null)
            {
                Debug.LogWarning($"[TransformRigForge] {robot}: {failure}");
                return false;
            }

            var hips = bones["Hips"];
            Vector3 hipsRest = hips.localPosition;
            Vector3 hipsVehicle = hipsRest + hipsOffset;

            var toVehicle = Bake($"{AnimDir}/{title}_ToVehicle.anim", root, bones, solved,
                hipsRest, hipsVehicle, forward: true, hold: false);
            var vehicle = Bake($"{AnimDir}/{title}_Vehicle.anim", root, bones, solved,
                hipsVehicle, hipsVehicle, forward: true, hold: true);
            var toRobot = Bake($"{AnimDir}/{title}_ToRobot.anim", root, bones, solved,
                hipsVehicle, hipsRest, forward: false, hold: false);

            PatchController($"{AnimDir}/{title}.controller", toVehicle, vehicle, toRobot);

            Debug.Log($"[TransformRigForge] {robot}: solved {solved.Count} bones, " +
                      $"hips drop {(-hipsOffset.y):0.###} (local units).");
            return true;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // ------------------------------------------------------------------ solving

    static Dictionary<string, Transform> MapBones(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name))
                map[t.name] = t;
        return map;
    }

    /// <summary>
    /// Turns the authored aims into local rotations for this specific rig.
    ///
    /// Everything is computed in ROOT space (the Animator's own space) rather
    /// than world space, so the result is independent of where the prefab
    /// happens to sit while forging.
    /// </summary>
    static Dictionary<Transform, Quaternion> Solve(
        Transform root, Dictionary<string, Transform> bones,
        out Vector3 hipsOffset, out string failure)
    {
        hipsOffset = Vector3.zero;
        failure = null;

        var solved = new Dictionary<Transform, Quaternion>();
        var newWorld = new Dictionary<Transform, Quaternion>();

        float facing = DetectFacing(root, bones);
        if (facing == 0f)
        {
            failure = "could not tell which way the rig faces (no foot/toe bones)";
            return solved;
        }

        // Parents before children: a bone's aim is measured after its ancestors
        // have already folded, so the arm follows the chest instead of fighting it.
        var aims = new List<Aim>();
        foreach (var aim in VehicleAims)
        {
            aims.Add(aim);
            if (aim.Bone.StartsWith("Left"))
                aims.Add(Mirror(aim));
        }
        aims.Sort((a, b) => Depth(bones, a.Bone).CompareTo(Depth(bones, b.Bone)));

        foreach (var aim in aims)
        {
            if (!bones.TryGetValue(aim.Bone, out var bone) ||
                !bones.TryGetValue(aim.Child, out var child))
                continue;
            if (child.parent != bone)
                continue;

            Quaternion parentWorld = WorldRotation(root, bone.parent, newWorld);
            Quaternion unchanged = parentWorld * bone.localRotation;

            Vector3 current = (unchanged * child.localPosition).normalized;
            if (current.sqrMagnitude < 0.5f)
                continue;   // zero-length bone: nothing to aim

            Vector3 target = aim.Direction;
            target.z *= facing;
            target.Normalize();

            Quaternion world = Quaternion.FromToRotation(current, target) * unchanged;
            newWorld[bone] = world;
            solved[bone] = Quaternion.Inverse(parentWorld) * world;
        }

        hipsOffset = GroundingOffset(root, bones, solved);
        return solved;
    }

    static Aim Mirror(Aim aim)
    {
        return new Aim(
            "Right" + aim.Bone.Substring("Left".Length),
            aim.Child.StartsWith("Left") ? "Right" + aim.Child.Substring("Left".Length) : aim.Child,
            -aim.Direction.x, aim.Direction.y, aim.Direction.z);
    }

    static int Depth(Dictionary<string, Transform> bones, string name)
    {
        if (!bones.TryGetValue(name, out var t))
            return int.MaxValue;
        int depth = 0;
        while (t.parent != null) { depth++; t = t.parent; }
        return depth;
    }

    /// <summary>
    /// +1 if the rig faces +Z, -1 if it faces -Z, read off the foot→toe bones.
    /// Meshy's rigger wants a +Z-facing input, but this is one assumption cheap
    /// enough to verify instead of trust — a flipped rig would fold the vehicle
    /// backwards, which looks deliberate enough to go unnoticed for a while.
    /// </summary>
    static float DetectFacing(Transform root, Dictionary<string, Transform> bones)
    {
        float sum = 0f;
        foreach (string side in new[] { "Left", "Right" })
        {
            if (!bones.TryGetValue($"{side}Foot", out var foot) ||
                !bones.TryGetValue($"{side}ToeBase", out var toe))
                continue;
            Vector3 delta = root.InverseTransformPoint(toe.position) -
                            root.InverseTransformPoint(foot.position);
            sum += delta.z;
        }
        return Mathf.Abs(sum) < 1e-5f ? 0f : Mathf.Sign(sum);
    }

    static Quaternion WorldRotation(Transform root, Transform bone,
        Dictionary<Transform, Quaternion> overrides)
    {
        if (bone == null || bone == root)
            return Quaternion.identity;
        if (overrides.TryGetValue(bone, out var stored))
            return stored;
        return WorldRotation(root, bone.parent, overrides) * bone.localRotation;
    }

    /// <summary>
    /// How far to move the Hips so the folded robot still rests on the floor.
    ///
    /// RobotFactory computes its feet-on-ground offset once, from the bind-pose
    /// bounds, and never revisits it — so a pose this different would leave the
    /// vehicle hovering or buried. Rather than hand-tuning a drop per robot,
    /// measure it: run forward kinematics over both poses, compare the lowest
    /// joint in each, and close the gap.
    /// </summary>
    static Vector3 GroundingOffset(Transform root, Dictionary<string, Transform> bones,
        Dictionary<Transform, Quaternion> solved)
    {
        var hips = bones["Hips"];
        float restLow = LowestJointY(root, hips, null);
        float vehicleLow = LowestJointY(root, hips, solved);

        // Convert the root-space correction into Hips-local units. The Armature
        // carries the glTF unit scale (0.01) and can carry a rotation too, so
        // this cannot be a straight assignment.
        Matrix4x4 parentToRoot = root.worldToLocalMatrix *
                                 (hips.parent != null ? hips.parent.localToWorldMatrix : Matrix4x4.identity);
        return parentToRoot.inverse.MultiplyVector(new Vector3(0f, restLow - vehicleLow, 0f));
    }

    static float LowestJointY(Transform root, Transform hips,
        Dictionary<Transform, Quaternion> solved)
    {
        Matrix4x4 hipsToRoot = root.worldToLocalMatrix *
                               (hips.parent != null ? hips.parent.localToWorldMatrix : Matrix4x4.identity);
        float lowest = float.MaxValue;
        Descend(hips, hipsToRoot, solved, ref lowest);
        return lowest;
    }

    static void Descend(Transform bone, Matrix4x4 parentToRoot,
        Dictionary<Transform, Quaternion> solved, ref float lowest)
    {
        Quaternion rotation = bone.localRotation;
        if (solved != null && solved.TryGetValue(bone, out var replacement))
            rotation = replacement;

        Matrix4x4 boneToRoot = parentToRoot *
            Matrix4x4.TRS(bone.localPosition, rotation, bone.localScale);
        lowest = Mathf.Min(lowest, boneToRoot.GetColumn(3).y);

        for (int i = 0; i < bone.childCount; i++)
            Descend(bone.GetChild(i), boneToRoot, solved, ref lowest);
    }

    // ------------------------------------------------------------------ baking

    /// <summary>
    /// Writes one clip. Rotations are stored as quaternion curves rather than
    /// euler ones: the thighs swing through more than 90 degrees, and euler
    /// curves that large pick their own way around the gimbal.
    /// </summary>
    static AnimationClip Bake(string path, Transform root, Dictionary<string, Transform> bones,
        Dictionary<Transform, Quaternion> solved,
        Vector3 hipsFrom, Vector3 hipsTo, bool forward, bool hold)
    {
        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(path),
            frameRate = FrameRate
        };

        int steps = hold ? 1 : Mathf.RoundToInt(TransformMode.FoldSeconds * FrameRate);
        var times = new float[steps + 1];
        var blends = new float[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 1f : (float)i / steps;
            times[i] = hold ? 0f : t * TransformMode.FoldSeconds;
            // Reversed clips ease the same way rather than playing backwards,
            // so unfolding has its own weight instead of looking like rewound tape.
            blends[i] = hold ? 1f : Ease(forward ? t : 1f - t);
        }

        // EVERY joint is written, not just the ones that move — see the class
        // comment. Unwritten bones keep whatever the previous state left behind.
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (bone == root)
                continue;
            string bonePath = AnimationUtility.CalculateTransformPath(bone, root);

            Quaternion rest = bone.localRotation;
            Quaternion target = solved != null && solved.TryGetValue(bone, out var q) ? q : rest;
            if (Quaternion.Dot(rest, target) < 0f)
                target = new Quaternion(-target.x, -target.y, -target.z, -target.w);

            var x = new AnimationCurve();
            var y = new AnimationCurve();
            var z = new AnimationCurve();
            var w = new AnimationCurve();
            for (int i = 0; i < times.Length; i++)
            {
                Quaternion value = Quaternion.SlerpUnclamped(rest, target, blends[i]);
                x.AddKey(times[i], value.x);
                y.AddKey(times[i], value.y);
                z.AddKey(times[i], value.z);
                w.AddKey(times[i], value.w);
            }

            SetCurve(clip, bonePath, "m_LocalRotation.x", x);
            SetCurve(clip, bonePath, "m_LocalRotation.y", y);
            SetCurve(clip, bonePath, "m_LocalRotation.z", z);
            SetCurve(clip, bonePath, "m_LocalRotation.w", w);
        }

        BakeHips(clip, root, bones, times, blends, hipsFrom, hipsTo);
        BakeHeadRetract(clip, root, bones, times, blends);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = hold;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // ------------------------------------------------------------- controller

    /// <summary>
    /// Adds Locomotion ⇄ vehicle to the controller MeshyWalkerForge built.
    ///
    /// The two forges deliberately stay split — that one owns walking, this one
    /// owns transforming — so this patches an existing controller in place and
    /// tears out its own previous states first to stay idempotent.
    ///
    /// There is no transition out of ToVehicle back to ToRobot: the clips are
    /// one-way bakes, so interrupting a fold halfway would jump the skeleton to
    /// the far end of the other clip. TransformMode queues the reversal until
    /// the fold lands instead.
    /// </summary>
    static void PatchController(string path, AnimationClip toVehicle, AnimationClip vehicle,
        AnimationClip toRobot)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
            return;

        var machine = controller.layers[0].stateMachine;

        AnimatorState locomotion = null;
        Vector3 anchor = Vector3.zero;
        foreach (var child in machine.states)
        {
            if (child.state.name == "Locomotion")
            {
                locomotion = child.state;
                anchor = child.position;   // graph layout lives on the slot, not the state
            }
            else if (child.state.name == "ToVehicle" || child.state.name == "VehicleMode" ||
                     child.state.name == "ToRobot")
            {
                machine.RemoveState(child.state);
            }
        }
        if (locomotion == null)
        {
            Debug.LogWarning($"[TransformRigForge] {path} has no Locomotion state — " +
                             "re-run Forge Meshy Walker Robots first.");
            return;
        }

        bool hasParameter = false;
        foreach (var parameter in controller.parameters)
            if (parameter.name == TransformMode.VehicleParameter)
                hasParameter = true;
        if (!hasParameter)
            controller.AddParameter(TransformMode.VehicleParameter, AnimatorControllerParameterType.Bool);

        var foldIn = machine.AddState("ToVehicle", anchor + new Vector3(280f, -90f, 0f));
        var driving = machine.AddState("VehicleMode", anchor + new Vector3(560f, -90f, 0f));
        var foldOut = machine.AddState("ToRobot", anchor + new Vector3(280f, 90f, 0f));
        foldIn.motion = toVehicle;
        driving.motion = vehicle;
        foldOut.motion = toRobot;

        Enter(locomotion, foldIn, AnimatorConditionMode.If);
        Chain(foldIn, driving);
        Enter(driving, foldOut, AnimatorConditionMode.IfNot);
        Chain(foldOut, locomotion);

        EditorUtility.SetDirty(controller);
    }

    /// <summary>Condition-driven transition: fires the moment the bool flips.</summary>
    static void Enter(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.06f;
        transition.AddCondition(mode, 0f, TransformMode.VehicleParameter);
    }

    /// <summary>Automatic hand-off once a fold clip has played out.</summary>
    static void Chain(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.hasFixedDuration = true;
        transition.duration = 0.04f;
    }

    static void BakeHips(AnimationClip clip, Transform root, Dictionary<string, Transform> bones,
        float[] times, float[] blends, Vector3 from, Vector3 to)
    {
        if (!bones.TryGetValue("Hips", out var hips))
            return;
        string path = AnimationUtility.CalculateTransformPath(hips, root);

        var x = new AnimationCurve();
        var y = new AnimationCurve();
        var z = new AnimationCurve();
        for (int i = 0; i < times.Length; i++)
        {
            Vector3 value = Vector3.LerpUnclamped(from, to, blends[i]);
            x.AddKey(times[i], value.x);
            y.AddKey(times[i], value.y);
            z.AddKey(times[i], value.z);
        }
        SetCurve(clip, path, "m_LocalPosition.x", x);
        SetCurve(clip, path, "m_LocalPosition.y", y);
        SetCurve(clip, path, "m_LocalPosition.z", z);
    }

    static void BakeHeadRetract(AnimationClip clip, Transform root,
        Dictionary<string, Transform> bones, float[] times, float[] blends)
    {
        if (!bones.TryGetValue("Head", out var head))
            return;
        string path = AnimationUtility.CalculateTransformPath(head, root);

        Vector3 rest = head.localScale;
        Vector3 target = rest * HeadRetractScale;

        var x = new AnimationCurve();
        var y = new AnimationCurve();
        var z = new AnimationCurve();
        for (int i = 0; i < times.Length; i++)
        {
            Vector3 value = Vector3.LerpUnclamped(rest, target, Mathf.Clamp01(blends[i]));
            x.AddKey(times[i], value.x);
            y.AddKey(times[i], value.y);
            z.AddKey(times[i], value.z);
        }
        SetCurve(clip, path, "m_LocalScale.x", x);
        SetCurve(clip, path, "m_LocalScale.y", y);
        SetCurve(clip, path, "m_LocalScale.z", z);
    }

    static void SetCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(
            clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }

    /// <summary>
    /// Ease-in, then a small overshoot that settles — the mechanical clunk at
    /// the end of the fold. Exceeds 1 on purpose; the bake slerps unclamped so
    /// the overshoot is a real rotation past the target, not a flattened curve.
    /// </summary>
    static float Ease(float t)
    {
        float s = t * t * (3f - 2f * t);        // ease in
        const float c1 = 0.9f, c3 = c1 + 1f;    // back-out
        float u = s - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}

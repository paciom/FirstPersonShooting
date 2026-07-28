using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Generates "blockbot-robot": a fully rigged, skinned, low-poly walker robot —
/// mesh, skeleton, bind poses, Idle/Walk/Run clips and an Animator controller —
/// entirely from script, the same way the arena itself is built.
///
/// NOT part of the roster any more: Meshy's rigging API turned out to handle
/// generated humanoids well (see MeshyWalkerForge), and those robots look far
/// better than blocks. This stays as the zero-credit fallback and as a worked
/// example of hand-authored gait curves — forge it from the menu if wanted.
///
/// Everything is rigid-skinned (one bone per vertex, weight 1) which is exactly
/// right for a hard-surface robot: no squashy deformation, and the joint gaps
/// are hidden by small "gem" blocks parented to the child bone.
///
/// Run: menu "Photon Arena → Forge Rigged Walker Robot", or batch
/// -executeMethod WalkerRigForge.ForgeBatch.
/// </summary>
public static class WalkerRigForge
{
    const string ModelDir = "Assets/Models/Generated";
    const string AnimDir = "Assets/Animation";
    // "blockbot", not "strider": the roster's STRIDER is now a Meshy-generated
    // rigged robot, and two forges writing the same prefab path would fight.
    const string RobotName = "blockbot-robot";

    // Submesh / material slots.
    const int Armor = 0;
    const int Glow = 1;
    const int Metal = 2;

    [MenuItem("Photon Arena/Forge Rigged Walker Robot")]
    public static void Forge()
    {
        EnsureFolder(ModelDir);
        EnsureFolder(AnimDir);

        var materials = new[]
        {
            LitMaterial("WalkerArmor", new Color(0.62f, 0.66f, 0.72f)),
            LitMaterial("WalkerGlow", new Color(0.05f, 0.05f, 0.06f), new Color(0.35f, 0.95f, 1f), 2f),
            LitMaterial("WalkerMetal", new Color(0.16f, 0.17f, 0.20f)),
        };

        var root = new GameObject(RobotName);
        try
        {
            var bones = BuildSkeleton(root.transform, out Transform rigRoot);
            var mesh = BuildMesh(root.transform, bones);

            string meshPath = $"{ModelDir}/{RobotName}-mesh.asset";
            ReplaceAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var skin = root.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            // Root bone sits at the origin, so localBounds (which Unity treats as
            // root-bone space) is simply the bind-pose mesh bounds.
            skin.rootBone = rigRoot;
            skin.sharedMaterials = materials;
            skin.updateWhenOffscreen = false;
            // Animation swings limbs outside the bind-pose bounds; pad so the
            // robot never pops out while a leg is mid-stride. Y is padded only
            // slightly because RobotFactory normalizes models by bounds height.
            var b = mesh.bounds;
            skin.localBounds = new Bounds(b.center,
                new Vector3(b.size.x * 1.7f, b.size.y * 1.05f, b.size.z * 2.4f));

            var clips = new[]
            {
                SaveClip(BuildIdleClip()),
                SaveClip(BuildGaitClip("Blockbot_Walk", Gait.Walk())),
                SaveClip(BuildGaitClip("Blockbot_Run", Gait.Run())),
            };

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildController(clips);
            animator.applyRootMotion = false;

            root.AddComponent<RobotLocomotion>();

            string prefabPath = $"{ModelDir}/{RobotName}.prefab";
            ReplaceAsset(prefabPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[WalkerRigForge] Forged {prefabPath}: {mesh.vertexCount} verts, " +
                      $"{bones.Length} bones, 3 clips (Idle/Walk/Run).");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>Batch entry point (-executeMethod WalkerRigForge.ForgeBatch).</summary>
    public static void ForgeBatch()
    {
        Forge();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Build-time hook: forge only if the prefab is absent, so a routine arena
    /// rebuild doesn't churn the asset GUIDs of a robot that already exists.
    /// </summary>
    public static void ForgeIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{RobotName}.prefab") == null)
            Forge();
    }

    // ---------------------------------------------------------------- skeleton

    // Bone indices, in the order they are added to the bones array.
    const int BHips = 0, BSpine = 1, BChest = 2, BHead = 3;
    const int BShoulderL = 4, BUpperArmL = 5, BLowerArmL = 6, BHandL = 7;
    const int BShoulderR = 8, BUpperArmR = 9, BLowerArmR = 10, BHandR = 11;
    const int BUpperLegL = 12, BLowerLegL = 13, BFootL = 14;
    const int BUpperLegR = 15, BLowerLegR = 16, BFootR = 17;

    /// <summary>
    /// Builds the bone hierarchy under <paramref name="root"/> in bind pose
    /// (feet on y=0, arms hanging straight down) and returns it in bone-index
    /// order. All bones keep identity rotation, so a bone's local X/Y/Z are the
    /// character's own axes — which is what makes the gait curves readable.
    /// </summary>
    static Transform[] BuildSkeleton(Transform root, out Transform rigRoot)
    {
        var bones = new Transform[18];

        rigRoot = Bone(root, "Root", Vector3.zero);
        var hips = Bone(rigRoot, "Hips", new Vector3(0f, 0.95f, 0f));
        bones[BHips] = hips;
        bones[BSpine] = Bone(hips, "Spine", new Vector3(0f, 1.15f, 0f));
        bones[BChest] = Bone(bones[BSpine], "Chest", new Vector3(0f, 1.35f, 0f));
        bones[BHead] = Bone(bones[BChest], "Head", new Vector3(0f, 1.60f, 0f));

        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            string suffix = s == 0 ? "L" : "R";
            int shoulder = s == 0 ? BShoulderL : BShoulderR;

            bones[shoulder] = Bone(bones[BChest], "Shoulder_" + suffix, new Vector3(0.24f * x, 1.48f, 0f));
            bones[shoulder + 1] = Bone(bones[shoulder], "UpperArm_" + suffix, new Vector3(0.40f * x, 1.44f, 0f));
            bones[shoulder + 2] = Bone(bones[shoulder + 1], "LowerArm_" + suffix, new Vector3(0.40f * x, 1.14f, 0f));
            bones[shoulder + 3] = Bone(bones[shoulder + 2], "Hand_" + suffix, new Vector3(0.40f * x, 0.88f, 0f));

            int upperLeg = s == 0 ? BUpperLegL : BUpperLegR;
            bones[upperLeg] = Bone(hips, "UpperLeg_" + suffix, new Vector3(0.17f * x, 0.90f, 0f));
            bones[upperLeg + 1] = Bone(bones[upperLeg], "LowerLeg_" + suffix, new Vector3(0.17f * x, 0.52f, 0f));
            bones[upperLeg + 2] = Bone(bones[upperLeg + 1], "Foot_" + suffix, new Vector3(0.17f * x, 0.14f, 0f));
        }
        return bones;
    }

    /// <summary>Creates a child bone at a character-space (root-space) position.</summary>
    static Transform Bone(Transform parent, string name, Vector3 characterSpacePosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = characterSpacePosition - CharacterSpace(parent);
        return go.transform;
    }

    /// <summary>Position of a transform in character space (root is the origin).</summary>
    static Vector3 CharacterSpace(Transform t)
    {
        var p = Vector3.zero;
        while (t != null && t.parent != null)
        {
            p += t.localPosition;
            t = t.parent;
        }
        return p;
    }

    // -------------------------------------------------------------------- mesh

    /// <summary>
    /// Assembles the robot out of blocks, each rigidly bound to one bone. All
    /// geometry is authored in character space (the same space the bind poses
    /// are computed in), so the numbers here read like a blueprint.
    /// </summary>
    static Mesh BuildMesh(Transform root, Transform[] bones)
    {
        var mb = new BlockMesh();

        // Pelvis / spine / chest.
        mb.Block(BHips, new Vector3(0f, 0.99f, 0f), new Vector3(0.42f, 0.24f, 0.30f), Armor);
        mb.Block(BHips, new Vector3(0f, 0.87f, 0f), new Vector3(0.46f, 0.10f, 0.32f), Metal);
        mb.Block(BSpine, new Vector3(0f, 1.16f, 0f), new Vector3(0.36f, 0.22f, 0.26f), Metal);
        mb.Block(BChest, new Vector3(0f, 1.36f, 0f), new Vector3(0.56f, 0.34f, 0.34f), Armor);
        mb.Block(BChest, new Vector3(0f, 1.36f, 0.18f), new Vector3(0.16f, 0.16f, 0.04f), Glow);
        mb.Block(BChest, new Vector3(0f, 1.36f, -0.20f), new Vector3(0.34f, 0.26f, 0.10f), Metal);
        mb.Block(BChest, new Vector3(0f, 1.19f, 0.14f), new Vector3(0.30f, 0.04f, 0.03f), Glow);

        // Head: skull, visor band, antenna.
        mb.Block(BHead, new Vector3(0f, 1.66f, 0f), new Vector3(0.30f, 0.26f, 0.30f), Armor);
        mb.Block(BHead, new Vector3(0f, 1.66f, 0.16f), new Vector3(0.24f, 0.08f, 0.03f), Glow);
        mb.Block(BHead, new Vector3(0f, 1.52f, 0f), new Vector3(0.20f, 0.06f, 0.22f), Metal);
        mb.Block(BHead, new Vector3(0.10f, 1.84f, 0f), new Vector3(0.03f, 0.16f, 0.03f), Metal);
        mb.Sphere(BHead, new Vector3(0.10f, 1.94f, 0f), 0.07f, Glow);

        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            int shoulder = s == 0 ? BShoulderL : BShoulderR;
            int upperArm = shoulder + 1, lowerArm = shoulder + 2, hand = shoulder + 3;
            int upperLeg = s == 0 ? BUpperLegL : BUpperLegR;
            int lowerLeg = upperLeg + 1, foot = upperLeg + 2;

            // Arm: pad, upper arm, elbow gem, forearm with a glow vent, fist.
            mb.Block(shoulder, new Vector3(0.33f * x, 1.48f, 0f), new Vector3(0.24f, 0.22f, 0.28f), Armor);
            mb.Block(upperArm, new Vector3(0.40f * x, 1.29f, 0f), new Vector3(0.13f, 0.30f, 0.14f), Armor);
            mb.Block(lowerArm, new Vector3(0.40f * x, 1.14f, 0f), new Vector3(0.15f, 0.13f, 0.15f), Metal);
            mb.Block(lowerArm, new Vector3(0.40f * x, 1.01f, 0f), new Vector3(0.12f, 0.24f, 0.13f), Metal);
            mb.Block(lowerArm, new Vector3(0.40f * x, 1.01f, 0.07f), new Vector3(0.05f, 0.16f, 0.02f), Glow);
            mb.Block(hand, new Vector3(0.40f * x, 0.83f, 0.01f), new Vector3(0.14f, 0.15f, 0.17f), Armor);

            // Leg: hip gem, thigh, knee gem, shin with light strip, foot.
            mb.Block(upperLeg, new Vector3(0.17f * x, 0.90f, 0f), new Vector3(0.19f, 0.18f, 0.20f), Metal);
            mb.Block(upperLeg, new Vector3(0.17f * x, 0.71f, 0f), new Vector3(0.17f, 0.34f, 0.20f), Armor);
            mb.Block(lowerLeg, new Vector3(0.17f * x, 0.52f, 0f), new Vector3(0.16f, 0.15f, 0.17f), Metal);
            mb.Block(lowerLeg, new Vector3(0.17f * x, 0.33f, 0f), new Vector3(0.15f, 0.32f, 0.17f), Armor);
            mb.Block(lowerLeg, new Vector3(0.17f * x, 0.33f, 0.09f), new Vector3(0.05f, 0.20f, 0.02f), Glow);
            mb.Block(foot, new Vector3(0.17f * x, 0.07f, 0.05f), new Vector3(0.19f, 0.14f, 0.34f), Metal);
            mb.Block(foot, new Vector3(0.17f * x, 0.03f, 0.20f), new Vector3(0.15f, 0.05f, 0.06f), Glow);
        }

        var mesh = mb.Build($"{RobotName}-mesh");
        var bindPoses = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            bindPoses[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
        mesh.bindposes = bindPoses;
        return mesh;
    }

    // --------------------------------------------------------------- animation

    /// <summary>Tunable shape of one locomotion cycle (a full two-step stride).</summary>
    struct Gait
    {
        public string name;
        public float length;      // seconds per stride
        public float thigh;       // hip swing amplitude, degrees
        public float knee;        // peak knee bend, degrees
        public float arm;         // shoulder swing, degrees
        public float elbow;       // constant elbow bend, degrees
        public float bob;         // hip rise/fall, metres
        public float lean;        // forward spine lean, degrees
        public float roll;        // hip roll, degrees
        public float twist;       // chest counter-twist, degrees

        public static Gait Walk()
        {
            return new Gait
            {
                name = "Blockbot_Walk", length = 0.9f, thigh = 26f, knee = 42f, arm = 18f,
                elbow = 14f, bob = 0.05f, lean = 4f, roll = 3f, twist = 5f,
            };
        }

        public static Gait Run()
        {
            return new Gait
            {
                name = "Blockbot_Run", length = 0.6f, thigh = 42f, knee = 78f, arm = 34f,
                elbow = 46f, bob = 0.09f, lean = 12f, roll = 4f, twist = 9f,
            };
        }
    }

    // Sign convention (all bones sit at identity in bind pose, so local axes are
    // the character's): +X rotation tips a bone's far end backward. Limbs point
    // down, so a leg swings FORWARD on negative X; the torso leans forward on
    // positive X.
    static AnimationClip BuildGaitClip(string name, Gait g)
    {
        var clip = new AnimationClip { name = name, frameRate = 30f };
        float twoPi = Mathf.PI * 2f;

        for (int s = 0; s < 2; s++)
        {
            string suffix = s == 0 ? "L" : "R";
            float phase = s == 0 ? 0f : 0.5f;   // legs half a stride apart
            string leg = $"Hips/UpperLeg_{suffix}";
            string shin = $"{leg}/LowerLeg_{suffix}";
            string foot = $"{shin}/Foot_{suffix}";
            string arm = $"Hips/Spine/Chest/Shoulder_{suffix}/UpperArm_{suffix}";
            string forearm = $"{arm}/LowerArm_{suffix}";

            Func<float, float> thigh = p => -g.thigh * Mathf.Sin((p + phase) * twoPi);
            // The knee only ever folds backward, peaking as the leg swings through.
            Func<float, float> knee = p => g.knee *
                (0.10f + 0.90f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos((p + phase) * twoPi)), 1.3f));
            // Keep the sole roughly level with the floor through the stride.
            Func<float, float> ankle = p => -(thigh(p) + knee(p)) * 0.45f;

            Rotate(clip, leg, g.length, thigh);
            Rotate(clip, shin, g.length, knee);
            Rotate(clip, foot, g.length, ankle);
            // Arms swing opposite their own leg.
            Rotate(clip, arm, g.length, p => g.arm * Mathf.Sin((p + phase) * twoPi));
            Rotate(clip, forearm, g.length, p => -g.elbow - g.elbow * 0.25f * Mathf.Sin((p + phase) * twoPi));
        }

        // Hips drop twice per stride (once per foot plant) and roll side to side;
        // the chest counter-twists so the walk doesn't look like a wind-up toy.
        Position(clip, "Hips", 'y', g.length,
            p => 0.95f - g.bob * 0.5f * (1f - Mathf.Cos(p * twoPi * 2f)));
        Rotate(clip, "Hips", g.length, null,
            p => g.twist * 0.5f * Mathf.Sin(p * twoPi),
            p => g.roll * Mathf.Sin(p * twoPi));
        Rotate(clip, "Hips/Spine", g.length, p => g.lean * 0.6f);
        Rotate(clip, "Hips/Spine/Chest", g.length,
            p => g.lean * 0.4f,
            p => -g.twist * Mathf.Sin(p * twoPi));
        // Head stays level-ish: cancel most of the torso lean.
        Rotate(clip, "Hips/Spine/Chest/Head", g.length,
            p => -g.lean * 0.7f + 1.5f * Mathf.Sin(p * twoPi * 2f));

        MakeLooping(clip);
        return clip;
    }

    /// <summary>Idle: a slow servo-breathing sway with a slight ready stance.</summary>
    static AnimationClip BuildIdleClip()
    {
        var clip = new AnimationClip { name = "Blockbot_Idle", frameRate = 30f };
        const float length = 3.2f;
        float twoPi = Mathf.PI * 2f;

        for (int s = 0; s < 2; s++)
        {
            string suffix = s == 0 ? "L" : "R";
            float phase = s == 0 ? 0f : 0.35f;
            string leg = $"Hips/UpperLeg_{suffix}";
            string shin = $"{leg}/LowerLeg_{suffix}";
            string arm = $"Hips/Spine/Chest/Shoulder_{suffix}/UpperArm_{suffix}";
            string forearm = $"{arm}/LowerArm_{suffix}";

            // +Z rotation swings a hanging arm toward +X, so the left arm flares
            // out on positive and the right on negative — elbows clear the hips.
            float armOut = s == 0 ? 5f : -5f;
            Rotate(clip, leg, length, p => -5f + 1.5f * Mathf.Sin((p + phase) * twoPi));
            Rotate(clip, shin, length, p => 9f + 2f * Mathf.Sin((p + phase) * twoPi));
            Rotate(clip, arm, length, p => 2f + 2.5f * Mathf.Sin((p + phase) * twoPi), null, p => armOut);
            Rotate(clip, forearm, length, p => -12f - 3f * Mathf.Sin((p + phase) * twoPi));
        }

        Position(clip, "Hips", 'y', length, p => 0.935f + 0.015f * Mathf.Sin(p * twoPi));
        Rotate(clip, "Hips/Spine", length, p => 2f + 1f * Mathf.Sin(p * twoPi));
        Rotate(clip, "Hips/Spine/Chest/Head", length,
            p => -2f - 1.5f * Mathf.Sin(p * twoPi),
            p => 6f * Mathf.Sin(p * twoPi));

        MakeLooping(clip);
        return clip;
    }

    static readonly Func<float, float> Flat = p => 0f;

    /// <summary>
    /// Writes a bone's rotation as euler curves. All three axes are always
    /// written — a partial euler curve set leaves Unity guessing at the missing
    /// axes and the rig snaps back to bind pose on those channels.
    /// </summary>
    static void Rotate(AnimationClip clip, string bonePath, float length,
        Func<float, float> x, Func<float, float> y = null, Func<float, float> z = null)
    {
        Curve(clip, bonePath, "localEulerAnglesRaw.x", Loop(x ?? Flat, length));
        Curve(clip, bonePath, "localEulerAnglesRaw.y", Loop(y ?? Flat, length));
        Curve(clip, bonePath, "localEulerAnglesRaw.z", Loop(z ?? Flat, length));
    }

    static void Position(AnimationClip clip, string bonePath, char axis, float length, Func<float, float> f)
    {
        Curve(clip, bonePath, $"localPosition.{axis}", Loop(f, length));
    }

    /// <summary>Bone paths are written relative to the Animator, under the Root bone.</summary>
    static void Curve(AnimationClip clip, string bonePath, string property, AnimationCurve curve)
    {
        clip.SetCurve("Root/" + bonePath, typeof(Transform), property, curve);
    }

    /// <summary>
    /// Samples <paramref name="f"/> (normalized cycle 0..1) into a seamless
    /// looping curve: the end key repeats the start value and both ends share
    /// the wrap-around slope, so there is no hitch at the loop point.
    /// </summary>
    static AnimationCurve Loop(Func<float, float> f, float length, int samples = 24)
    {
        var keys = new Keyframe[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float p = (float)i / samples;
            keys[i] = new Keyframe(p * length, i == samples ? f(0f) : f(p));
        }

        var curve = new AnimationCurve(keys);
        for (int i = 0; i <= samples; i++)
            curve.SmoothTangents(i, 0f);

        float step = length / samples;
        float wrapSlope = (f(1f / samples) - f(1f - 1f / samples)) / (2f * step);
        var first = curve[0];
        first.inTangent = first.outTangent = wrapSlope;
        curve.MoveKey(0, first);
        var last = curve[samples];
        last.inTangent = last.outTangent = wrapSlope;
        curve.MoveKey(samples, last);
        return curve;
    }

    static void MakeLooping(AnimationClip clip)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    static AnimationClip SaveClip(AnimationClip clip)
    {
        string path = $"{AnimDir}/{clip.name}.anim";
        ReplaceAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>1D blend tree on "Speed" (m/s), driven by RobotLocomotion.</summary>
    static AnimatorController BuildController(AnimationClip[] clips)
    {
        string path = $"{AnimDir}/Blockbot.controller";
        ReplaceAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter(RobotLocomotion.SpeedParameter, AnimatorControllerParameterType.Float);

        BlendTree tree;
        controller.CreateBlendTreeInController("Locomotion", out tree);
        tree.blendParameter = RobotLocomotion.SpeedParameter;
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips[0], 0f);     // idle
        tree.AddChild(clips[1], 3.5f);   // walk
        tree.AddChild(clips[2], 8f);     // run
        return controller;
    }

    // ------------------------------------------------------------------ assets

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    /// <summary>Deletes an existing asset so the forge is safely re-runnable.</summary>
    static void ReplaceAsset(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            AssetDatabase.DeleteAsset(path);
    }

    static Material LitMaterial(string name, Color baseColor, Color? emission = null, float intensity = 2f)
    {
        string path = $"Assets/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = mat == null;
        if (isNew)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = name };
        }

        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Smoothness", 0.55f);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // Bloom whiteout guard: emission above ~2.5 washes the colour out.
            mat.SetColor("_EmissionColor", emission.Value * intensity);
        }
        if (isNew)
            AssetDatabase.CreateAsset(mat, path);
        else
            EditorUtility.SetDirty(mat);
        return mat;
    }

    // -------------------------------------------------------------- mesh utils

    /// <summary>
    /// Accumulates rigidly-skinned blocks into one multi-submesh mesh. Shapes
    /// are stamped from Unity's built-in primitives, which guarantees correct
    /// winding, normals and UVs without hand-rolling a cube table.
    /// </summary>
    class BlockMesh
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<BoneWeight> _weights = new List<BoneWeight>();
        readonly List<int>[] _submeshes = { new List<int>(), new List<int>(), new List<int>() };

        static Mesh _cube, _sphere;

        public void Block(int bone, Vector3 center, Vector3 size, int submesh)
        {
            Stamp(Primitive(PrimitiveType.Cube, ref _cube), bone, center, size, submesh);
        }

        public void Sphere(int bone, Vector3 center, float diameter, int submesh)
        {
            Stamp(Primitive(PrimitiveType.Sphere, ref _sphere), bone, center, Vector3.one * diameter, submesh);
        }

        void Stamp(Mesh source, int bone, Vector3 center, Vector3 size, int submesh)
        {
            int offset = _vertices.Count;
            var verts = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var weight = new BoneWeight { boneIndex0 = bone, weight0 = 1f };

            for (int i = 0; i < verts.Length; i++)
            {
                _vertices.Add(center + Vector3.Scale(verts[i], size));
                // Non-uniform scale would skew normals on curved shapes, but the
                // sphere is only ever stamped uniformly, so scaling+normalizing
                // is exact here and keeps blocks flat-shaded.
                _normals.Add(new Vector3(normals[i].x / size.x, normals[i].y / size.y, normals[i].z / size.z)
                    .normalized);
                _uv.Add(i < uv.Length ? uv[i] : Vector2.zero);
                _weights.Add(weight);
            }

            var tris = source.triangles;
            var target = _submeshes[submesh];
            for (int i = 0; i < tris.Length; i++)
                target.Add(tris[i] + offset);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = _vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uv);
            mesh.boneWeights = _weights.ToArray();
            mesh.subMeshCount = _submeshes.Length;
            for (int i = 0; i < _submeshes.Length; i++)
                mesh.SetTriangles(_submeshes[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Borrows a built-in primitive's mesh (cached per shape).</summary>
        static Mesh Primitive(PrimitiveType type, ref Mesh cache)
        {
            if (cache != null)
                return cache;
            var temp = GameObject.CreatePrimitive(type);
            cache = temp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(temp);
            return cache;
        }
    }
}

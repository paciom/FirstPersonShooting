using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Turns a Meshy rigging result into a playable roster robot.
///
/// Meshy's rigging API (POST /openapi/v1/rigging) returns a skinned, textured
/// humanoid plus in-place "walking" and "running" GLBs sharing one skeleton.
/// Downloaded into Assets/Models/Meshy as &lt;name&gt;-rig / -walk / -run.glb, they
/// are three separate imports with no controller between them — this forge
/// stitches them into one prefab: the walk model's mesh, an Animator driving a
/// Speed blend tree (rest → walking → running), and RobotLocomotion to feed it.
///
/// The clips are CLONED into Assets/Animation rather than referenced in place:
/// imported clips come in with loopTime off, and settings written onto a
/// sub-asset of a .glb are lost on the next reimport.
///
/// Source files live in a subfolder on purpose — ArenaBuilder's roster scan of
/// Assets/Models is non-recursive, so the raw animation GLBs never show up as
/// three separate "robots" in the select screen; only the forged prefab does.
///
/// Run: menu "Photon Arena → Forge Meshy Walker Robots" (Build Greybox Arena
/// also forges any that are missing).
/// </summary>
public static class MeshyWalkerForge
{
    const string SourceDir = "Assets/Models/Meshy";
    const string OutDir = "Assets/Models/Generated";
    const string AnimDir = "Assets/Animation";

    // Blend thresholds in m/s, matched to CharacterMotor's walk/sprint speeds.
    const float WalkSpeed = 3.2f;
    const float RunSpeed = 7f;

    // The jump, template-baked from BrawlPoses onto each robot's own rig.
    // Meshy's rigging result ships walking and running and nothing else, so
    // there is no leap to adopt — and the alternative, freezing the walk mid
    // -stride and sliding it through the air, is what this replaces.
    const float JumpFps = 30f;
    const float LaunchTime = 0.20f;
    const float AirTime = 0.70f;    // looped for as long as the flight lasts
    const float LandTime = 0.32f;

    const string LaunchState = "JumpLaunch";
    const string AirState = "JumpAir";
    const string LandState = "JumpLand";

    [MenuItem("Photon Arena/Forge Meshy Walker Robots")]
    public static void ForgeAll()
    {
        Forge(true);
    }

    /// <summary>Build-time hook: only forges robots whose prefab doesn't exist yet.</summary>
    public static void ForgeIfMissing()
    {
        Forge(false);
    }

    static void Forge(bool rebuildExisting)
    {
        if (!Directory.Exists(SourceDir))
            return;

        EnsureFolder(OutDir);
        EnsureFolder(AnimDir);

        int forged = 0;
        foreach (var raw in Directory.GetFiles(SourceDir, "*-walk.glb"))
        {
            string walkPath = raw.Replace('\\', '/');
            string robot = Path.GetFileNameWithoutExtension(walkPath);
            robot = robot.Substring(0, robot.Length - "-walk".Length);
            string prefabPath = $"{OutDir}/{robot}-robot.prefab";

            if (!rebuildExisting && !IsStale(robot, prefabPath))
                continue;
            if (ForgeOne(robot, walkPath, prefabPath))
                forged++;
        }

        if (forged > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// True when the prefab is missing, older than its sources, or missing a
    /// clip it should have. That last case is the one that actually bites: a
    /// forge triggered while Unity was still importing a freshly downloaded
    /// rig sees only some of the .glb files and bakes an incomplete controller
    /// (typically walk standing in for run), which a plain existence check
    /// would then happily keep forever.
    /// </summary>
    static bool IsStale(string robot, string prefabPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            return true;

        string title = Title(robot);

        // A robot forged before the jump existed has a perfectly good prefab
        // and no way to leap in it — nothing about its GLB sources changed, so
        // only asking after the clips themselves catches it.
        foreach (string state in new[] { LaunchState, AirState, LandState })
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{title}_{state}.anim") == null)
                return true;

        var prefabWritten = File.GetLastWriteTimeUtc(prefabPath);
        foreach (var pair in new[] { ("walk", "_Walk"), ("run", "_Run"), ("rig", "_Idle") })
        {
            string source = $"{SourceDir}/{robot}-{pair.Item1}.glb";
            if (!File.Exists(source))
                continue;
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{title}{pair.Item2}.anim") == null)
                return true;
            if (File.GetLastWriteTimeUtc(source) > prefabWritten)
                return true;
        }
        return false;
    }

    static string Title(string robot)
    {
        return char.ToUpperInvariant(robot[0]) + robot.Substring(1);
    }

    static bool ForgeOne(string robot, string walkPath, string prefabPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(walkPath);
        if (model == null)
        {
            Debug.LogWarning($"[MeshyWalkerForge] {walkPath} did not import as a GameObject — skipping. " +
                             "Reimport it (glTFast) and run the forge again.");
            return false;
        }

        string title = Title(robot);
        var walk = CloneClip(FindClip($"{SourceDir}/{robot}-walk"), $"{AnimDir}/{title}_Walk.anim");
        var run = CloneClip(FindClip($"{SourceDir}/{robot}-run"), $"{AnimDir}/{title}_Run.anim");
        // The rigging result's own clip is a single-key rest pose — ideal idle.
        var idle = CloneClip(FindClip($"{SourceDir}/{robot}-rig"), $"{AnimDir}/{title}_Idle.anim");

        if (walk == null)
        {
            Debug.LogWarning($"[MeshyWalkerForge] No animation clip inside {walkPath} — skipping {robot}. " +
                             "Check the .glb importer's animationMethod is Mecanim (2).");
            return false;
        }
        if (idle == null)
            idle = SaveClip(new AnimationClip { name = title + "_Idle" }, $"{AnimDir}/{title}_Idle.anim");
        if (run == null)
        {
            if (File.Exists($"{SourceDir}/{robot}-run.glb"))
                Debug.LogWarning($"[MeshyWalkerForge] {robot}-run.glb exists but has no clip yet " +
                                 "(still importing?) — using the walk cycle for the run state. " +
                                 "Re-run the forge once the import settles.");
            run = walk;
        }

        var jump = BakeJump(model, title);
        var controller = BuildController($"{AnimDir}/{title}.controller", idle, walk, run, jump);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = $"{robot}-robot";

            // glTFast's Mecanim import already leaves an Animator on the root;
            // reuse it so we don't end up with two.
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;   // clips are in-place; the motor moves us

            if (instance.GetComponent<RobotLocomotion>() == null)
                instance.AddComponent<RobotLocomotion>();

            // Skinned characters get culled by their bind-pose bounds unless told
            // otherwise; a mid-stride limb outside them pops the whole robot.
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.updateWhenOffscreen = true;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        Debug.Log($"[MeshyWalkerForge] Forged {prefabPath} (idle/walk/run: " +
                  $"{idle.length:0.00}s / {walk.length:0.00}s / {run.length:0.00}s" +
                  $"{(jump.Count > 0 ? ", + jump" : ", NO JUMP")}).");
        return true;
    }

    /// <summary>
    /// Bakes the leap onto this robot's own skeleton from the kung-fu pose
    /// templates the Brawl fighters are built from — a jumping knee with the
    /// arms driving it, not a vertical slide.
    ///
    /// Baked here rather than adopted from a GLB because Meshy's rigging
    /// result ships exactly two motions, walking and running. Baked per robot
    /// rather than once because the templates are relative to each rig's own
    /// measured rest pose; that is the whole reason a template transfers
    /// between these skeletons at all (see BrawlPoseRig).
    ///
    /// Empty when the model is not a recognizable humanoid — the controller
    /// then simply has no jump states, and RobotLocomotion falls back to
    /// holding the legs still in the air.
    /// </summary>
    static Dictionary<string, AnimationClip> BakeJump(GameObject model, string title)
    {
        var clips = new Dictionary<string, AnimationClip>();
        var instance = Object.Instantiate(model);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = BrawlPoseRig.Discover(instance);
            if (rig == null)
            {
                Debug.LogWarning($"[MeshyWalkerForge] {title}: no 'Hips' bone — not a Meshy " +
                                 "humanoid rig, so no jump animation. It will still leap, " +
                                 "just without a pose.");
                return clips;
            }

            // These sources are raw Meshy exports at whatever size Meshy chose;
            // the templates' crouches and sinks are authored in metres against
            // a roster-height robot. Without this the same landing absorb is a
            // deep sink on a small export and invisible on a large one.
            float height = rig.MeasureHeight();
            if (height > 0.01f)
                rig.PoseScale = height / RobotFactory.NormalizedHeight;

            clips[LaunchState] = SaveClip(
                rig.BakeClip($"{title}_{LaunchState}", LaunchTime, false, JumpFps, BrawlPoses.JumpLaunch),
                $"{AnimDir}/{title}_{LaunchState}.anim");
            clips[AirState] = SaveClip(
                rig.BakeClip($"{title}_{AirState}", AirTime, true, JumpFps, BrawlPoses.JumpAir),
                $"{AnimDir}/{title}_{AirState}.anim");
            clips[LandState] = SaveClip(
                rig.BakeClip($"{title}_{LandState}", LandTime, false, JumpFps, BrawlPoses.JumpLand),
                $"{AnimDir}/{title}_{LandState}.anim");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
        return clips;
    }

    /// <summary>
    /// First AnimationClip inside an imported model, trying the .glb (textured,
    /// what the prefab uses) and falling back to the .fbx of the same name —
    /// both come from the same Meshy rig with identical bone paths, so the FBX
    /// clip is a drop-in if glTFast is set to import animations as Legacy/None.
    /// </summary>
    static AnimationClip FindClip(string modelPathWithoutExtension)
    {
        foreach (string extension in new[] { ".glb", ".fbx" })
        {
            string path = modelPathWithoutExtension + extension;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                continue;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                var clip = asset as AnimationClip;
                if (clip != null && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
        }
        return null;
    }

    /// <summary>
    /// Copies an imported clip into a standalone looping asset. Imported clips
    /// can't be edited in place (a reimport reverts them) and arrive with
    /// loopTime off, which would leave the robot frozen at the end of one stride.
    /// </summary>
    static AnimationClip CloneClip(AnimationClip source, string path)
    {
        if (source == null)
            return null;

        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path), frameRate = source.frameRate };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
            AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, path);
    }

    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>
    /// Same Speed blend tree contract as the script-forged strider, plus the
    /// jump — launch, a looping flight, and a landing — hung off the single
    /// Airborne bool.
    ///
    /// ONE BOOL, THREE STATES, and no jump trigger anywhere: how long a robot
    /// is off the ground is decided by physics (the motor's arc, or the width
    /// of the link a bot is crossing), never by a clip length. So the flight
    /// state loops until the flag clears, and the landing is entered by the
    /// flag clearing rather than by the launch running out. A one-shot leap
    /// clip would land early on a long jump and still be extending on a short
    /// one.
    ///
    /// NO ANY-STATE TRANSITIONS, unlike the Brawl controller next door. That
    /// one enters its moves on triggers, which are consumed on arrival; a bool
    /// is not, so an Any State entry on Airborne would re-enter the launch
    /// every frame of the flight and the robot would take off over and over
    /// without ever reaching the tuck. The jump is chained off Locomotion
    /// instead — which is also what keeps a folded tank driving off a ramp
    /// from being yanked out of its own vehicle state.
    /// </summary>
    static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip walk,
        AnimationClip run, Dictionary<string, AnimationClip> jump)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter(RobotLocomotion.SpeedParameter, AnimatorControllerParameterType.Float);

        BlendTree tree;
        controller.CreateBlendTreeInController("Locomotion", out tree);
        tree.blendParameter = RobotLocomotion.SpeedParameter;
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, WalkSpeed);
        tree.AddChild(run, RunSpeed);

        if (jump.Count == 3)
            AddJumpStates(controller, jump);
        return controller;
    }

    static void AddJumpStates(AnimatorController controller, Dictionary<string, AnimationClip> jump)
    {
        controller.AddParameter(RobotLocomotion.AirborneParameter, AnimatorControllerParameterType.Bool);

        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.defaultState;
        Vector3 anchor = Vector3.zero;
        foreach (var child in machine.states)
            if (child.state == locomotion)
                anchor = child.position;   // graph layout lives on the slot

        var launch = machine.AddState(LaunchState, anchor + new Vector3(300f, -180f, 0f));
        var air = machine.AddState(AirState, anchor + new Vector3(580f, -180f, 0f));
        var land = machine.AddState(LandState, anchor + new Vector3(300f, -280f, 0f));
        launch.motion = jump[LaunchState];
        air.motion = jump[AirState];
        land.motion = jump[LandState];

        // Feet leave the floor. Also straight out of the landing, so a robot
        // that jumps again during the 0.3 s absorb goes now rather than after
        // the recovery it has already abandoned.
        foreach (var from in new[] { locomotion, land })
        {
            var takeoff = from.AddTransition(launch);
            takeoff.AddCondition(AnimatorConditionMode.If, 0f, RobotLocomotion.AirborneParameter);
            takeoff.hasExitTime = false;
            takeoff.duration = 0.06f;
        }

        // Drive runs out while still up there: hold the tuck.
        var toAir = launch.AddTransition(air);
        toAir.AddCondition(AnimatorConditionMode.If, 0f, RobotLocomotion.AirborneParameter);
        toAir.hasExitTime = true;
        toAir.exitTime = 1f;
        toAir.duration = 0.10f;

        // Touchdown, from either half of the flight — a jump short enough to
        // end during the drive never reaches the tuck at all.
        foreach (var from in new[] { launch, air })
        {
            var touchdown = from.AddTransition(land);
            touchdown.AddCondition(AnimatorConditionMode.IfNot, 0f, RobotLocomotion.AirborneParameter);
            touchdown.hasExitTime = false;
            touchdown.duration = 0.08f;
        }

        var recover = land.AddTransition(locomotion);
        recover.hasExitTime = true;
        recover.exitTime = 1f;
        recover.duration = 0.12f;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

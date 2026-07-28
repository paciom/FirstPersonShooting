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

        var controller = BuildController($"{AnimDir}/{title}.controller", idle, walk, run);

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
                  $"{idle.length:0.00}s / {walk.length:0.00}s / {run.length:0.00}s).");
        return true;
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

    /// <summary>Same Speed blend tree contract as the script-forged strider.</summary>
    static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip walk, AnimationClip run)
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
        return controller;
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

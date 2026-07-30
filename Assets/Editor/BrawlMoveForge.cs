using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Bakes every roster robot's fight animations and Brawl animator controller.
///
/// HOW THE TEMPLATE BAKE WORKS. Fight moves are authored as POSES, not
/// curves: each move is "aim this bone chain at this character-space
/// direction" plus a weight envelope over time. The forge instantiates the
/// robot, applies the pose through the live Transform hierarchy
/// (Quaternion.FromToRotation from the bone's measured rest direction), and
/// records the resulting local rotations as clip curves. Because every
/// rotation is computed relative to the instance's OWN rest pose, the same
/// template bakes correctly onto any humanoid rig — old skeleton, re-rigged
/// skeleton, doesn't matter — which is what rigrest.py concluded a fleet
/// forge must do (absolute curves don't transfer between these rigs).
///
/// MESHY CLIPS. Where a robot has Fight/&lt;robot&gt;-walking.glb (the re-rig
/// pipeline, see Tools/meshyfight.py), the fighter prefab is built from that
/// model and its natural walk loop; the fight moves are template-baked onto
/// the SAME instance, so everything shares one skeleton by construction.
/// Meshy's strike clips (punch 7.3 s, kick 2.7 s…) are choreography
/// sequences, not frame-data pokes — they are only adopted for a move when
/// Tools/fight_trims.json names a [start,end] worth using, after a human has
/// actually looked at them. No trim entry, no adoption: the template holds.
///
/// Outputs, all runtime-loadable with zero scene serialization:
///   Assets/Resources/Brawl/&lt;robot&gt;.controller
///   Assets/Resources/Brawl/&lt;robot&gt;-fighter.prefab   (Meshy-track robots only)
///   Assets/Animation/Brawl_&lt;Robot&gt;_*.anim
///
/// Run: menu "Photon Arena → Forge Brawl Moves", or batch
/// -executeMethod BrawlMoveForge.ForgeBatch.
/// </summary>
public static class BrawlMoveForge
{
    const string GeneratedDir = "Assets/Models/Generated";
    const string FightDir = "Assets/Models/Meshy/Fight";
    const string AnimDir = "Assets/Animation";
    const string OutDir = "Assets/Resources/Brawl";
    const string TrimPath = "Tools/fight_trims.txt";

    const float Fps = 30f;

    /// <summary>
    /// Bumped whenever the pose templates change shape. BrawlPoses edits
    /// leave no source-file timestamp IsStale can see, so this version —
    /// written beside the controllers — is how a stale bake gets caught on
    /// the next Play. v2: full-body kung fu chains + the stance idle.
    /// </summary>
    const int TemplateVersion = 2;

    static string VersionPath => $"{OutDir}/forge_version.txt";

    static void WriteVersion()
    {
        File.WriteAllText(VersionPath, TemplateVersion.ToString());
        AssetDatabase.ImportAsset(VersionPath);
    }

    [MenuItem("Photon Arena/Forge Brawl Moves")]
    public static void ForgeAll()
    {
        int forged = 0;
        foreach (var raw in Directory.GetFiles(GeneratedDir, "*-robot.prefab"))
        {
            string prefabPath = raw.Replace('\\', '/');
            string robot = Path.GetFileNameWithoutExtension(prefabPath);
            robot = robot.Substring(0, robot.Length - "-robot".Length);
            if (robot == "blockbot")
                continue;   // the zero-credit fallback isn't on the roster
            if (ForgeOne(robot, prefabPath))
                forged++;
        }
        if (forged > 0)
            WriteVersion();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BrawlMoveForge] Forged {forged} robot(s).");
    }

    /// <summary>Batch entry point (-executeMethod BrawlMoveForge.ForgeBatch).</summary>
    public static void ForgeBatch()
    {
        ForgeAll();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Build-time hook (ArenaBuilder → every Play with auto-rebuild on):
    /// forges only robots whose Brawl controller is missing or older than
    /// their Meshy fight sources. Template changes (BrawlPoses edits) need
    /// the menu item — same deal as the other forges.
    /// </summary>
    public static void ForgeIfMissing()
    {
        int forged = 0;
        foreach (var raw in Directory.GetFiles(GeneratedDir, "*-robot.prefab"))
        {
            string prefabPath = raw.Replace('\\', '/');
            string robot = Path.GetFileNameWithoutExtension(prefabPath);
            robot = robot.Substring(0, robot.Length - "-robot".Length);
            if (robot == "blockbot" || !IsStale(robot))
                continue;
            if (ForgeOne(robot, prefabPath))
                forged++;
        }
        if (forged > 0)
        {
            WriteVersion();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    static bool IsStale(string robot)
    {
        // A template-version mismatch outranks every per-file check: the
        // clips on disk were baked by poses that no longer exist.
        if (!File.Exists(VersionPath)
            || File.ReadAllText(VersionPath).Trim() != TemplateVersion.ToString())
            return true;

        string controllerPath = $"{OutDir}/{robot}.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null)
            return true;
        var built = File.GetLastWriteTimeUtc(controllerPath);

        string meshyWalk = $"{FightDir}/{robot}-walking.glb";
        if (File.Exists(meshyWalk))
        {
            // The Meshy track exists: stale when the sources are newer or the
            // fighter prefab hasn't been built from them yet.
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/{robot}-fighter.prefab") == null)
                return true;
            foreach (var source in Directory.GetFiles(FightDir, $"{robot}-*.glb"))
                if (File.GetLastWriteTimeUtc(source) > built)
                    return true;
        }
        if (File.Exists(TrimPath) && File.GetLastWriteTimeUtc(TrimPath) > built)
            return true;
        return false;
    }

    static bool ForgeOne(string robot, string walkerPrefabPath)
    {
        string title = char.ToUpperInvariant(robot[0]) + robot.Substring(1);
        EnsureFolder(OutDir);
        EnsureFolder(AnimDir);

        // Meshy-track when the re-rigged walking model exists: the fighter
        // prefab, its walk and its idle all come from the new skeleton.
        string meshyWalk = $"{FightDir}/{robot}-walking.glb";
        bool meshyTrack = AssetDatabase.LoadAssetAtPath<GameObject>(meshyWalk) != null;

        GameObject source = meshyTrack
            ? AssetDatabase.LoadAssetAtPath<GameObject>(meshyWalk)
            : AssetDatabase.LoadAssetAtPath<GameObject>(walkerPrefabPath);
        if (source == null)
        {
            Debug.LogWarning($"[BrawlMoveForge] {robot}: no source model — skipping.");
            return false;
        }

        // The walk is the one clip not baked from templates: Meshy's natural
        // loop on the re-rigged track, the FPS walker's forged cycle otherwise.
        AnimationClip walk = meshyTrack
            ? CloneClip(FindClip(meshyWalk), $"{AnimDir}/Brawl_{title}_Walk.anim", true)
            : AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{title}_Walk.anim");
        if (walk == null)
        {
            Debug.LogWarning($"[BrawlMoveForge] {robot}: no walk clip (import settled?) — skipping.");
            return false;
        }

        // ---- template bake against this robot's own rig ----
        // The stance is baked here too and becomes the blend-tree idle: a
        // fighter at rest holds a bladed guard, not the rig's mannequin rest
        // pose — and every move clip starts and ends in that same stance, so
        // transitions land instead of snapping.
        var instance = Object.Instantiate(source);
        Dictionary<string, AnimationClip> moves;
        AnimationClip stance;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = BrawlPoseRig.Discover(instance);
            if (rig == null)
            {
                Debug.LogWarning($"[BrawlMoveForge] {robot}: rig joints not found — skipping.");
                return false;
            }
            stance = Bake(rig, $"Brawl_{title}_Stance", 1.6f, true, BrawlPoses.Stance);
            moves = BakeMoves(rig, title);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        // ---- adopted Meshy strike clips, only where a human blessed a trim ----
        foreach (var pair in Trims(robot))
        {
            var clip = CloneTrimmed($"{FightDir}/{robot}-{pair.Key}.glb",
                $"{AnimDir}/Brawl_{title}_{pair.Key}_meshy.anim", pair.Value);
            if (clip != null)
                moves[MoveStateName(pair.Key)] = clip;
        }

        var controller = BuildController($"{OutDir}/{robot}.controller", stance, walk, moves);

        if (meshyTrack)
            SaveFighterPrefab(robot, source, controller);
        else if (AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/{robot}-fighter.prefab") != null)
            AssetDatabase.DeleteAsset($"{OutDir}/{robot}-fighter.prefab");

        Debug.Log($"[BrawlMoveForge] {robot}: {(meshyTrack ? "meshy" : "walker")} track, " +
                  $"{moves.Count} move clips.");
        return true;
    }

    // ------------------------------------------------------------- move bake

    /// <summary>One entry per animator state; times from BrawlMoveSet.</summary>
    static Dictionary<string, AnimationClip> BakeMoves(BrawlPoseRig rig, string title)
    {
        var punch = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch].Duration;
        var kick = BrawlMoveSet.Table[BrawlMoveSet.Move.Kick].Duration;
        var fly = BrawlMoveSet.Table[BrawlMoveSet.Move.FlyKick];
        var blast = BrawlMoveSet.Table[BrawlMoveSet.Move.Blast].Duration;

        return new Dictionary<string, AnimationClip>
        {
            [BrawlAnim.Punch] = Bake(rig, $"Brawl_{title}_Punch", punch, false, BrawlPoses.Punch),
            [BrawlAnim.Kick] = Bake(rig, $"Brawl_{title}_Kick", kick, false, BrawlPoses.Kick),
            [BrawlAnim.FlyKick] = Bake(rig, $"Brawl_{title}_FlyKick",
                fly.startup + 0.35f + fly.recover, false, BrawlPoses.FlyKick),
            [BrawlAnim.Block] = Bake(rig, $"Brawl_{title}_Block",
                BrawlMoveSet.BlockClipTime, true, BrawlPoses.Block),
            [BrawlAnim.Hit] = Bake(rig, $"Brawl_{title}_Hit",
                BrawlMoveSet.HitClipTime, false, BrawlPoses.Hit),
            [BrawlAnim.Knockdown] = Bake(rig, $"Brawl_{title}_Knockdown",
                BrawlMoveSet.KnockdownClipTime, false, BrawlPoses.Knockdown),
            [BrawlAnim.Victory] = Bake(rig, $"Brawl_{title}_Victory",
                BrawlMoveSet.VictoryClipTime, true, BrawlPoses.Victory),
            [BrawlAnim.Blast] = Bake(rig, $"Brawl_{title}_Blast", blast, false, BrawlPoses.Blast),
        };
    }

    static AnimationClip Bake(BrawlPoseRig rig, string name, float duration, bool loop,
        System.Action<BrawlPoseRig, float> pose)
    {
        var clip = new AnimationClip { name = name, frameRate = Fps };
        int samples = Mathf.Max(2, Mathf.CeilToInt(duration * Fps) + 1);

        var curves = rig.NewRecorder();
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1) * duration;
            rig.RestoreRest();
            pose(rig, t / duration);
            rig.Record(curves, t);
        }
        rig.RestoreRest();
        rig.Write(clip, curves);
        clip.EnsureQuaternionContinuity();

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, $"{AnimDir}/{name}.anim");
    }

    // -------------------------------------------------------- clip plumbing

    static AnimationClip FindClip(string modelPath)
    {
        if (!modelPath.EndsWith(".glb"))
            modelPath += ".glb";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
            return null;
        foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
        {
            var clip = asset as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__"))
                return clip;
        }
        return null;
    }

    static AnimationClip CloneClip(AnimationClip source, string path, bool loop)
    {
        if (source == null)
            return null;
        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(path),
            frameRate = source.frameRate,
        };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
            AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, path);
    }

    /// <summary>A Meshy sequence cut down to the [start,end] worth keeping.</summary>
    static AnimationClip CloneTrimmed(string modelPath, string path, Vector2 range)
    {
        var source = FindClip(modelPath);
        if (source == null)
            return null;
        var clip = CloneClip(source, path, false);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = range.x;
        settings.stopTime = Mathf.Min(range.y, source.length);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>
    /// Tools/fight_trims.txt, one blessing per line: "ranger punch 2.10 2.60"
    /// — robot, move, clip seconds in, clip seconds out. # comments allowed.
    /// The file only exists once a human has watched a Meshy sequence and
    /// picked the segment worth keeping; absent file means templates hold.
    /// </summary>
    static Dictionary<string, Vector2> Trims(string robot)
    {
        var result = new Dictionary<string, Vector2>();
        if (!File.Exists(TrimPath))
            return result;
        foreach (var raw in File.ReadAllLines(TrimPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != robot)
                continue;
            if (float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float start)
                && float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float stop))
                result[parts[1]] = new Vector2(start, stop);
        }
        return result;
    }

    static string MoveStateName(string trimKey)
    {
        switch (trimKey)
        {
            case "punch": return BrawlAnim.Punch;
            case "kick": case "highkick": return BrawlAnim.Kick;
            case "flykick": return BrawlAnim.FlyKick;
            case "block": return BrawlAnim.Block;
            case "hit": return BrawlAnim.Hit;
            case "knockdown": return BrawlAnim.Knockdown;
            case "victory": return BrawlAnim.Victory;
            case "blast": return BrawlAnim.Blast;
            default: return trimKey;
        }
    }

    // ---------------------------------------------------------- controller

    static AnimatorController BuildController(string path, AnimationClip idle,
        AnimationClip walk, Dictionary<string, AnimationClip> moves)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter(BrawlAnim.Speed, AnimatorControllerParameterType.Float);
        controller.AddParameter(BrawlAnim.Block, AnimatorControllerParameterType.Bool);
        foreach (var trigger in new[] { BrawlAnim.Punch, BrawlAnim.Kick, BrawlAnim.FlyKick,
                 BrawlAnim.Hit, BrawlAnim.Knockdown, BrawlAnim.KO, BrawlAnim.Victory, BrawlAnim.Blast })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        var machine = controller.layers[0].stateMachine;

        BlendTree tree;
        controller.CreateBlendTreeInController("Locomotion", out tree);
        tree.blendParameter = BrawlAnim.Speed;
        tree.useAutomaticThresholds = false;
        if (idle != null)
            tree.AddChild(idle, 0f);
        tree.AddChild(walk, BrawlMoveSet.WalkSpeed);
        var locomotion = machine.defaultState;

        // One state per move, entered from Any State by trigger, back to
        // Locomotion when the clip runs out. KO never comes back — the round
        // is over and the match flow owns what happens next.
        foreach (var pair in moves)
        {
            if (pair.Key == BrawlAnim.Block)
                continue;   // block is a held bool, wired below
            var state = machine.AddState(pair.Key);
            state.motion = pair.Value;
            var enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, pair.Key);
            enter.duration = 0.05f;
            enter.hasExitTime = false;
            enter.canTransitionToSelf = false;
            if (pair.Key != BrawlAnim.Knockdown)
            {
                var exit = state.AddTransition(locomotion);
                exit.hasExitTime = true;
                exit.exitTime = 1f;
                exit.duration = 0.15f;
            }

            // KO plays the knockdown fall and stays on the floor: same clip,
            // separate state, no way out.
            if (pair.Key == BrawlAnim.Knockdown)
            {
                var getUp = state.AddTransition(locomotion);
                getUp.hasExitTime = true;
                getUp.exitTime = 1f;
                getUp.duration = BrawlMoveSet.GetUpTime;

                var ko = machine.AddState(BrawlAnim.KO);
                ko.motion = pair.Value;
                var koEnter = machine.AddAnyStateTransition(ko);
                koEnter.AddCondition(AnimatorConditionMode.If, 0f, BrawlAnim.KO);
                koEnter.duration = 0.05f;
                koEnter.hasExitTime = false;
                koEnter.canTransitionToSelf = false;
            }
        }

        if (moves.TryGetValue(BrawlAnim.Block, out var blockClip))
        {
            var block = machine.AddState(BrawlAnim.Block);
            block.motion = blockClip;
            var enter = machine.AddAnyStateTransition(block);
            enter.AddCondition(AnimatorConditionMode.If, 0f, BrawlAnim.Block);
            enter.duration = 0.05f;
            enter.hasExitTime = false;
            enter.canTransitionToSelf = false;
            var exit = block.AddTransition(locomotion);
            exit.AddCondition(AnimatorConditionMode.IfNot, 0f, BrawlAnim.Block);
            exit.hasExitTime = false;
            exit.duration = 0.1f;
        }

        return controller;
    }

    // -------------------------------------------------------------- prefab

    static void SaveFighterPrefab(string robot, GameObject source, AnimatorController controller)
    {
        string path = $"{OutDir}/{robot}-fighter.prefab";
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            instance.name = $"{robot}-fighter";
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // The marker RobotFactory.InstantiateNormalized reads to sit the
            // model's FEET at the character root instead of centring it like
            // a hovering robot. BrawlFighter disables the component itself —
            // its presence is the contract, not its Update.
            if (instance.GetComponent<RobotLocomotion>() == null)
                instance.AddComponent<RobotLocomotion>();

            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.updateWhenOffscreen = true;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
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

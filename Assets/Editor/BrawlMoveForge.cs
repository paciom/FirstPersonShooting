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
    /// v3: curve-level trim adoption + gameplay-window speed scaling.
    /// v4: reaction knockdown + crouch-through get-up + whole adoption.
    /// v5: reactions play in place — the root owns all knockdown travel.
    /// v6: the in-place pin is the shared REST pose, not per-clip frame 0.
    /// v7: the pin happens DURING the clone — post-CreateAsset curve edits
    ///     were silently lost on save, so v5/v6 shipped unpinned clips.
    /// v8: punch/kick variant states (jab, hook, uppercut, elbow, high,
    ///     side, low, spin) — one button, many moves.
    /// v9: EVERY adopted clip pins in place (strikes included; fly kick
    ///     pins Y too) — Variant.lunge moves the root instead. Ends the
    ///     mid-move body drift that snapped home at every state seam.
    /// v12: the guard actually guards — elbows tucked to the ribs, forearms
    ///     upright at the chin instead of high over the head.
    /// </summary>
    const int TemplateVersion = 12;

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
        Vector3 restHips;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = BrawlPoseRig.Discover(instance);
            if (rig == null)
            {
                Debug.LogWarning($"[BrawlMoveForge] {robot}: rig joints not found — skipping.");
                return false;
            }
            restHips = rig.RestHipsPosition;
            stance = Bake(rig, $"Brawl_{title}_Stance", 1.6f, true, BrawlPoses.Stance);
            moves = BakeMoves(rig, title);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        // ---- adopted Meshy clips ----
        // Reactions, rises and loops are complete motions: adopt whole
        // whenever the GLB is on disk (first candidate wins; blownback
        // beats the plain knockdown because the body travels). Strike
        // ROUTINES stay behind fight_trims.txt — they need a strike window.
        // EVERY adopted clip plays in place — the root owns all travel.
        // Strikes were exempt once ("short windows, small drift") and the
        // traveling moves proved that wrong: a Lunge Spin Kick's slice
        // carries the body a metre off the root, then the next state snaps
        // it home — the teleport. Variant.lunge moves the ROOT instead.
        AdoptWhole(robot, title, moves, BrawlAnim.Knockdown, false, restHips, HipsPin.Horizontal, "blownback", "knockdown");
        AdoptWhole(robot, title, moves, BrawlAnim.GetUp, false, restHips, HipsPin.Horizontal, "getup", "getup2");
        AdoptWhole(robot, title, moves, BrawlAnim.Hit, false, restHips, HipsPin.Horizontal, "hit");
        AdoptWhole(robot, title, moves, BrawlAnim.Block, true, restHips, HipsPin.Horizontal, "block");
        AdoptWhole(robot, title, moves, BrawlAnim.Victory, true, restHips, HipsPin.Horizontal, "victory");
        var meshyStance = AdoptClip(robot, title, "stance", true, restHips, HipsPin.Horizontal);
        if (meshyStance != null)
            stance = meshyStance;

        // Trim entries win over whole adoption: they exist because a human
        // (or the velocity analyzer) chose better. The fly kick pins Y as
        // well — its flight comes from the motor's jump arc, and a clip
        // that also flies doubles the height then drops at the seam.
        foreach (var pair in Trims(robot))
        {
            string state = MoveStateName(pair.Key);
            var clip = CloneTrimmed($"{FightDir}/{robot}-{pair.Key}.glb",
                $"{AnimDir}/Brawl_{title}_{pair.Key}_meshy.anim", pair.Value, restHips,
                state == BrawlAnim.FlyKick ? HipsPin.Full : HipsPin.Horizontal);
            if (clip != null)
                moves[state] = clip;
        }

        // What actually plays comes off the DISK, so verify the saved
        // assets: any adopted clip that still drifts means teleporting.
        foreach (var pair in moves)
            if (pair.Value != null && pair.Value.name.EndsWith("_meshy"))
                WarnIfDrifting(robot, pair.Key, pair.Value);

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

        var moves = new Dictionary<string, AnimationClip>();
        // Every variant of a family bakes to the family's duration — one
        // button, many faces, identical frame data.
        foreach (var variant in BrawlMoveSet.PunchVariants)
            moves[variant.trigger] = Bake(rig, $"Brawl_{title}_{variant.trigger}",
                punch, false, TemplatePose(variant.trigger));
        foreach (var variant in BrawlMoveSet.KickVariants)
            moves[variant.trigger] = Bake(rig, $"Brawl_{title}_{variant.trigger}",
                kick, false, TemplatePose(variant.trigger));

        moves[BrawlAnim.FlyKick] = Bake(rig, $"Brawl_{title}_FlyKick",
            fly.startup + 0.35f + fly.recover, false, BrawlPoses.FlyKick);
        moves[BrawlAnim.Block] = Bake(rig, $"Brawl_{title}_Block",
            BrawlMoveSet.BlockClipTime, true, BrawlPoses.Block);
        moves[BrawlAnim.Hit] = Bake(rig, $"Brawl_{title}_Hit",
            BrawlMoveSet.HitClipTime, false, BrawlPoses.Hit);
        moves[BrawlAnim.Knockdown] = Bake(rig, $"Brawl_{title}_Knockdown",
            BrawlMoveSet.KnockdownClipTime, false, BrawlPoses.Knockdown);
        moves[BrawlAnim.GetUp] = Bake(rig, $"Brawl_{title}_GetUp",
            BrawlMoveSet.GetUpTime, false, BrawlPoses.GetUp);
        moves[BrawlAnim.Victory] = Bake(rig, $"Brawl_{title}_Victory",
            BrawlMoveSet.VictoryClipTime, true, BrawlPoses.Victory);
        moves[BrawlAnim.Blast] = Bake(rig, $"Brawl_{title}_Blast", blast, false, BrawlPoses.Blast);
        return moves;
    }

    static System.Action<BrawlPoseRig, float> TemplatePose(string trigger)
    {
        switch (trigger)
        {
            case "Punch": return BrawlPoses.Punch;
            case "PunchJab": return BrawlPoses.Jab;
            case "PunchHook": return BrawlPoses.Hook;
            case "PunchUppercut": return BrawlPoses.Uppercut;
            case "PunchElbow": return BrawlPoses.Elbow;
            case "PunchBackfist": return BrawlPoses.Backfist;
            case "PunchHammer": return BrawlPoses.Hammerfist;
            case "PunchPalm": return BrawlPoses.PalmStrike;
            case "PunchChop": return BrawlPoses.Chop;
            case "Kick": return BrawlPoses.Kick;
            case "KickHigh": return BrawlPoses.KickHigh;
            case "KickSide": return BrawlPoses.KickSide;
            case "KickLow": return BrawlPoses.KickLow;
            case "KickSpin": return BrawlPoses.KickSpin;
            case "KickAxe": return BrawlPoses.KickAxe;
            case "KickCrescent": return BrawlPoses.KickCrescent;
            case "KickBack": return BrawlPoses.KickBack;
            case "KickKnee": return BrawlPoses.KickKnee;
            default: return BrawlPoses.Punch;
        }
    }

    static AnimationClip Bake(BrawlPoseRig rig, string name, float duration, bool loop,
        System.Action<BrawlPoseRig, float> pose)
    {
        return SaveClip(rig.BakeClip(name, duration, loop, Fps, pose), $"{AnimDir}/{name}.anim");
    }

    // -------------------------------------------------------- clip plumbing

    /// <summary>How much of the Hips position a clip may keep animating.</summary>
    enum HipsPin { None, Horizontal, Full }

    static AnimationClip AdoptClip(string robot, string title, string key, bool loop,
        Vector3 restHips, HipsPin pin)
    {
        return CloneClip(FindClip($"{FightDir}/{robot}-{key}.glb"),
            $"{AnimDir}/Brawl_{title}_{key}_meshy.anim", loop, restHips, pin);
    }

    static void AdoptWhole(string robot, string title, Dictionary<string, AnimationClip> moves,
        string state, bool loop, Vector3 restHips, HipsPin pin, params string[] candidates)
    {
        foreach (var key in candidates)
        {
            var clip = AdoptClip(robot, title, key, loop, restHips, pin);
            if (clip != null)
            {
                moves[state] = clip;
                return;
            }
        }
    }


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

    /// <summary>
    /// Copies a clip; with <paramref name="pinHips"/> set, the Hips'
    /// horizontal position channels are replaced by constants at the rest
    /// values DURING the copy — the in-place conversion for Meshy reaction
    /// clips, whose travel lives in the curves (blown-back: 5.2 m).
    ///
    /// Pinned here, at authoring time, and not afterwards: curve edits made
    /// AFTER SaveClip's CreateAsset were silently lost on save, which
    /// shipped clips that still travelled — the robots teleporting back to
    /// where they were hit, twice. The constant must be part of the first
    /// serialization.
    /// </summary>
    static AnimationClip CloneClip(AnimationClip source, string path, bool loop,
        Vector3 restHips = default, HipsPin pin = HipsPin.None)
    {
        if (source == null)
            return null;
        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(path),
            frameRate = source.frameRate,
        };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve curve;
            if (PinValue(binding, restHips, pin, out float value))
                curve = AnimationCurve.Constant(0f, Mathf.Max(source.length, 0.01f), value);
            else
                curve = AnimationUtility.GetEditorCurve(source, binding);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, path);
    }

    /// <summary>True when this binding is a Hips position channel the pin owns.</summary>
    static bool PinValue(EditorCurveBinding binding, Vector3 restHips, HipsPin pin, out float value)
    {
        value = 0f;
        if (pin == HipsPin.None || !binding.path.EndsWith("Hips"))
            return false;
        if (binding.propertyName == "m_LocalPosition.x") { value = restHips.x; return true; }
        if (binding.propertyName == "m_LocalPosition.z") { value = restHips.z; return true; }
        if (pin == HipsPin.Full && binding.propertyName == "m_LocalPosition.y")
        {
            value = restHips.y;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The loud self-check: an "in-place" clip whose hips still drift is
    /// exactly the silent failure that shipped teleporting robots. Reads
    /// the saved ASSET back from disk, so it verifies what will actually
    /// play, not the in-memory object.
    /// </summary>
    static void WarnIfDrifting(string robot, string state, AnimationClip clip)
    {
        if (clip == null)
            return;
        var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GetAssetPath(clip));
        if (saved == null)
            return;
        foreach (var binding in AnimationUtility.GetCurveBindings(saved))
        {
            if (!binding.path.EndsWith("Hips"))
                continue;
            if (binding.propertyName != "m_LocalPosition.x"
                && binding.propertyName != "m_LocalPosition.z")
                continue;
            var curve = AnimationUtility.GetEditorCurve(saved, binding);
            if (curve == null || curve.keys.Length == 0)
                continue;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var key in curve.keys)
            {
                lo = Mathf.Min(lo, key.value);
                hi = Mathf.Max(hi, key.value);
            }
            if (hi - lo > 0.5f)   // rig units are centimetres
                Debug.LogWarning($"[BrawlMoveForge] {robot} {state}: {binding.propertyName} " +
                                 $"still drifts {hi - lo:0.0} cm — the in-place pin failed to stick!");
        }
    }

    /// <summary>
    /// A Meshy sequence cut down to the [start,end] worth keeping — at the
    /// CURVE level, keys re-timed to zero, boundaries sampled exactly.
    /// (AnimationClipSettings start/stop is an importer concept; on a
    /// standalone generic .anim it does not reliably trim playback.)
    /// </summary>
    static AnimationClip CloneTrimmed(string modelPath, string path, Vector2 range,
        Vector3 restHips, HipsPin pin)
    {
        var source = FindClip(modelPath);
        if (source == null)
            return null;
        float start = Mathf.Max(0f, range.x);
        float end = Mathf.Min(source.length, range.y);
        if (end - start < 0.05f)
            return null;

        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(path),
            frameRate = source.frameRate,
        };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            if (PinValue(binding, restHips, pin, out float value))
            {
                AnimationUtility.SetEditorCurve(clip, binding,
                    AnimationCurve.Constant(0f, end - start, value));
                continue;
            }
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            var trimmed = new AnimationCurve();
            trimmed.AddKey(new Keyframe(0f, curve.Evaluate(start)));
            foreach (var key in curve.keys)
                if (key.time > start + 1e-4f && key.time < end - 1e-4f)
                    trimmed.AddKey(new Keyframe(key.time - start, key.value,
                        key.inTangent, key.outTangent));
            trimmed.AddKey(new Keyframe(end - start, curve.Evaluate(end)));
            AnimationUtility.SetEditorCurve(clip, binding, trimmed);
        }
        clip.EnsureQuaternionContinuity();

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
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

    /// <summary>
    /// How long each state's clip must take on screen. Zero means "play at
    /// natural pace" — the loops and the victory pose.
    /// </summary>
    static float TargetDuration(string state)
    {
        // Every variant of a family shares the family's window.
        foreach (var variant in BrawlMoveSet.PunchVariants)
            if (variant.trigger == state)
                return BrawlMoveSet.Table[BrawlMoveSet.Move.Punch].Duration;
        foreach (var variant in BrawlMoveSet.KickVariants)
            if (variant.trigger == state)
                return BrawlMoveSet.Table[BrawlMoveSet.Move.Kick].Duration;
        switch (state)
        {
            case BrawlAnim.FlyKick:
                var fly = BrawlMoveSet.Table[BrawlMoveSet.Move.FlyKick];
                return fly.startup + 0.35f + fly.recover;
            case BrawlAnim.Blast:
                return BrawlMoveSet.Table[BrawlMoveSet.Move.Blast].Duration;
            case BrawlAnim.Hit:
                return BrawlMoveSet.HitClipTime;
            case BrawlAnim.Knockdown:
                return BrawlMoveSet.KnockdownClipTime;
            case BrawlAnim.GetUp:
                return BrawlMoveSet.GetUpTime;
            default:
                return 0f;
        }
    }

    static string MoveStateName(string trimKey)
    {
        foreach (var variant in BrawlMoveSet.PunchVariants)
            if (variant.meshyKey == trimKey)
                return variant.trigger;
        foreach (var variant in BrawlMoveSet.KickVariants)
            if (variant.meshyKey == trimKey)
                return variant.trigger;
        switch (trimKey)
        {
            case "flykick": return BrawlAnim.FlyKick;
            case "block": return BrawlAnim.Block;
            case "hit": return BrawlAnim.Hit;
            case "knockdown": case "blownback": return BrawlAnim.Knockdown;
            case "getup": case "getup2": return BrawlAnim.GetUp;
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
        var triggers = new List<string>();
        foreach (var variant in BrawlMoveSet.PunchVariants)
            triggers.Add(variant.trigger);
        foreach (var variant in BrawlMoveSet.KickVariants)
            triggers.Add(variant.trigger);
        triggers.AddRange(new[] { BrawlAnim.FlyKick, BrawlAnim.Hit, BrawlAnim.Knockdown,
            BrawlAnim.GetUp, BrawlAnim.KO, BrawlAnim.Victory, BrawlAnim.Blast });
        foreach (var trigger in triggers)
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
            // Gameplay windows are authoritative: an adopted Meshy segment
            // plays fast or slow enough to land exactly on BrawlMoveSet's
            // timing, so swapping clips never changes the feel.
            float target = TargetDuration(pair.Key);
            if (target > 0f && pair.Value.length > 0.01f
                && Mathf.Abs(pair.Value.length - target) > 0.02f)
                state.speed = pair.Value.length / target;
            var enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, pair.Key);
            enter.duration = 0.05f;
            enter.hasExitTime = false;
            enter.canTransitionToSelf = false;
            // Knockdown has no exit at all: it clamps in the sprawl until
            // gameplay fires GetUp (whose own state walks back to
            // locomotion), or the KO twin holds the floor forever.
            if (pair.Key != BrawlAnim.Knockdown)
            {
                var exit = state.AddTransition(locomotion);
                exit.hasExitTime = true;
                exit.exitTime = 1f;
                exit.duration = 0.15f;
            }
            else
            {
                var ko = machine.AddState(BrawlAnim.KO);
                ko.motion = pair.Value;
                ko.speed = state.speed;
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

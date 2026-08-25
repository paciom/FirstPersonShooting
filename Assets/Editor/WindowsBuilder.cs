using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the native Windows player. Run from the menu, or:
///   Unity.exe -batchmode -projectPath &lt;path&gt; -buildTarget Win64
///             -executeMethod WindowsBuilder.BuildBatch -logFile &lt;log&gt;
/// The method exits the editor itself (do NOT pass -quit).
///
/// IL2CPP, not Mono: the whole point of this build is performance, and the
/// C++ toolchain it needs (VS 2022 Build Tools) is installed on this machine.
/// Everything WebGL-specific in the scripts (NetBridge, ShareBridge, Metrics
/// beacon, menu video URLs) already compiles out behind UNITY_WEBGL — the
/// standalone player runs the same code paths the editor does every day.
/// The one real difference: NetBridge.Available is false, so online PvP is
/// browser-only until the WebRTC layer gets a native transport.
/// </summary>
public static class WindowsBuilder
{
    const string OutDir = "Build/Windows";
    const string ExeName = "JetArmorHeroes.exe";

    [MenuItem("Photon Arena/Build Windows Player")]
    public static void Build()
    {
        RunBuild(exitWhenDone: false);
    }

    public static void BuildBatch()
    {
        RunBuild(exitWhenDone: true);
    }

    static void RunBuild(bool exitWhenDone)
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[WindowsBuilder] No enabled scenes in Build Settings — nothing to build.");
            if (exitWhenDone) EditorApplication.Exit(1);
            return;
        }

        ApplyWindowsSettings();

        // Switching first (rather than letting BuildPlayer do it) reimports every
        // asset for the new target up front, so the build log stays readable.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[WindowsBuilder] Switching active build target to Windows64 (this reimports assets)...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[WindowsBuilder] Could not switch to Windows64 — is Windows Build Support installed?");
                if (exitWhenDone) EditorApplication.Exit(1);
                return;
            }
        }

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var outPath = Path.Combine(projectRoot, OutDir);
        Directory.CreateDirectory(outPath);

        Debug.Log($"[WindowsBuilder] Building {scenes.Length} scene(s) to {outPath}\\{ExeName}");
        foreach (var scene in scenes)
            Debug.Log($"[WindowsBuilder]   scene: {scene}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outPath, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            var mb = summary.totalSize / (1024f * 1024f);
            Debug.Log($"[WindowsBuilder] SUCCESS — {mb:F1} MB in {summary.totalTime.TotalMinutes:F1} min at {outPath}");
            if (exitWhenDone) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[WindowsBuilder] FAILED: {summary.result}, {summary.totalErrors} error(s).");
            if (exitWhenDone) EditorApplication.Exit(1);
        }
    }

    static void ApplyWindowsSettings()
    {
        // IL2CPP with the Release C++ config: near-Master runtime speed without
        // Master's much longer C++ compile. Flip to Master for a shipping build
        // if profiling ever shows the difference matters.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);

        // Incremental GC keeps frame times smooth — same reason it matters in
        // the browser, but here it actually gets a real thread to run on.
        PlayerSettings.gcIncremental = true;

        // Borderless fullscreen at desktop resolution, alt-enter to window.
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;

        // Same reason as WebGL: a PvP host alt-tabbing to send a match code
        // must not stall the handshake. Harmless for everything else.
        PlayerSettings.runInBackground = true;

        Debug.Log("[WindowsBuilder] Windows settings: IL2CPP (Release), incremental GC, " +
                  "borderless fullscreen, run in background.");
    }
}

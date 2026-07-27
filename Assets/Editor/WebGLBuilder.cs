using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the WebGL player that gets hosted on Azure. Run from the menu, or:
///   Unity.exe -batchmode -projectPath &lt;path&gt; -buildTarget WebGL
///             -executeMethod WebGLBuilder.BuildBatch -logFile &lt;log&gt;
/// The method exits the editor itself (do NOT pass -quit).
///
/// Compression is Brotli with the decompression fallback OFF, which is the
/// smallest download but only works if the host serves each .br file with
/// Content-Encoding: br — Tools/deploy_webgl.ps1 sets that header per blob.
/// A blank page with a "not a valid Unity content" console error means the
/// headers were lost, not that the build is broken.
/// </summary>
public static class WebGLBuilder
{
    const string OutDir = "Build/WebGL";

    [MenuItem("Photon Arena/Build WebGL Player")]
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
            Debug.LogError("[WebGLBuilder] No enabled scenes in Build Settings — nothing to build.");
            if (exitWhenDone) EditorApplication.Exit(1);
            return;
        }

        ApplyWebGLSettings();

        // Switching first (rather than letting BuildPlayer do it) reimports every
        // asset for WebGL up front, so the build log stays readable.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.Log("[WebGLBuilder] Switching active build target to WebGL (this reimports assets)...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("[WebGLBuilder] Could not switch to WebGL — is the WebGL module installed?");
                if (exitWhenDone) EditorApplication.Exit(1);
                return;
            }
        }

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var outPath = Path.Combine(projectRoot, OutDir);
        Directory.CreateDirectory(outPath);

        Debug.Log($"[WebGLBuilder] Building {scenes.Length} scene(s) to {outPath}");
        foreach (var scene in scenes)
            Debug.Log($"[WebGLBuilder]   scene: {scene}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            var mb = summary.totalSize / (1024f * 1024f);
            Debug.Log($"[WebGLBuilder] SUCCESS — {mb:F1} MB in {summary.totalTime.TotalMinutes:F1} min at {outPath}");
            if (exitWhenDone) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[WebGLBuilder] FAILED: {summary.result}, {summary.totalErrors} error(s).");
            if (exitWhenDone) EditorApplication.Exit(1);
        }
    }

    static void ApplyWebGLSettings()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.dataCaching = true;

        // Explicitly-thrown only: keeps our own Debug/exception messages visible in
        // the browser console without paying for full null-check instrumentation.
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

        Debug.Log("[WebGLBuilder] WebGL settings: Brotli, fallback off, wasm, data caching on.");
    }
}

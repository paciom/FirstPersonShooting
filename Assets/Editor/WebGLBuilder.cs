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
            StampBuildFolder(outPath);
            if (exitWhenDone) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[WebGLBuilder] FAILED: {summary.result}, {summary.totalErrors} error(s).");
            if (exitWhenDone) EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Renames the player's Build folder to Build-&lt;hash&gt; and points index.html
    /// at it, so every deploy publishes its payload under a URL nobody has ever
    /// cached.
    ///
    /// The player is four files whose names never change, served with a long
    /// max-age. A returning visitor therefore reuses whatever they already have:
    /// index.html is no-cache so it is always current, but the data file beside
    /// it can be a build old. New code against old data does not fail cleanly --
    /// it produces garbage that floods stderr until the JS stack gives out
    /// ("Maximum call stack size exceeded" inside Object.write). Purging the CDN
    /// does not help; the stale copy is on the player's own disk, and Unity's
    /// dataCaching keeps a second copy in IndexedDB keyed by the same URL.
    ///
    /// Hashing the payload rather than stamping the clock means an unchanged
    /// rebuild keeps its URL, so returning players stay on their cached copy.
    /// </summary>
    static void StampBuildFolder(string outPath)
    {
        string buildDir = Path.Combine(outPath, "Build");
        string indexPath = Path.Combine(outPath, "index.html");

        if (!Directory.Exists(buildDir) || !File.Exists(indexPath))
        {
            Debug.LogWarning($"[WebGLBuilder] No Build folder or index.html under {outPath} — " +
                             "skipping the cache-busting rename.");
            return;
        }

        string hash = HashPayload(buildDir);
        string stamped = "Build-" + hash;
        string stampedDir = Path.Combine(outPath, stamped);

        // Clear out any previous build's folder: the deploy uploads whatever is
        // here, and shipping several hundred MB of superseded payload is worse
        // than the problem this fixes.
        foreach (string dir in Directory.GetDirectories(outPath, "Build-*"))
            if (dir != stampedDir) Directory.Delete(dir, recursive: true);

        if (Directory.Exists(stampedDir)) Directory.Delete(stampedDir, recursive: true);
        Directory.Move(buildDir, stampedDir);

        // One reference to rewrite: the template derives loader, data, framework
        // and code URLs from this single variable.
        string html = File.ReadAllText(indexPath);
        string replaced = html.Replace("var buildUrl = \"Build\";", $"var buildUrl = \"{stamped}\";");
        if (replaced == html)
        {
            // Fail loudly rather than deploy an index.html pointing at a folder
            // that no longer exists — that is a blank page, not a stale one.
            Directory.Move(stampedDir, buildDir);
            Debug.LogError("[WebGLBuilder] Could not find `var buildUrl = \"Build\";` in index.html. " +
                           "The template changed; the rename has been undone.");
            return;
        }
        File.WriteAllText(indexPath, replaced);

        Debug.Log($"[WebGLBuilder] Payload published as {stamped}/ — every file now sits at a URL " +
                  "no browser has cached.");
    }

    /// <summary>
    /// Short content hash over the payload. Reads the files rather than trusting
    /// timestamps so that a rebuild producing identical bytes keeps its URL.
    /// </summary>
    static string HashPayload(string buildDir)
    {
        var files = Directory.GetFiles(buildDir);
        System.Array.Sort(files, System.StringComparer.Ordinal);   // stable across machines

        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            foreach (string file in files)
            {
                byte[] name = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(file));
                sha.TransformBlock(name, 0, name.Length, null, 0);
                using (var stream = File.OpenRead(file))
                {
                    var buffer = new byte[1 << 20];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        sha.TransformBlock(buffer, 0, read, null, 0);
                }
            }
            sha.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);

            var text = new System.Text.StringBuilder(12);
            for (int i = 0; i < 6; i++) text.Append(sha.Hash[i].ToString("x2"));
            return text.ToString();
        }
    }

    static void ApplyWebGLSettings()
    {
        // Gzip, not Brotli, while the player is still ~350 MB: brotli.exe is
        // single-threaded and spent ~50 min on the data file alone, which is most
        // of the build. Gzip costs roughly 15% more bytes and turns an iteration
        // from an hour into minutes. Worth flipping back to Brotli for a release
        // build, or once the textures are cut down — deploy_webgl.ps1 already
        // sends the right Content-Encoding for either.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.dataCaching = true;

        // Explicitly-thrown only: keeps our own Debug/exception messages visible in
        // the browser console without paying for full null-check instrumentation.
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

        // Online PvP depends on this: with it off, the player pauses the moment
        // the canvas loses focus — which is exactly when a host alt-tabs to
        // send their friend the match code, stalling the handshake for both.
        PlayerSettings.runInBackground = true;

        Debug.Log($"[WebGLBuilder] WebGL settings: {PlayerSettings.WebGL.compressionFormat}, " +
                  "fallback off, wasm, data caching on, run in background.");
    }
}

using System;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Renders a DOGFIGHT: AI v AI match to Renders/&lt;name&gt;.mp4 through Unity
/// Recorder. NOT a -batchmode entry point (the Recorder never starts without
/// a graphics pipeline — EpisodeRender's discovery), so this runs a windowed
/// editor from the command line:
///
///     Unity -projectPath . -executeMethod DogfightRender.Render
///           -dogfightWar -dogfightSize 4 -dogfightRobots ranger,titan
///           -dogfightOut dogfight4v4
///
/// Render() opens the scene and presses Play; DogfightWarBootstrap (runtime
/// side) sees -dogfightWar and starts the exhibition; the [InitializeOnLoad]
/// runner below survives the play-mode domain reload, starts the recorder the
/// moment the match exists (so the fold ceremony is on tape), and closes the
/// editor a beat after a side takes the sky.
/// </summary>
public static class DogfightRender
{
    public static void Render()
    {
        UnityEditor.SceneManagement.EditorSceneManager
            .OpenScene("Assets/Scenes/GreyboxArena.unity");
        EditorApplication.EnterPlaymode();
    }
}

[InitializeOnLoad]
static class DogfightRenderRunner
{
    static RecorderController _controller;
    static double _playStarted;
    static double _overSeen;

    /// <summary>Give up after this long in play mode — a 4v4 runs to twenty
    /// kills and can honestly take a while, but a carousel must not hold a
    /// headless-launched editor open forever. The tape is saved either way.</summary>
    // Slow matchups (defensive pilots, big maps) can spend 45+ minutes
    // reaching 20 kills — four of the first batch's tapes cut off at match
    // point. The ceiling is a hang-guard, not a target; give real matches
    // room to finish.
    const double TimeoutSeconds = 75 * 60;

    /// <summary>Seconds of tape after the OVER card: the winner banner's
    /// flash and the match-point mushroom both live in this window.</summary>
    const double TailSeconds = 8;

    static DogfightRenderRunner()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), DogfightWarBootstrap.Arg) < 0)
            return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            WatchEditIdle();
            return;
        }
        _editIdleSince = 0;

        if (_playStarted == 0)
            _playStarted = EditorApplication.timeSinceStartup;

        if (_controller == null && Dogfight.Active != null)
            StartRecording();

        // The fight object dying mid-recording (a teardown, an exception)
        // means there is nothing left to film — save the tape, don't roll
        // fifteen minutes of main menu waiting for a timeout.
        if (_controller != null && Dogfight.Active == null)
        {
            Debug.LogWarning("DogfightRender: the match tore down mid-recording — saving the tape as is.");
            Finish(0);
            return;
        }

        if (_controller != null && Dogfight.Active != null && Dogfight.Active.MatchOver)
        {
            if (_overSeen == 0)
                _overSeen = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - _overSeen > TailSeconds)
            {
                Finish(0);
                return;
            }
        }
        if (EditorApplication.timeSinceStartup - _playStarted > TimeoutSeconds)
        {
            Debug.LogWarning("DogfightRender: match still running at timeout — saving the tape as is.");
            Finish(0);
        }
    }

    static double _editIdleSince;

    /// <summary>AutoRebuildOnPlay cancels the first play request and re-enters
    /// through EditorApplication.delayCall — which the rebuild's domain reload
    /// can swallow, stranding the editor in edit mode forever. If the editor
    /// sits idle and not playing, press Play again.</summary>
    static void WatchEditIdle()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            _editIdleSince = 0;
            return;
        }
        if (_editIdleSince == 0)
            _editIdleSince = EditorApplication.timeSinceStartup;
        else if (EditorApplication.timeSinceStartup - _editIdleSince > 45)
        {
            _editIdleSince = 0;
            Debug.Log("DogfightRender: editor idle in edit mode - pressing Play again.");
            EditorApplication.EnterPlaymode();
        }
    }

    static void StartRecording()
    {
        string name = ArgAfter("-dogfightOut") ?? "dogfight4v4";

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "dogfight";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920,
            OutputHeight = 1080,
        };
        movie.CaptureAudio = true;
        movie.OutputFile = "Renders/" + name;

        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = 30;
        settings.CapFrameRate = true;

        _controller = new RecorderController(settings);
        _controller.PrepareRecording();
        _controller.StartRecording();
        Debug.Log($"DogfightRender: recording '{name}' at 30 fps, 1920x1080.");
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (_controller != null && _controller.IsRecording())
            _controller.StopRecording();
        _controller = null;
        EditorApplication.ExitPlaymode();
        // Let the recorder flush and play mode wind down before the process
        // leaves — an Exit inside the same tick truncates the file.
        EditorApplication.update += WaitAndExit;
        _exitCode = code;
    }

    static int _exitCode;
    static int _settleFrames;

    static void WaitAndExit()
    {
        if (EditorApplication.isPlaying)
            return;
        if (++_settleFrames < 30)
            return;
        EditorApplication.update -= WaitAndExit;
        EditorApplication.Exit(_exitCode);
    }

    static string ArgAfter(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, flag);
        if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("-"))
            return null;
        return args[at + 1];
    }
}

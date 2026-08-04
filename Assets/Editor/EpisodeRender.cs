using System;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Renders a story episode to Renders/&lt;episode&gt;.mp4 through Unity
/// Recorder. NOT a -batchmode entry point: with -batchmode the graphics
/// pipeline never initializes and the Recorder simply never starts (Unity's
/// own documentation) — so this runs a windowed editor from the command line:
///
///     Unity -projectPath . -executeMethod EpisodeRender.Render
///           -storyEpisode pilot
///
/// Render() opens the scene and presses Play; StoryBootstrap (runtime side)
/// sees -storyEpisode and starts the mode; the [InitializeOnLoad] runner
/// below survives the play-mode domain reload, starts the recorder once the
/// director exists, and closes the editor when the episode completes.
/// </summary>
public static class EpisodeRender
{
    public static void Render()
    {
        UnityEditor.SceneManagement.EditorSceneManager
            .OpenScene("Assets/Scenes/GreyboxArena.unity");
        EditorApplication.EnterPlaymode();
    }
}

[InitializeOnLoad]
static class EpisodeRenderRunner
{
    static RecorderController _controller;
    static double _playStarted;

    /// <summary>Give up after this long in play mode — a wedged episode must
    /// not hold a headless-launched editor open forever.</summary>
    const double TimeoutSeconds = 15 * 60;

    static EpisodeRenderRunner()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), StoryBootstrap.Arg) < 0)
            return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (_playStarted == 0)
            _playStarted = EditorApplication.timeSinceStartup;

        if (_controller == null && StoryDirector.Active != null)
            StartRecording();

        if (_controller != null && StoryDirector.Completed)
        {
            Finish(0);
            return;
        }
        if (EditorApplication.timeSinceStartup - _playStarted > TimeoutSeconds)
        {
            Debug.LogError("EpisodeRender: timed out waiting for the episode to complete.");
            Finish(1);
        }
    }

    static void StartRecording()
    {
        string episode = ArgAfter(StoryBootstrap.Arg) ?? "pilot";

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "story";
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
        movie.OutputFile = "Renders/" + episode;

        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = 30;
        settings.CapFrameRate = true;

        _controller = new RecorderController(settings);
        _controller.PrepareRecording();
        _controller.StartRecording();
        Debug.Log($"EpisodeRender: recording '{episode}' at 30 fps, 1920x1080.");
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

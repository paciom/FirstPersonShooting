using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Says the word out loud.
///
/// Hearing a character is half of learning it, so Chinese Quest speaks every
/// word it rewards and every answer it reveals. Unity has no text-to-speech,
/// and the browser API that would cover WebGL does not exist anywhere else,
/// so the clips are baked offline by Tools/chinesevoice.ps1 with Windows'
/// zh-CN voice and shipped as ordinary AudioClips — identical on every
/// platform, no runtime dependency at all.
///
/// One voice at a time, deliberately: a new word cuts off the last one
/// rather than talking over it. Clips are cached after first load and
/// released with the mode.
/// </summary>
public static class ChineseVoice
{
    const string ResourceFolder = "Chinese/Voice";

    static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
    static AudioSource _source;
    static bool _warned;

    /// <summary>Speak a word. Silent (and harmless) if its clip was never baked.</summary>
    public static void Say(ChineseLexicon.Word word)
    {
        if (!word.IsValid)
            return;
        Say(word.AudioId);
    }

    public static void Say(string audioId)
    {
        var clip = Load(audioId);
        if (clip == null)
            return;

        var source = Source();
        if (source == null)
            return;
        // Stop first: two overlapping Chinese words are worse than one.
        source.Stop();
        source.clip = clip;
        source.Play();
    }

    public static void Stop()
    {
        if (_source != null)
            _source.Stop();
    }

    /// <summary>
    /// Drop the speaker when the mode ends. The clip cache survives — the
    /// player is very likely coming straight back, and Resources.Load of a
    /// clip already in memory is the cheap half anyway.
    /// </summary>
    public static void Release()
    {
        if (_source != null)
        {
            Object.Destroy(_source.gameObject);
            _source = null;
        }
    }

    static AudioSource Source()
    {
        if (_source != null)
            return _source;
        var go = new GameObject("ChineseVoice");
        // Not parented to the mode: the mode is destroyed on teardown, and a
        // word cut off mid-syllable by an Escape keypress is a rough edge.
        // Release() is what ends it.
        _source = go.AddComponent<AudioSource>();
        // Flat 2D: the teacher is not standing anywhere in the arena.
        _source.spatialBlend = 0f;
        _source.playOnAwake = false;
        _source.volume = 1f;
        return _source;
    }

    static AudioClip Load(string audioId)
    {
        if (string.IsNullOrEmpty(audioId))
            return null;
        if (Cache.TryGetValue(audioId, out var cached))
            return cached;

        var clip = Resources.Load<AudioClip>($"{ResourceFolder}/{audioId}");
        Cache[audioId] = clip;
        if (clip == null && !_warned)
        {
            _warned = true;
            Debug.LogWarning($"[ChineseVoice] No clip Resources/{ResourceFolder}/{audioId} — " +
                "run `powershell -ExecutionPolicy Bypass -File Tools/chinesevoice.ps1`. " +
                "The mode plays on in silence.");
        }
        return clip;
    }
}

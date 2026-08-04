using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cast's voices: neural-TTS lines baked offline by Tools/storyvoice.py
/// into Resources/Episodes/&lt;episode&gt;/voice/&lt;id&gt;.wav and played as
/// plain AudioClips — the ChineseVoice pattern, aimed at English dialogue.
/// Baked rather than live for the same reasons: identical on every platform,
/// zero runtime dependency, and WebGL has no other option anyway.
///
/// One line at a time: a new line cuts the last off. The clip is returned so
/// the director can hold the beat for exactly its length.
/// </summary>
public static class StoryVoice
{
    static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
    static AudioSource _source;
    static bool _warned;

    /// <summary>Play a baked line. Returns the clip (null when missing —
    /// the mode plays on with text-length timing).</summary>
    public static AudioClip Say(string episode, string id)
    {
        var clip = Load(episode, id);
        if (clip == null)
            return null;
        var source = Source();
        source.Stop();
        source.clip = clip;
        source.Play();
        return clip;
    }

    public static AudioClip Load(string episode, string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        string key = $"Episodes/{episode}/voice/{id}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var clip = Resources.Load<AudioClip>(key);
        Cache[key] = clip;
        if (clip == null && !_warned)
        {
            _warned = true;
            Debug.LogWarning($"[StoryVoice] No clip Resources/{key} — run " +
                "`python Tools/storyvoice.py` to bake the episode's dialogue. " +
                "Playing on with silent subtitles.");
        }
        return clip;
    }

    public static void Stop()
    {
        if (_source != null)
            _source.Stop();
    }

    /// <summary>Drop the speaker when the mode ends; the cache survives for
    /// the likely replay.</summary>
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
        var go = new GameObject("StoryVoice");
        _source = go.AddComponent<AudioSource>();
        // Flat 2D: dialogue is broadcast sound, not a point in the arena —
        // a closeup that pans the speaker hard left is wrong, not immersive.
        _source.spatialBlend = 0f;
        _source.playOnAwake = false;
        _source.volume = 1f;
        return _source;
    }
}

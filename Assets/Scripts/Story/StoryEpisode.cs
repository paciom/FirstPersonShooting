using System;
using UnityEngine;

/// <summary>
/// The episode file: one JSON under Resources/Episodes/&lt;id&gt;.json is the
/// whole show — cast, scenes, beats. Everything the StoryDirector does is
/// written here; the code only knows HOW to stage, never WHAT.
///
/// The format is a flat command list on purpose (the shipped pattern for
/// data-driven cutscenes — an interpreter, not a Timeline): every beat is one
/// struct with optional fields, and absent fields simply do nothing. That is
/// also what lets an LLM author episodes later: the vocabulary it must write
/// against is the set of field values StoryDirector accepts, nothing more.
/// </summary>
[Serializable]
public class StoryEpisode
{
    public string title;
    public string subtitle;
    public StoryActor[] cast;
    public StoryScene[] scenes;

    public static StoryEpisode Load(string id)
    {
        var asset = Resources.Load<TextAsset>($"Episodes/{id}");
        if (asset == null)
        {
            Debug.LogError($"[Story] No episode at Resources/Episodes/{id}.json");
            return null;
        }
        var episode = JsonUtility.FromJson<StoryEpisode>(asset.text);
        if (episode == null || episode.cast == null || episode.cast.Length == 0
            || episode.scenes == null || episode.scenes.Length == 0)
        {
            Debug.LogError($"[Story] Episode '{id}' is empty or malformed.");
            return null;
        }
        return episode;
    }
}

[Serializable]
public class StoryActor
{
    /// <summary>The name beats refer to — also matched (case-blind) against
    /// the roster's displayName to pick the model.</summary>
    public string name;

    /// <summary>0 = cyan paint, 1 = magenta — the Brawl teams read as
    /// costumes here.</summary>
    public int team;

    /// <summary>Mark the actor stands on when the curtain rises.</summary>
    public string at;
}

[Serializable]
public class StoryScene
{
    public string name;
    public StoryBeat[] beats;
}

/// <summary>
/// One beat = one command. Fields compose: a beat may cut the camera, start a
/// walk AND speak — the director runs shot/move/verb first, then holds for the
/// line. Any beat that speaks or performs waits for every walk still in
/// flight; a move-only beat is fire-and-forget so two actors can cross the
/// stage together.
/// </summary>
[Serializable]
public class StoryBeat
{
    /// <summary>Full-screen title card. Shown for `wait` seconds (default 2.4).</summary>
    public string caption;

    /// <summary>Who acts: an actor name from the cast list.</summary>
    public string who;

    /// <summary>Dialogue line — subtitled, and voiced when `voice` names a
    /// baked clip (Resources/Episodes/&lt;id&gt;/voice/&lt;voice&gt;.wav).</summary>
    public string text;
    public string voice;

    /// <summary>Animator verb: punch, jab, hook, uppercut, elbow, kick,
    /// highkick, sidekick, lowkick, spinkick, kneestrike, flykick, blast,
    /// block, hit, knockdown, getup, victory.</summary>
    public string verb;

    /// <summary>Walk to a mark (see StoryMarks). Fire-and-forget unless the
    /// same beat also speaks or performs.</summary>
    public string move;

    /// <summary>Face a mark or another actor after moving/before speaking.</summary>
    public string face;

    /// <summary>Camera: wide, front, closeup:X, medium:X, ots:X&gt;Y,
    /// twoshot, track:X.</summary>
    public string shot;

    /// <summary>Extra hold, seconds, after everything else in the beat.</summary>
    public float wait;
}

/// <summary>
/// Named marks on the Brawl stage floor (the authored 16×12 deck, y = 0).
/// A tiny fixed vocabulary rather than raw coordinates: the story compiler
/// and the staging can never disagree about where "L" is.
/// </summary>
public static class StoryMarks
{
    public static bool TryGet(string name, out Vector3 position)
    {
        switch (name != null ? name.ToUpperInvariant() : "")
        {
            case "CENTER": position = new Vector3(0f, 0f, 0f); return true;
            case "L": position = new Vector3(-1.7f, 0f, 0f); return true;
            case "R": position = new Vector3(1.7f, 0f, 0f); return true;
            case "CL": position = new Vector3(-3.5f, 0f, 0f); return true;
            case "CR": position = new Vector3(3.5f, 0f, 0f); return true;
            case "FRONT": position = new Vector3(0f, 0f, -2.8f); return true;
            case "BACK": position = new Vector3(0f, 0f, 2.8f); return true;
            case "ENTER_L": position = new Vector3(-8.5f, 0f, 1.5f); return true;
            case "ENTER_R": position = new Vector3(8.5f, 0f, 1.5f); return true;
            default: position = Vector3.zero; return false;
        }
    }
}

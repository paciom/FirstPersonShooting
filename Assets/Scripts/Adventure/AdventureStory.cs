using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One mini adventure: a branching story graph, authored entirely in
/// Resources/Adventures/&lt;id&gt;.json. The code knows HOW to read a graph and
/// never WHAT is in it, exactly as StoryEpisode does for the movie mode — so a
/// new adventure is a new JSON file and nothing else.
///
/// The shape is a flat node array with string ids and string links, not nested
/// objects: JsonUtility cannot do dictionaries or recursion, and a flat list
/// with goto-style references is also what lets the same file describe a GRAPH
/// rather than a tree (several nodes can point at one node, which is how the
/// story stays short enough to afford — 36 nodes cover a hundred paths).
///
/// Every node is one 30-second beat, and carries the three things a beat needs:
/// a hook, a body with a surprise in it, and a cliffhanger the choice hangs
/// off. The shot line is the bridge to video mode: one node, one clip.
///
/// Tools/adventure_doc.py validates a file against every rule the reader
/// assumes (two choices per branching node, links resolve, acyclic, nobody can
/// walk past maxSteps) and renders it for reading. Run it after editing.
/// </summary>
[Serializable]
public class AdventureStory
{
    public string id;
    public string title;
    public string tagline;
    public string hero;
    public string logline;
    public string setting;

    /// <summary>The authored cap on the longest path, in nodes shown. The
    /// reader displays it as "STEP n / max" so the player can feel the end
    /// coming.</summary>
    public int maxSteps;

    public string start;
    public AdventureNode[] nodes;

    Dictionary<string, AdventureNode> _byId;

    public AdventureNode Find(string nodeId)
    {
        if (_byId == null)
        {
            _byId = new Dictionary<string, AdventureNode>();
            foreach (var node in nodes)
                _byId[node.id] = node;
        }
        return _byId.TryGetValue(nodeId, out var found) ? found : null;
    }

    public AdventureNode StartNode => Find(start);

    public int EndingCount
    {
        get
        {
            int count = 0;
            foreach (var node in nodes)
                if (node.IsEnding)
                    count++;
            return count;
        }
    }

    public static AdventureStory Load(string storyId)
    {
        var asset = Resources.Load<TextAsset>($"Adventures/{storyId}");
        if (asset == null)
        {
            Debug.LogError($"[Adventure] No story at Resources/Adventures/{storyId}.json");
            return null;
        }
        return Parse(asset);
    }

    /// <summary>
    /// Every adventure in the build, alphabetical by id. There is no index
    /// file on purpose: dropping a JSON into Resources/Adventures is the whole
    /// act of adding a mini adventure, and the select screen finds it.
    /// </summary>
    public static AdventureStory[] All()
    {
        var assets = Resources.LoadAll<TextAsset>("Adventures");
        var stories = new List<AdventureStory>(assets.Length);
        foreach (var asset in assets)
        {
            var story = Parse(asset);
            if (story != null)
                stories.Add(story);
        }
        stories.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return stories.ToArray();
    }

    static AdventureStory Parse(TextAsset asset)
    {
        AdventureStory story = null;
        try
        {
            story = JsonUtility.FromJson<AdventureStory>(asset.text);
        }
        catch (Exception error)
        {
            Debug.LogError($"[Adventure] '{asset.name}' is not valid JSON: {error.Message}");
            return null;
        }
        // A malformed file must not take the menu down with it — the screen
        // simply lists one fewer adventure.
        if (story == null || story.nodes == null || story.nodes.Length == 0
            || string.IsNullOrEmpty(story.start) || story.StartNode == null)
        {
            Debug.LogError($"[Adventure] '{asset.name}' is empty or has no start node.");
            return null;
        }
        if (string.IsNullOrEmpty(story.id))
            story.id = asset.name;
        return story;
    }
}

[Serializable]
public class AdventureNode
{
    public string id;
    public string title;

    /// <summary>The line that grabs — the first thing on screen.</summary>
    public string hook;

    /// <summary>The beat itself, with the turn in it. ~30 seconds of screen.</summary>
    public string body;

    /// <summary>The cliffhanger the two choices hang off.</summary>
    public string cliff;

    /// <summary>What the 30-second clip shows. Unused by text mode; this is
    /// the hand-off to video mode.</summary>
    public string shot;

    /// <summary>"" for a branching node, else "good" / "bad" / "strange".</summary>
    public string ending;

    /// <summary>Marks the one ending that matches the record in
    /// Stories/S01E01_The_Green_Line.md — the side file the show agrees with.</summary>
    public bool canon;

    public AdventureChoice[] choices;

    public bool IsEnding => choices == null || choices.Length == 0;
}

[Serializable]
public class AdventureChoice
{
    public string text;
    public string to;
}

/// <summary>
/// Which endings a player has found, per story. This is the replay hook — the
/// select screen shows "3 / 7 ENDINGS" — so it is deliberately the only thing
/// the mode remembers between sessions.
/// </summary>
public static class AdventureProgress
{
    static string Key(string storyId, string endingId) => $"adv.{storyId}.{endingId}";

    public static void Record(string storyId, string endingId)
    {
        PlayerPrefs.SetInt(Key(storyId, endingId), 1);
        PlayerPrefs.Save();
    }

    public static bool Found(string storyId, string endingId)
        => PlayerPrefs.GetInt(Key(storyId, endingId), 0) == 1;

    public static int FoundCount(AdventureStory story)
    {
        int count = 0;
        foreach (var node in story.nodes)
            if (node.IsEnding && Found(story.id, node.id))
                count++;
        return count;
    }
}

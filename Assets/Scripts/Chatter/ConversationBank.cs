using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The authored radio chatter, loaded once from Resources/Chatter/conversations.
///
/// Written offline by Tools/generate_chatter.py against the MiniMax API rather
/// than assembled from a grammar at runtime. A slot grammar can vary the *words*
/// in a line but not its *shape*, so eight synonyms for "Contact" across six
/// templates still read as one line said badly. What differs between two real
/// radio calls is who interrupts whom and whether anyone answers, and that has
/// to be authored.
///
/// Each beat's text carries placeholders ({enemy}, {bearing}, {range}, ...) that
/// ChatterContext fills from the live match. That split is deliberate: the bank
/// supplies phrasing, the match supplies specifics, and the product of the two
/// is far larger than either. A thousand conversations is the phrasing half.
/// </summary>
public static class ConversationBank
{
    /// <summary>One line spoken by one robot. Field names are the short keys the
    /// generator writes -- s(peaker), r(egister), t(ext) -- because JsonUtility
    /// binds by exact field name and the file is parsed on phones.</summary>
    [Serializable]
    public class Beat
    {
        public string s;
        public string r;
        public string t;
    }

    [Serializable]
    public class Conversation
    {
        public string id;
        public string cue;
        public Beat[] beats;

        /// <summary>Distinct speaker slots ("A", "B", "C") this needs cast.</summary>
        public int SpeakerCount
        {
            get
            {
                bool b = false, c = false;
                for (int i = 0; i < beats.Length; i++)
                {
                    if (beats[i].s == "B") b = true;
                    else if (beats[i].s == "C") c = true;
                }
                return 1 + (b ? 1 : 0) + (c ? 1 : 0);
            }
        }

        /// <summary>Register the generator tagged this speaker slot with, so the
        /// director can cast a robot that actually talks that way.</summary>
        public string RegisterFor(string speaker)
        {
            for (int i = 0; i < beats.Length; i++)
                if (beats[i].s == speaker)
                    return beats[i].r;
            return null;
        }
    }

    [Serializable]
    class BankFile
    {
        public Conversation[] conversations;
    }

    const string ResourcePath = "Chatter/conversations";

    static readonly Dictionary<string, List<Conversation>> ByCue =
        new Dictionary<string, List<Conversation>>();

    static bool _loaded;

    public static int Count { get; private set; }

    public static bool IsEmpty => Count == 0;

    /// <summary>Conversations for a cue, or null. Never allocates on the hot path.</summary>
    public static List<Conversation> ForCue(string cue)
    {
        Load();
        return ByCue.TryGetValue(cue, out var list) ? list : null;
    }

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;   // set first: a parse failure must not retry every frame

        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"[Chatter] No bank at Resources/{ResourcePath}. " +
                             "Run: python Tools/generate_chatter.py generate && pack");
            return;
        }

        BankFile file;
        try
        {
            file = JsonUtility.FromJson<BankFile>(asset.text);
        }
        catch (Exception exc)
        {
            Debug.LogError($"[Chatter] Bank failed to parse: {exc.Message}");
            return;
        }

        if (file?.conversations == null)
            return;

        foreach (var convo in file.conversations)
        {
            // A malformed entry would throw later inside the beat loop, where
            // there is no context to report; drop it here instead.
            if (convo?.beats == null || convo.beats.Length == 0 || string.IsNullOrEmpty(convo.cue))
                continue;
            if (!ByCue.TryGetValue(convo.cue, out var list))
                ByCue[convo.cue] = list = new List<Conversation>();
            list.Add(convo);
            Count++;
        }

        Debug.Log($"[Chatter] Loaded {Count} conversations across {ByCue.Count} cues.");
    }
}

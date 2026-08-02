using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// What the player has met, and what they should meet next.
///
/// The themed decks are a bag to draw from. The INFINITE deck is a syllabus:
/// two thousand characters in order of how often they actually turn up in
/// written Chinese, walked from the top. This class decides, question by
/// question, whether to introduce the next one or bring back an old one, and
/// when a word has been learned well enough to stop asking.
///
/// Three rules, and they interact:
///
///   * A word is INTRODUCED in frequency order. The frontier only moves
///     forward.
///   * A word may be REVIEWED only once at least 50 questions have passed
///     since it was last asked. Sooner than that and the answer is still in
///     the player's short-term memory, so getting it right proves nothing.
///     How OFTEN a review is chosen over a new word follows the size of the
///     backlog rather than being fixed — see ReviewChance, which is the one
///     decision in this class that a simulation changed.
///   * A word is RETIRED after three correct answers in a row, and never
///     asked again.
///
/// That last rule is stated as "three times, all correct". Implemented
/// literally it can never fire for a word missed once — a single slip would
/// keep a character in rotation forever and the review pool would grow without
/// bound, crowding out the new material that is the point of the deck. Three
/// IN A ROW is the same promise for a player who is getting it right, and a
/// reachable one for a player who is still learning.
///
/// State survives the session in PlayerPrefs, because a syllabus that resets
/// every time the game is opened is not a syllabus.
/// </summary>
public class ChineseProgress
{
    /// <summary>Questions that must pass before a word may be asked again.</summary>
    public const int ReviewGap = 50;

    /// <summary>Correct answers in a row that retire a word for good.</summary>
    public const int RetireStreak = 3;

    // How often a question is a review rather than a new word, when a review
    // is due at all. NOT a constant, and that turned out to matter more than
    // anything else here.
    //
    // A flat rate cannot work, because the deck is 2000 long and retiring one
    // word costs three correct answers. Simulated over 4000 questions at 85%
    // accuracy, a flat 35% introduced all 2000 characters and retired 354 of
    // them, leaving a backlog of 1646 words the player had met roughly twice
    // each and learned none of. That is not a syllabus, it is a flood.
    //
    // So the rate follows the backlog: pool/(pool+Anchor), floored and capped.
    // An empty pool leans toward new material; a full one stops introducing
    // and consolidates. Same simulation, same accuracy: 1015 introduced, 918
    // retired, backlog 97. And a player who is struggling gets fewer new
    // words rather than more, which falls out of the formula for free.
    const float ReviewAnchor = 14f;
    const float MinReviewChance = 0.30f;
    const float MaxReviewChance = 0.75f;

    float ReviewChance
    {
        get
        {
            float pool = _active.Count;
            return Mathf.Clamp(pool / (pool + ReviewAnchor), MinReviewChance, MaxReviewChance);
        }
    }

    const string Key = "PhotonArena.ChineseInfinite";

    class Record
    {
        public int shown;
        public int streak;
        public int lastAsked = int.MinValue / 2;
        public bool Retired => streak >= RetireStreak;
    }

    readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();
    /// <summary>Seen and not yet retired — the review pool, kept as a list to scan.</summary>
    readonly List<int> _active = new List<int>();
    readonly int _count;

    int _frontier;
    int _asked;
    bool _dirty;

    public ChineseProgress(int wordCount)
    {
        _count = Mathf.Max(1, wordCount);
        Load();
    }

    /// <summary>How far down the frequency list the player has been introduced.</summary>
    public int Introduced => Mathf.Min(_frontier, _count);

    /// <summary>How many words have been retired — three in a row, done.</summary>
    public int Learned
    {
        get
        {
            int learned = 0;
            foreach (var record in _records.Values)
                if (record.Retired)
                    learned++;
            return learned;
        }
    }

    /// <summary>Pick the next word's index, and mark it asked.</summary>
    public int Next()
    {
        int index = Choose();
        var record = Get(index);
        record.lastAsked = _asked;
        _asked++;
        _dirty = true;
        return index;
    }

    int Choose()
    {
        bool exhausted = _frontier >= _count;
        // Reviews win as often as the backlog says they should — and always,
        // once there is nothing left to introduce.
        if (exhausted || Random.value < ReviewChance)
        {
            int due = MostOverdue();
            if (due >= 0)
                return due;
        }
        if (!exhausted)
            return _frontier++;

        // Nothing new and nothing due yet: take the longest-waiting word
        // anyway rather than stall. Only reachable once the whole deck has
        // been introduced and the survivors are all recent.
        int oldest = -1;
        foreach (int index in _active)
            if (oldest < 0 || _records[index].lastAsked < _records[oldest].lastAsked)
                oldest = index;
        return oldest >= 0 ? oldest : 0;
    }

    /// <summary>
    /// The word that has waited longest past the gap, or -1 if none has.
    /// Longest-waiting rather than random so the pool drains evenly instead of
    /// re-asking whichever word the roll happened to land on.
    /// </summary>
    int MostOverdue()
    {
        int best = -1;
        foreach (int index in _active)
        {
            var record = _records[index];
            if (_asked - record.lastAsked < ReviewGap)
                continue;
            if (best < 0 || record.lastAsked < _records[best].lastAsked)
                best = index;
        }
        return best;
    }

    /// <summary>The answer came in. Advance or reset that word's streak.</summary>
    public void Report(int index, bool correct)
    {
        var record = Get(index);
        record.shown++;
        record.streak = correct ? record.streak + 1 : 0;
        _dirty = true;

        if (record.Retired)
            _active.Remove(index);
        else if (!_active.Contains(index))
            _active.Add(index);
    }

    /// <summary>This word's record, created on first sight.</summary>
    Record Get(int index)
    {
        if (!_records.TryGetValue(index, out var record))
        {
            record = new Record();
            _records[index] = record;
        }
        return record;
    }

    // ------------------------------------------------------------ persistence

    /// <summary>
    /// Write the syllabus out. Called on the way out of the mode rather than
    /// after every answer: PlayerPrefs.Save touches the disk, and a learner
    /// answering a question every three seconds does not need it to.
    /// </summary>
    public void Flush()
    {
        if (!_dirty)
            return;
        _dirty = false;

        var text = new StringBuilder();
        text.Append(_frontier).Append('|').Append(_asked).Append('|');
        bool first = true;
        foreach (var pair in _records)
        {
            if (!first)
                text.Append(',');
            first = false;
            text.Append(pair.Key).Append(':')
                .Append(pair.Value.shown).Append(':')
                .Append(pair.Value.streak).Append(':')
                .Append(pair.Value.lastAsked);
        }
        PlayerPrefs.SetString(Key, text.ToString());
        PlayerPrefs.Save();
    }

    void Load()
    {
        string text = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(text))
            return;

        var parts = text.Split('|');
        if (parts.Length < 3)
            return;
        int.TryParse(parts[0], out _frontier);
        int.TryParse(parts[1], out _asked);
        _frontier = Mathf.Clamp(_frontier, 0, _count);

        foreach (string entry in parts[2].Split(','))
        {
            var fields = entry.Split(':');
            if (fields.Length != 4)
                continue;
            if (!int.TryParse(fields[0], out int index) || index < 0 || index >= _count)
                continue;
            var record = new Record();
            int.TryParse(fields[1], out record.shown);
            int.TryParse(fields[2], out record.streak);
            int.TryParse(fields[3], out record.lastAsked);
            _records[index] = record;
            if (!record.Retired)
                _active.Add(index);
        }
    }

    /// <summary>Wipe the syllabus — start the two thousand again from the top.</summary>
    public static void Forget()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}

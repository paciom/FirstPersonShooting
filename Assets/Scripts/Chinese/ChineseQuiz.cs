using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One question at a time: which word to ask, and the three decoys to bury it
/// among. Both Chinese modes deal from this, because the question is the part
/// they actually share — Quest and Run differ only in what happens after the
/// player commits.
///
/// Two sources of questions behind one interface:
///
///   * A THEMED deck is a bag. Pick a word not asked lately, and take the
///     decoys from the same deck — same-theme decoys are the whole difficulty,
///     since four words from four themes can be answered off the theme alone
///     without ever reading the character.
///   * The INFINITE deck is a syllabus walked in frequency order with spaced
///     repetition on top; see <see cref="ChineseProgress"/>. Its decoys come
///     from nearby ranks, which keeps them words of comparable difficulty and,
///     usually, ones the player has already met.
/// </summary>
public class ChineseQuiz
{
    /// <summary>Answers per question. Four robots, four cards, four keys.</summary>
    public const int Count = 4;

    /// <summary>
    /// How far either side of the target the infinite deck fishes for decoys.
    /// Wide enough that they are not always the same handful, narrow enough
    /// that a rank-12 character is never buried among rank-1800 ones.
    /// </summary>
    const int DecoyWindow = 150;

    public ChineseLexicon.Deck Deck { get; }
    public bool IsInfinite { get; }

    /// <summary>The word being asked — always the correct answer.</summary>
    public ChineseLexicon.Word Word { get; private set; }

    /// <summary>The four answers on offer, in card order.</summary>
    public ChineseLexicon.Word[] Choices { get; } = new ChineseLexicon.Word[Count];

    /// <summary>Which of <see cref="Choices"/> is right.</summary>
    public int Correct { get; private set; }

    readonly ChineseProgress _progress;
    int _index = -1;

    /// <summary>
    /// Words asked recently, newest last. A themed deck of twelve that asks
    /// the same character three times running does not feel random, it feels
    /// broken; half the deck is held back so short decks still rotate. The
    /// infinite deck has ChineseProgress for this and does not use it.
    /// </summary>
    readonly List<string> _recent = new List<string>();

    public ChineseQuiz(int deckIndex)
    {
        Deck = ChineseLexicon.DeckAt(deckIndex);
        IsInfinite = ChineseLexicon.IsInfinite(deckIndex);
        if (IsInfinite)
            _progress = new ChineseProgress(Deck.Count);
    }

    /// <summary>Words retired for good — three right in a row. Infinite deck only.</summary>
    public int Learned => _progress != null ? _progress.Learned : 0;

    /// <summary>How far down the frequency list the player has been taken.</summary>
    public int Introduced => _progress != null ? _progress.Introduced : 0;

    public void Deal()
    {
        var words = Deck.words;
        if (words.Length == 0)
            return;

        _index = IsInfinite ? _progress.Next() : PickFromBag(words);
        Word = words[_index];

        // Distinct by MEANING, not by index: a deck holding two words that
        // both mean "old" would otherwise offer two correct-looking answers
        // and mark one of them wrong.
        var chosen = new List<ChineseLexicon.Word> { Word };
        int low = 0, high = words.Length;
        if (IsInfinite)
        {
            low = Mathf.Max(0, _index - DecoyWindow);
            high = Mathf.Min(words.Length, Mathf.Max(_index + DecoyWindow, low + Count * 4));
        }

        for (int attempt = 0; attempt < 240 && chosen.Count < Count; attempt++)
        {
            var candidate = words[Random.Range(low, high)];
            bool clash = false;
            foreach (var taken in chosen)
                if (taken.english == candidate.english || taken.hanzi == candidate.hanzi)
                    clash = true;
            if (!clash)
                chosen.Add(candidate);
        }
        // A window too repetitive to fill four slots widens rather than
        // dealing a broken question.
        for (int i = 0; chosen.Count < Count && i < words.Length; i++)
            if (!chosen.Contains(words[i]))
                chosen.Add(words[i]);

        for (int i = 0; i < Count; i++)
            Choices[i] = chosen[i % chosen.Count];
        for (int i = Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (Choices[i], Choices[j]) = (Choices[j], Choices[i]);
        }
        Correct = 0;
        for (int i = 0; i < Count; i++)
            if (Choices[i].hanzi == Word.hanzi)
                Correct = i;
    }

    /// <summary>The answer came in. Only the syllabus cares.</summary>
    public void Report(bool correct)
    {
        if (_progress != null && _index >= 0)
            _progress.Report(_index, correct);
    }

    /// <summary>Write the syllabus out — on the way out of the mode.</summary>
    public void Flush()
    {
        _progress?.Flush();
    }

    int PickFromBag(ChineseLexicon.Word[] words)
    {
        int hold = Mathf.Min(_recent.Count, Mathf.Max(0, words.Length / 2 - 1));
        int pick = Random.Range(0, words.Length);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int candidate = Random.Range(0, words.Length);
            if (!RecentlyAsked(words[candidate].hanzi, hold))
            {
                pick = candidate;
                break;
            }
        }
        _recent.Add(words[pick].hanzi);
        if (_recent.Count > words.Length)
            _recent.RemoveAt(0);
        return pick;
    }

    bool RecentlyAsked(string hanzi, int hold)
    {
        for (int i = _recent.Count - hold; i < _recent.Count; i++)
            if (i >= 0 && _recent[i] == hanzi)
                return true;
        return false;
    }
}

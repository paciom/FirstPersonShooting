using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Scores in, boards out. Every mode calls <see cref="Submit"/> once when a
/// human player's match ends; this keeps the local personal best (so the
/// board screen has something to show offline and for guests) and, when the
/// player is signed in, posts it to the server's /api/score. A post that
/// cannot reach the server is queued in PlayerPrefs and retried on the next
/// sign-in or board view, so a flaky connection never eats a record.
///
/// Board keys: the server's mode key (see <see cref="ModeKey"/>) and the
/// arena's DISPLAY name ("" for the mode's overall board). Names, never
/// indexes -- rosters reorder, names don't.
/// </summary>
public class LeaderboardClient : MonoBehaviour
{
    public static LeaderboardClient Instance { get; private set; }

    const string BestPrefix = "lb.best.";
    const string PendingPref = "lb.pending";
    const int PendingMax = 40;
    const int TimeoutSeconds = 10;

    /// <summary>Overall (all arenas) board name, as the server spells it.</summary>
    public const string AllArenas = "";

    [Serializable]
    public class Row
    {
        public int rank;
        public string username;
        public int score;
        public long at;
        public bool me;
    }

    [Serializable]
    public class BoardReply
    {
        public bool ok;
        public string mode;
        public string arena;
        public bool higherIsBetter;
        public string unit;
        public Row[] rows;
        public int myRank;
        public int myScore;
        public string myName;
        public string error;
        public string message;
    }

    [Serializable]
    public class ScoreReply
    {
        public bool ok;
        public bool improved;
        public int best;
        public int rank;
        public bool overallImproved;
        public int overallBest;
        public string error;
        public string message;
    }

    [Serializable]
    class ScoreBody { public string mode; public string arena; public int score; }

    [Serializable]
    class Pending { public string mode; public string arena; public int score; }

    [Serializable]
    class PendingList { public List<Pending> items = new List<Pending>(); }

    /// <summary>
    /// The result of one match, once the server has spoken (or the local best
    /// moved while offline). Payload: mode key, arena, score, reply (null when
    /// the post never reached the server).
    /// </summary>
    public static event Action<string, string, int, ScoreReply> Posted;

    bool _flushing;

    public static LeaderboardClient Ensure()
    {
        if (Instance == null)
        {
            Instance = new GameObject("LeaderboardClient").AddComponent<LeaderboardClient>();
            Instance.gameObject.AddComponent<LeaderboardToast>();
            DontDestroyOnLoad(Instance.gameObject);
        }
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        AccountClient.Changed += OnAccountChanged;
    }

    void OnDestroy()
    {
        AccountClient.Changed -= OnAccountChanged;
    }

    void OnAccountChanged()
    {
        if (AccountClient.Instance != null && AccountClient.Instance.SignedIn)
            FlushPending();
    }

    // ------------------------------------------------------------ mode keys

    /// <summary>
    /// The server's board name for a mode, or null for modes that have no
    /// board (menus, AI-only wars, the movie). Only human play is ranked --
    /// an AI war has nobody to credit.
    /// </summary>
    public static string ModeKey(GameMode mode)
    {
        switch (mode)
        {
            case GameMode.PlayerVsAI: return "gunfight";
            case GameMode.Brawl: return "brawl";
            case GameMode.Commander: return "commander";
            case GameMode.TowerDefense: return "towerdefense";
            case GameMode.TankRaid: return "tankraid";
            case GameMode.Dogfight: return "dogfight";
            case GameMode.ChineseQuest: return "chinesequest";
            case GameMode.ChineseRun: return "chineserun";
            case GameMode.Adventure: return "adventure";
            // OnlinePvP has no referee and no result yet; when it does, it
            // gets an "online" board with career wins like Brawl.
            default: return null;
        }
    }

    /// <summary>Kid-facing title for a board, matching the main menu's words.</summary>
    public static string ModeTitle(string modeKey)
    {
        switch (modeKey)
        {
            case "gunfight": return "GUNFIGHT";
            case "brawl": return "BRAWL";
            case "commander": return "COMMANDER";
            case "towerdefense": return "TOWER DEFENSE";
            case "tankraid": return "TANK RAID";
            case "dogfight": return "DOGFIGHT";
            case "chinesequest": return "CHINESE QUEST";
            case "chineserun": return "CHINESE RUN";
            case "adventure": return "ADVENTURE";
            default: return modeKey.ToUpperInvariant();
        }
    }

    /// <summary>
    /// What the number on a board counts, per mode. The scoring itself lives
    /// at each mode's match-end call (search for LeaderboardClient.Submit):
    ///   gunfight     win 1000 + rounds won x300 + your takedowns x100
    ///   brawl        career wins (cumulative, one per victory)
    ///   commander    win 1000 + enemy buildings down x100 + yours standing x50 + units x10
    ///   towerdefense waves cleared x100 + core energy left x10
    ///   tankraid     furthest metres driven
    ///   dogfight     win 1000 + your team's kills x100
    ///   chinesequest the round's points
    ///   chineserun   furthest metres run
    ///   adventure    endings found in that story
    /// The server's plausibility caps (Server/signaling/leaderboard.js) are
    /// sized to these formulas -- change one, change both.
    /// </summary>
    public static string ModeUnit(string modeKey)
    {
        switch (modeKey)
        {
            case "brawl": return "wins";
            case "tankraid": return "metres";
            case "chineserun": return "metres";
            case "adventure": return "endings";
            default: return "points";
        }
    }

    /// <summary>Boards that ADD each post (career wins) rather than keep a best.</summary>
    public static bool IsCumulative(string modeKey) => modeKey == "brawl";

    /// <summary>The boards the menu lists, in menu order.</summary>
    public static readonly string[] RankedModes =
    {
        "gunfight", "brawl", "commander", "dogfight", "towerdefense",
        "tankraid", "chinesequest", "chineserun", "adventure",
    };

    /// <summary>
    /// The arenas a mode has boards for, by display name -- the same names
    /// the modes pass to <see cref="Submit"/>. Empty for modes whose maps are
    /// rolled from a seed (Commander, Tower Defense) or recycled endlessly
    /// (Tank Raid): those only have the overall board.
    /// </summary>
    public static string[] ArenasFor(string modeKey)
    {
        var names = new List<string>();
        switch (modeKey)
        {
            case "gunfight":
                for (int i = 0; i < ArenaLibrary.Count; i++)
                    names.Add(ArenaLibrary.Get(i).DisplayName);
                break;
            case "brawl":
                foreach (var def in BrawlArenas.All)
                    names.Add(def.name);
                break;
            case "dogfight":
                foreach (var kind in DogfightMapPick.All)
                    names.Add(DogfightMapPick.NameOf(kind));
                break;
            case "chinesequest":
            case "chineserun":
                for (int i = 0; i < ChineseLexicon.MenuCount; i++)
                    names.Add(ChineseLexicon.DeckAt(i).title);
                break;
            case "adventure":
                foreach (var story in AdventureStory.All())
                    names.Add(story.title);
                break;
        }
        return names.ToArray();
    }

    // ---------------------------------------------------------- local bests

    static string BestKey(string modeKey, string arena) =>
        BestPrefix + modeKey + "|" + Slug(arena);

    /// <summary>Same slug rule as the server, so local and remote keys agree.</summary>
    public static string Slug(string arena)
    {
        if (string.IsNullOrEmpty(arena)) return "*";
        var sb = new StringBuilder();
        bool dash = false;
        foreach (char c in arena.Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        string slug = sb.ToString().TrimEnd('-');
        if (slug.Length > 32) slug = slug.Substring(0, 32);
        return slug.Length == 0 ? "*" : slug;
    }

    /// <summary>This device's best for a board; -1 when nothing was played.</summary>
    public static int LocalBest(string modeKey, string arena) =>
        PlayerPrefs.GetInt(BestKey(modeKey, arena), -1);

    static bool RecordLocal(string modeKey, string arena, int score)
    {
        int old = LocalBest(modeKey, arena);
        if (IsCumulative(modeKey))
        {
            if (score <= 0) return false;
            PlayerPrefs.SetInt(BestKey(modeKey, arena), Mathf.Max(0, old) + score);
            return true;
        }
        if (old >= score) return false;
        PlayerPrefs.SetInt(BestKey(modeKey, arena), score);
        return true;
    }

    // -------------------------------------------------------------- submit

    /// <summary>
    /// The one call a mode makes when a human's match ends. Safe to call with
    /// a mode that has no board (ignored) and from a signed-out session (kept
    /// locally only). Never throws, never blocks.
    /// </summary>
    public static void Submit(GameMode mode, string arena, int score)
    {
        string key = ModeKey(mode);
        if (key == null) return;
        Submit(key, arena, score);
    }

    public static void Submit(string modeKey, string arena, int score)
    {
        if (string.IsNullOrEmpty(modeKey) || score < 0) return;
        // A loss on a career-wins board is nothing to record.
        if (IsCumulative(modeKey) && score == 0) return;
        arena = arena ?? AllArenas;
        var client = Ensure();
        bool localImproved = RecordLocal(modeKey, arena, score);
        if (arena != AllArenas)
            RecordLocal(modeKey, AllArenas, score);
        PlayerPrefs.Save();
        Debug.Log($"Leaderboard: {modeKey}/{Slug(arena)} = {score}" +
            (localImproved ? " (local best)" : ""));

        var account = AccountClient.Instance;
        if (account == null || !account.SignedIn || AccountClient.ApiBase == null)
        {
            // A guest's score still counts for THEM: if they sign in later it
            // rides up with the pending queue.
            client.Enqueue(modeKey, arena, score);
            Posted?.Invoke(modeKey, arena, score, null);
            return;
        }
        client.StartCoroutine(client.Post(modeKey, arena, score, reply =>
        {
            if (reply == null)
                client.Enqueue(modeKey, arena, score);
            Posted?.Invoke(modeKey, arena, score, reply);
        }));
    }

    IEnumerator Post(string modeKey, string arena, int score, Action<ScoreReply> done)
    {
        var body = new ScoreBody { mode = modeKey, arena = arena, score = score };
        string json = JsonUtility.ToJson(body);
        string text = null;
        yield return Request("POST", "/api/score", json, Token(), t => text = t);
        ScoreReply reply = null;
        if (!string.IsNullOrEmpty(text))
        {
            try { reply = JsonUtility.FromJson<ScoreReply>(text); }
            catch { /* not JSON: network-level failure */ }
        }
        if (reply != null && !reply.ok)
            Debug.Log($"Leaderboard: server declined {modeKey} {score}: {reply.error} {reply.message}");
        // A refused score (cap, bad mode) is final; only a dead network retries.
        done(reply);
    }

    // ------------------------------------------------------------- pending

    void Enqueue(string modeKey, string arena, int score)
    {
        var list = LoadPending();
        // One entry per board: keep the best, the rest would be rejected as
        // non-improvements anyway.
        var existing = list.items.Find(p => p.mode == modeKey && p.arena == arena);
        if (existing != null)
        {
            if (IsCumulative(modeKey)) existing.score += score;
            else if (existing.score < score) existing.score = score;
        }
        else
        {
            if (list.items.Count >= PendingMax) list.items.RemoveAt(0);
            list.items.Add(new Pending { mode = modeKey, arena = arena, score = score });
        }
        SavePending(list);
    }

    static PendingList LoadPending()
    {
        string raw = PlayerPrefs.GetString(PendingPref, "");
        if (raw.Length == 0) return new PendingList();
        try { return JsonUtility.FromJson<PendingList>(raw) ?? new PendingList(); }
        catch { return new PendingList(); }
    }

    static void SavePending(PendingList list)
    {
        if (list.items.Count == 0) PlayerPrefs.DeleteKey(PendingPref);
        else PlayerPrefs.SetString(PendingPref, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
    }

    /// <summary>Retry queued posts. Silent; stops at the first network miss.</summary>
    public void FlushPending()
    {
        if (_flushing) return;
        var account = AccountClient.Instance;
        if (account == null || !account.SignedIn || AccountClient.ApiBase == null) return;
        var list = LoadPending();
        if (list.items.Count == 0) return;
        StartCoroutine(FlushRoutine(list));
    }

    IEnumerator FlushRoutine(PendingList list)
    {
        _flushing = true;
        while (list.items.Count > 0)
        {
            var item = list.items[0];
            ScoreReply reply = null;
            yield return Post(item.mode, item.arena, item.score, r => reply = r);
            if (reply == null)
                break;      // still offline; keep the rest for next time
            list.items.RemoveAt(0);
            SavePending(list);
        }
        _flushing = false;
    }

    // --------------------------------------------------------------- fetch

    /// <summary>
    /// Read one board. The callback always fires: with a reply (ok or a
    /// friendly refusal), or null when the server is unreachable.
    /// </summary>
    public static void Fetch(string modeKey, string arena, int top, Action<BoardReply> done)
    {
        var client = Ensure();
        if (AccountClient.ApiBase == null)
        {
            done?.Invoke(null);
            return;
        }
        client.FlushPending();
        client.StartCoroutine(client.FetchRoutine(modeKey, arena, top, done));
    }

    IEnumerator FetchRoutine(string modeKey, string arena, int top, Action<BoardReply> done)
    {
        string path = "/api/leaderboard?mode=" + UnityWebRequest.EscapeURL(modeKey) +
            "&arena=" + UnityWebRequest.EscapeURL(arena ?? "") + "&top=" + top;
        string text = null;
        yield return Request("GET", path, null, Token(), t => text = t);
        BoardReply reply = null;
        if (!string.IsNullOrEmpty(text))
        {
            try { reply = JsonUtility.FromJson<BoardReply>(text); }
            catch { /* not JSON */ }
        }
        done?.Invoke(reply);
    }

    // ------------------------------------------------------------- plumbing

    static string Token() => AccountClient.SessionToken;

    IEnumerator Request(string method, string path, string json, string bearer, Action<string> done)
    {
        using (var req = new UnityWebRequest(AccountClient.ApiBase + path, method))
        {
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrEmpty(bearer))
                req.SetRequestHeader("Authorization", "Bearer " + bearer);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            done(req.downloadHandler.text);
        }
    }
}

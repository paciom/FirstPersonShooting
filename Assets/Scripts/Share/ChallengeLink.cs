using System;
using System.Text;
using UnityEngine;

/// <summary>
/// The share code that rides in the page URL, and the boot intent it becomes.
///
/// A kid does not spread a game, they spread a LINK — so a challenge has to
/// survive a paste into a chat window and land the friend inside the fight,
/// with no code to type, no account and no menu in the way. That is this
/// file's whole job: turn a finished bout into
/// <c>https://play.jah.cc/#c=djE...</c>, and turn that fragment back into
/// "fight TITAN vs BOLT on CARRIER DECK at CHAMPION".
///
/// Three decisions worth keeping:
///
/// FRAGMENT, NOT QUERY. `#c=` never reaches the server, so every challenge
/// link is the same URL to Cloudflare and to Unity's IndexedDB data cache —
/// a shared link therefore hits a warm cache instead of re-downloading the
/// player. A `?c=` link would be a distinct URL and would cost the receiver
/// the whole download again. `?c=` is still ACCEPTED (some chat clients
/// helpfully "clean" fragments) but never produced.
///
/// NAMES, NOT INDEXES. Robots and stages travel as their display names.
/// Roster order changes with every fleet pass, and a link a kid sent last
/// month must not silently become a different fight — an unknown name falls
/// back to the local default rather than resolving to whatever now sits at
/// index 4.
///
/// UNKNOWN KEYS ARE IGNORED. The payload is `k=v` pairs, so a build that
/// learns a new field still reads old links, and old builds still read new
/// ones. Only `v` (format version) and `m` (mode) are load-bearing.
/// </summary>
public static class ChallengeLink
{
    /// <summary>What a produced link looks like: base URL + this + the code.</summary>
    public const string Prefix = "#c=";

    /// <summary>Where the player lives, for links built outside a browser.</summary>
    public const string PlayerUrl = "https://play.jah.cc/";

    /// <summary>Payload format. Bumped only for a change old builds cannot read.</summary>
    const int Format = 1;

    /// <summary>A code longer than this is not one of ours; do not even decode it.</summary>
    const int MaxCode = 512;

    public const string ModeBrawl = "brawl";
    public const string ModeBrawlWar = "brawlwar";

    /// <summary>One shared fight: who fought whom, where, and how it went.</summary>
    public struct Challenge
    {
        public string mode;         // ModeBrawl / ModeBrawlWar
        public string cyan;         // roster display name in the P1 corner
        public string magenta;      // roster display name in the P2 corner
        public string stage;        // BrawlArenas name; empty means RANDOM
        public int difficulty;      // BrawlDifficulty level, 1-5; 0 means "leave mine"
        public string from;         // sender's pilot name, empty when signed out
        public string score;        // "2-1", as the sender's score first
        public bool cyanWon;

        public bool IsValid => !string.IsNullOrEmpty(mode);

        /// <summary>
        /// The one line that sells the click: "ANDY WON 2-1 AS TITAN". Falls
        /// back through everything it does not know, down to naming the two
        /// fighters, so a half-filled challenge still greets the receiver
        /// with something true.
        /// </summary>
        public string Boast()
        {
            string who = string.IsNullOrEmpty(from) ? "A  PILOT" : from.ToUpperInvariant();
            string robot = (cyanWon ? cyan : magenta) ?? "";
            if (!string.IsNullOrEmpty(score) && robot.Length > 0)
                return $"{who}  WON  {score}  AS  {robot.ToUpperInvariant()}";
            if (robot.Length > 0)
                return $"{who}  WON  AS  {robot.ToUpperInvariant()}";
            return $"{who}  SENT  YOU  A  FIGHT";
        }

        /// <summary>"TITAN  vs  BOLT" — the matchup, for the loader and the HUD.</summary>
        public string Matchup()
        {
            if (string.IsNullOrEmpty(cyan) || string.IsNullOrEmpty(magenta))
                return "";
            return $"{cyan.ToUpperInvariant()}  vs  {magenta.ToUpperInvariant()}";
        }
    }

    // ---------------------------------------------------------------- encode

    /// <summary>The bare code — no URL around it.</summary>
    public static string Encode(Challenge c)
    {
        var sb = new StringBuilder(128);
        Field(sb, "v", Format.ToString());
        Field(sb, "m", c.mode);
        Field(sb, "a", c.cyan);
        Field(sb, "b", c.magenta);
        Field(sb, "s", c.stage);
        if (c.difficulty > 0) Field(sb, "d", c.difficulty.ToString());
        Field(sb, "w", c.from);
        Field(sb, "r", c.score);
        Field(sb, "x", c.cyanWon ? "a" : "b");
        return ToBase64Url(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// The full link to paste. Built against the page the player is actually
    /// running on when there is one, so a link shared from a staging deploy or
    /// a local server points back at itself rather than at production.
    /// </summary>
    public static string Url(Challenge c) => BaseUrl() + Prefix + Encode(c);

    static void Field(StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (sb.Length > 0) sb.Append('|');
        sb.Append(key).Append('=').Append(Scrub(value));
    }

    /// <summary>
    /// The two characters the payload's own grammar owns. A pilot may name
    /// themselves anything at all, and a name carrying a '|' would otherwise
    /// forge extra fields on the way in.
    /// </summary>
    static string Scrub(string value)
    {
        if (value.IndexOf('|') < 0 && value.IndexOf('=') < 0)
            return value;
        return value.Replace('|', ' ').Replace('=', ' ');
    }

    /// <summary>The page URL with any query and fragment cut off.</summary>
    public static string BaseUrl()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url))
            return PlayerUrl;
        int cut = url.IndexOfAny(new[] { '#', '?' });
        if (cut >= 0) url = url.Substring(0, cut);
        return url;
    }

    // ---------------------------------------------------------------- decode

    public static bool TryDecode(string code, out Challenge challenge)
    {
        challenge = default;
        if (string.IsNullOrEmpty(code) || code.Length > MaxCode)
            return false;

        string plain;
        try
        {
            plain = Encoding.UTF8.GetString(FromBase64Url(code));
        }
        catch (Exception)
        {
            // A mangled paste is the normal case here, not an exception worth
            // reporting: half the links kids send arrive with a trailing
            // bracket or a smart quote glued on.
            return false;
        }

        var found = new Challenge();
        foreach (string pair in plain.Split('|'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string key = pair.Substring(0, eq);
            string value = pair.Substring(eq + 1);
            switch (key)
            {
                case "v":
                    // A payload from a FUTURE format may not mean what this
                    // build thinks it means; refuse rather than mis-fight.
                    if (!int.TryParse(value, out int format) || format > Format)
                        return false;
                    break;
                case "m": found.mode = value; break;
                case "a": found.cyan = value; break;
                case "b": found.magenta = value; break;
                case "s": found.stage = value; break;
                case "d":
                    if (int.TryParse(value, out int level)) found.difficulty = level;
                    break;
                case "w": found.from = value; break;
                case "r": found.score = value; break;
                case "x": found.cyanWon = value == "a"; break;
            }
        }

        if (!found.IsValid)
            return false;
        challenge = found;
        return true;
    }

    // ------------------------------------------------------- the boot intent

    static bool _read;
    static Challenge _pending;

    /// <summary>
    /// The challenge this page was opened with, if any. Read once — clicking
    /// SHARE mid-session rewrites the address bar, and re-reading it would
    /// hand the player their own link back as an incoming challenge.
    /// </summary>
    public static Challenge Pending
    {
        get
        {
            if (!_read)
            {
                _read = true;
                string code = CodeFromLaunch();
                if (!string.IsNullOrEmpty(code) && TryDecode(code, out Challenge c))
                    _pending = c;
            }
            return _pending;
        }
    }

    public static bool HasPending => Pending.IsValid;

    /// <summary>
    /// Take the pending challenge and clear it — including from the address
    /// bar, so a reload (or a kid who bookmarks the page) does not drop them
    /// back into someone else's fight forever.
    /// </summary>
    public static Challenge Consume()
    {
        Challenge c = Pending;
        _pending = default;
        if (c.IsValid)
            ShareBridge.ClearLocationHash();
        return c;
    }

    /// <summary>
    /// Where the code comes from, in order: the live address bar (WebGL, and
    /// authoritative — the hash can change after the page loaded), the URL
    /// Unity was handed at startup, then `-challenge &lt;code&gt;` for testing
    /// the whole path from the editor and desktop builds.
    /// </summary>
    static string CodeFromLaunch()
    {
        string hash = ShareBridge.LocationHash();
        string code = CodeFrom(hash);
        if (!string.IsNullOrEmpty(code)) return code;

        code = CodeFrom(Application.absoluteURL);
        if (!string.IsNullOrEmpty(code)) return code;

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-challenge")
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// Pull the code out of `#c=` or `?c=`. Anchored on the delimiter so a
    /// parameter that merely ENDS in c (`src=`, `abc=`) can never be mistaken
    /// for this one — the same trap NetSession's query lookup documents.
    /// </summary>
    static string CodeFrom(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        int at = -1;
        foreach (string opener in new[] { "#c=", "?c=", "&c=" })
        {
            at = url.IndexOf(opener, StringComparison.Ordinal);
            if (at >= 0) { at += opener.Length; break; }
        }
        if (at < 0)
            return null;
        int end = url.IndexOfAny(new[] { '&', '#', '?' }, at);
        return end < 0 ? url.Substring(at) : url.Substring(at, end - at);
    }

    // ------------------------------------------------- resolving against this build

    /// <summary>
    /// Roster index for a robot named in a link, or <paramref name="fallback"/>
    /// when this build has never heard of it (a link from a newer fleet).
    /// </summary>
    public static int RobotIndex(RobotRoster roster, string name, int fallback)
    {
        if (roster == null || !roster.HasRobots || string.IsNullOrEmpty(name))
            return fallback;
        for (int i = 0; i < roster.robots.Length; i++)
            if (string.Equals(roster.robots[i].displayName, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return fallback;
    }

    /// <summary>
    /// BrawlArenas selection (1-based, 0 = RANDOM) for a stage named in a
    /// link. An unknown stage rolls the dice rather than refusing the fight.
    /// </summary>
    public static int StageSelection(string name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;
        var all = BrawlArenas.All;
        for (int i = 0; i < all.Count; i++)
            if (string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return 0;
    }

    // ------------------------------------------------------------ base64url

    /// <summary>
    /// Standard base64 with the two URL-hostile characters swapped and the
    /// padding dropped: `+/=` all get percent-escaped or truncated somewhere
    /// between a chat app, a URL shortener and an address bar.
    /// </summary>
    static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    static byte[] FromBase64Url(string code)
    {
        var sb = new StringBuilder(code.Length + 3);
        foreach (char c in code)
        {
            if (c == '-') sb.Append('+');
            else if (c == '_') sb.Append('/');
            else sb.Append(c);
        }
        while (sb.Length % 4 != 0)
            sb.Append('=');
        return Convert.FromBase64String(sb.ToString());
    }
}

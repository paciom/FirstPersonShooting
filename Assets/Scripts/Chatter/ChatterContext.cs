using System.Text;
using UnityEngine;

/// <summary>
/// Fills the placeholders in an authored chatter line from the live match.
///
/// This is the half of the system that makes a thousand authored conversations
/// feel like tens of thousands. "Contact! {enemy}, {bearing}, {range}!" is one
/// authored line; what reaches the screen depends on who was actually spotted,
/// from where, and how far away, so the same line rarely repeats verbatim within
/// a session.
///
/// Every placeholder resolves to something non-empty even when the state it
/// wants is missing -- a half-substituted line printing literal braces at a
/// child is worse than a slightly generic one, and these are radio calls, so
/// vagueness is in character ("somewhere close", "one of theirs").
/// </summary>
public class ChatterContext
{
    // Distances are spelled out, not printed as digits, because the authored
    // lines read as speech and "forty meters" is what a voice would say.
    static readonly string[] Tens =
    {
        "zero", "ten", "twenty", "thirty", "forty", "fifty",
        "sixty", "seventy", "eighty", "ninety",
    };

    static readonly string[] VagueBearings =
    {
        "somewhere close", "out past the cover", "off to one side", "up ahead",
    };

    static readonly string[] FallbackWeapons =
    {
        "Comet Sling", "Arc Whip", "Bass Dropper", "Frostbite Beam",
        "Static Shotgun", "Prism Splitter", "Magma Mortar",
    };

    /// <summary>The robot that started the exchange. Bearings and ranges are
    /// measured from here for the whole conversation, never from whoever happens
    /// to be speaking — two robots stood apart compute different compass
    /// directions to the same enemy, and a reply that renames the place its caller
    /// just gave ("Contact east ridge" / "North side clear") reads as nonsense.</summary>
    public Transform speaker;
    public Transform enemy;
    public int teamId;
    public float shieldNormalized = 1f;

    /// <summary>Cast by speaker slot: A, B, C.</summary>
    public readonly Transform[] cast = new Transform[3];

    /// <summary>Which slot is speaking the current beat. {ally} is resolved
    /// against this, because "Where, Hawk?" said by Hawk is nobody talking to
    /// anybody — a robot addresses a teammate, not itself.</summary>
    public int speakingSlot;

    // Frozen on first use so every beat of one conversation describes one place.
    string _bearing;
    string _range;

    // ------------------------------------------------------------ substitution

    /// <summary>
    /// Single-pass placeholder substitution. Written as a scan rather than a
    /// chain of string.Replace calls because a chain allocates one throwaway
    /// string per placeholder whether or not the line contains it, and this runs
    /// for every beat of every conversation for the whole match.
    /// </summary>
    public string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            return text;

        var built = new StringBuilder(text.Length + 24);
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch != '{')
            {
                built.Append(ch);
                i++;
                continue;
            }
            int close = text.IndexOf('}', i + 1);
            if (close < 0)
            {
                built.Append(text, i, text.Length - i);   // unterminated: pass through
                break;
            }
            string value = Value(text.Substring(i + 1, close - i - 1));
            // Many authored lines open on a placeholder ("{bearing} clear."), and
            // the resolved values are lower case because they usually land
            // mid-sentence. Capitalise only where a sentence actually begins.
            if (AtSentenceStart(built))
                value = Capitalize(value);
            built.Append(value);
            i = close + 1;
        }
        return built.ToString();
    }

    /// <summary>True if the next character written begins a sentence — nothing
    /// before it, or the previous sentence just ended.</summary>
    static bool AtSentenceStart(StringBuilder built)
    {
        for (int i = built.Length - 1; i >= 0; i--)
        {
            char ch = built[i];
            if (ch == ' ' || ch == '"' || ch == '\'')
                continue;
            return ch == '.' || ch == '!' || ch == '?';
        }
        return true;
    }

    static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsLower(value[0]))
            return value;
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    string Value(string key)
    {
        switch (key)
        {
            case "enemy":     return ShortName(enemy, "one of theirs");
            case "ally":      return ShortName(Ally(), "somebody");
            case "bearing":   return _bearing ??= Bearing();
            case "range":     return _range ??= Range();
            case "shield":    return Percent(shieldNormalized);
            case "weapon":    return Weapon();
            case "crate":     return Crate();
            case "gold":      return Gold();
            case "team":      return MatchAnnouncer.TeamName(teamId);
            case "enemyteam": return MatchAnnouncer.TeamName(teamId == 1 ? 0 : 1);
            // An unknown key means the generator's whitelist and this switch
            // drifted apart. Drop it rather than print braces at a player.
            default:          return string.Empty;
        }
    }

    // --------------------------------------------------------------- resolvers

    /// <summary>
    /// A teammate the current speaker could be addressing: anyone in the cast but
    /// itself. Falls back past empty slots, so a two-robot conversation still
    /// names the other robot rather than going vague.
    /// </summary>
    Transform Ally()
    {
        for (int offset = 1; offset <= cast.Length; offset++)
        {
            var candidate = cast[(speakingSlot + offset) % cast.Length];
            if (candidate != null && candidate != cast[speakingSlot])
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// A robot's name as a teammate would say it: "Panther", not "PANTHER" or
    /// "panther-robot (Clone)". MatchAnnouncer.CharacterName shouts in caps,
    /// which suits its headlines but not the middle of a sentence.
    /// </summary>
    public static string ShortName(Transform root, string fallback)
    {
        if (root == null)
            return fallback;
        if (root.GetComponent<PlayerBrain>() != null)
            return "you";

        string name = root.name;
        int paren = name.IndexOf('(');          // "(Clone)"
        if (paren > 0) name = name.Substring(0, paren);
        name = name.Replace('_', ' ').Replace('-', ' ').Trim();

        // Strip the asset-naming tails the prefabs carry: "panther robot walk".
        foreach (var tail in new[] { " robot", " vehicle", " rig", " walk", " run" })
            if (name.EndsWith(tail, System.StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - tail.Length);

        name = name.Trim();
        if (name.Length == 0)
            return fallback;
        return char.ToUpperInvariant(name[0]) + name.Substring(1).ToLowerInvariant();
    }

    /// <summary>
    /// Where the enemy is, phrased the way a robot would call it.
    ///
    /// Deliberately geometric rather than named: ArenaDefinition has no landmark
    /// labels, so compass plus elevation plus the speaker's own left/right is
    /// everything available. Elevation and flank are checked before compass
    /// because "high ground" and "your left" are more useful to a teammate than
    /// "north-east" -- and more useful is also more believable.
    /// </summary>
    string Bearing()
    {
        if (enemy == null || speaker == null)
            return VagueBearings[Random.Range(0, VagueBearings.Length)];

        Vector3 delta = enemy.position - speaker.position;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        if (flat.sqrMagnitude < 0.01f)
            return "right on top of us";

        if (delta.y > 3.5f)
            return "the high ground";
        if (delta.y < -3.5f)
            return "the low road";

        // Signed angle from the speaker's facing: a call is relative to the
        // caller, and a teammate nearby shares roughly the same frame.
        float signed = Vector3.SignedAngle(speaker.forward, flat.normalized, Vector3.up);
        float abs = Mathf.Abs(signed);
        if (abs < 25f)
            return "dead ahead";
        if (abs > 150f)
            return "behind us";
        if (abs > 60f)
            return signed > 0f ? "the right flank" : "the left flank";

        return Compass(flat.normalized);
    }

    static string Compass(Vector3 dir)
    {
        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        switch ((int)((angle + 22.5f) % 360f / 45f))
        {
            case 0:  return "north side";
            case 1:  return "north east";
            case 2:  return "east ridge";
            case 3:  return "south east";
            case 4:  return "south side";
            case 5:  return "south west";
            case 6:  return "west ridge";
            default: return "north west";
        }
    }

    /// <summary>
    /// Distance as speech. Resolves WITH its unit ("forty meters"), which is why
    /// the generator strips a trailing "meters" the model sometimes adds.
    /// </summary>
    string Range()
    {
        if (enemy == null || speaker == null)
            return "close";

        float distance = Vector3.Distance(speaker.position, enemy.position);
        if (distance < 8f)
            return "point blank";
        if (distance > 95f)
            return "long range";

        int tens = Mathf.Clamp(Mathf.RoundToInt(distance / 10f), 1, 9);
        return Tens[tens] + " meters";
    }

    /// <summary>Shield as speech, including the word "percent".</summary>
    static string Percent(float normalized)
    {
        int tens = Mathf.Clamp(Mathf.RoundToInt(normalized * 10f), 0, 9);
        if (tens == 0)
            return "nothing left";
        return Tens[tens] + " percent";
    }

    string Weapon()
    {
        if (speaker != null)
        {
            // The enabled weapon is the equipped one; the rest of the 50-gun kit
            // rides along disabled (see WeaponCatalog.AttachAll).
            foreach (var weapon in speaker.GetComponentsInChildren<Weapon>(false))
                if (weapon.enabled && !string.IsNullOrEmpty(weapon.weaponName))
                    return weapon.weaponName;
        }
        return FallbackWeapons[Random.Range(0, FallbackWeapons.Length)];
    }

    static string Crate()
    {
        var all = TreasureCatalog.All;
        if (all == null || all.Length == 0)
            return "the crate";
        return all[Random.Range(0, all.Length)].displayName;
    }

    string Gold() => TeamBank.Gold(teamId).ToString();
}

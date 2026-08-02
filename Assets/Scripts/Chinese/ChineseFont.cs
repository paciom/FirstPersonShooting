using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one font Chinese Quest draws with.
///
/// Every other screen in this project uses Unity's built-in LegacyRuntime
/// font, which has no CJK glyphs whatsoever — and a missing glyph in legacy
/// UI Text is a silent blank box, not an exception. So this mode carries its
/// own: Assets/Resources/Chinese/NotoSansSC-Quest.ttf, a 121 KB subset of
/// Noto Sans SC (SIL OFL 1.1) built by Tools/subsetfont.py.
///
/// Three tiers, best first:
///   1. the packed subset — the only one that survives a WebGL build;
///   2. an OS CJK font, if the packed one is missing (a fresh clone before
///      LFS has pulled, an editor session mid-reimport) — desktop only;
///   3. LegacyRuntime, which draws the Latin half correctly and the Chinese
///      half as boxes. Visibly broken beats invisibly missing.
///
/// Glyph coverage is checked once per character actually drawn, so a word
/// added to the lexicon without re-running the subsetter says so in the
/// console instead of appearing as an empty rectangle nobody can diagnose.
/// </summary>
public static class ChineseFont
{
    /// <summary>Where the subsetter writes; Resources path, no extension.</summary>
    public const string ResourcePath = "Chinese/NotoSansSC-Quest";

    // Windows, macOS and the common Linux packages, in that order. Only ever
    // reached in the editor or a desktop build.
    static readonly string[] OSFallbacks =
    {
        "Microsoft YaHei", "Noto Sans SC", "Noto Sans CJK SC", "SimHei",
        "PingFang SC", "Heiti SC", "Hiragino Sans GB", "SimSun",
    };

    static Font _font;
    static bool _resolved;
    static bool _isFallback;

    static readonly HashSet<char> Reported = new HashSet<char>();

    /// <summary>True when we are drawing with something that has no CJK glyphs.</summary>
    public static bool UsingFallback
    {
        get { Resolve(); return _isFallback; }
    }

    public static Font Get()
    {
        Resolve();
        return _font;
    }

    static void Resolve()
    {
        if (_resolved)
            return;
        _resolved = true;

        _font = Resources.Load<Font>(ResourcePath);
        if (_font != null)
            return;

        Debug.LogWarning($"[ChineseFont] Resources/{ResourcePath} is missing — " +
            "run `python Tools/subsetfont.py`. Falling back to an OS font, which " +
            "does not exist in a WebGL build.");

        // CreateDynamicFontFromOSFont returns a Font even for names the
        // machine does not have (it silently substitutes), so the coverage
        // check below is what actually tells us whether this worked.
        _font = Font.CreateDynamicFontFromOSFont(OSFallbacks, 64);
        if (_font != null && _font.HasCharacter('汉'))
            return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _isFallback = true;
        Debug.LogError("[ChineseFont] No font with Chinese glyphs found. " +
            "Chinese characters will draw as empty boxes.");
    }

    /// <summary>
    /// Build a UI Text already wearing the Chinese font. Same shape as the
    /// MakeText helpers the rest of the project's runtime UI uses, so the
    /// two read alike.
    /// </summary>
    public static Text MakeText(Transform parent, string name, string content, int size,
        Color color, FontStyle style = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Get();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Set(text, content);
        return text;
    }

    /// <summary>
    /// Assign text and check its glyphs. Everything Chinese goes through
    /// here rather than touching Text.text directly — the check is the only
    /// thing standing between a forgotten `subsetfont.py` run and a blank
    /// rectangle in the middle of the screen.
    /// </summary>
    public static void Set(Text label, string content)
    {
        if (label == null)
            return;
        label.text = content;
        if (_isFallback || string.IsNullOrEmpty(content))
            return;

        Resolve();
        foreach (char c in content)
        {
            // Latin, digits and punctuation are in every font; only the
            // interesting half is worth the lookup.
            if (c < 0x2E80 || Reported.Contains(c) || _font.HasCharacter(c))
                continue;
            Reported.Add(c);
            Debug.LogWarning($"[ChineseFont] '{c}' (U+{(int)c:X4}) is not in the packed " +
                "font and will draw as a blank — re-run `python Tools/subsetfont.py`.");
        }
    }
}

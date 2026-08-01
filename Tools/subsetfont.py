#!/usr/bin/env python
"""Pack the Chinese Quest font.

Unity's built-in LegacyRuntime font — the one every other screen in this
project draws with — has no CJK glyphs at all, and a missing glyph in legacy
UI Text is a blank box, not an error. On desktop Unity can fall back to an OS
font; in a WebGL build there are no OS fonts, so the mode would ship as rows
of empty rectangles. The fix is to carry our own font.

Noto Sans SC is SIL Open Font License 1.1, so it may be redistributed inside
the game. The whole family is ~18 MB of 31,000 glyphs, which is absurd for a
vocabulary of ~160 characters, so this script cuts it down:

  1. read every string literal in Assets/Scripts/Chinese/*.cs — that is the
     lexicon AND the mode's own UI text, so any Chinese added to either is
     picked up automatically;
  2. pin the variable font's weight axis (it defaults to Thin 100, which is
     unreadable on a dark HUD);
  3. subset to those characters plus printable ASCII and the full set of
     tone-marked pinyin vowels.

Result: ~100 KB instead of ~18 MB.

    python Tools/subsetfont.py

Re-run it after adding words to ChineseLexicon.cs. If a character is missing
from the packed font, the game shows a blank — ChineseFont.cs logs a warning
naming this script when it spots one.
"""

import glob
import os
import re
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts", "Chinese")
OUT = os.path.join(ROOT, "Assets", "Resources", "Chinese", "NotoSansSC-Quest.ttf")

# Where Noto Sans SC may be found. Windows 10/11 ship it as a system font;
# otherwise download it from https://fonts.google.com/noto/specimen/Noto+Sans+SC
# and drop it next to this script.
SOURCES = [
    r"C:\Windows\Fonts\NotoSansSC-VF.ttf",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "NotoSansSC-VF.ttf"),
    "/usr/share/fonts/opentype/noto/NotoSansSC[wght].ttf",
    "/System/Library/Fonts/Supplemental/NotoSansSC-Regular.otf",
]

# Medium, not Regular: these glyphs are drawn big and glowing on a dark stage,
# where Regular reads thin and Bold closes up the denser characters' strokes.
WEIGHT = 500

# Tone-marked pinyin, in full — the lexicon only uses some of these today and
# a new word must never be the thing that discovers a missing accent.
PINYIN = "āáǎàōóǒòēéěèīíǐìūúǔùǖǘǚǜüĀÁǍÀŌÓǑÒĒÉĚÈ"

# Punctuation the HUD draws that plain ASCII does not cover.
EXTRA = "·—…×✓、。，！？：；“”‘’（）《》"


def source_font():
    for path in SOURCES:
        if os.path.isfile(path):
            return path
    sys.exit(
        "Noto Sans SC not found. Looked in:\n  "
        + "\n  ".join(SOURCES)
        + "\n\nDownload it (SIL OFL 1.1) from\n"
        "  https://fonts.google.com/noto/specimen/Noto+Sans+SC\n"
        "and save the variable TTF as Tools/NotoSansSC-VF.ttf."
    )


def wanted_characters():
    """Every character the Chinese mode can put on screen."""
    chars = set(chr(c) for c in range(0x20, 0x7F))
    chars.update(PINYIN)
    chars.update(EXTRA)

    sources = sorted(glob.glob(os.path.join(SCRIPTS, "*.cs")))
    if not sources:
        sys.exit("No scripts found in %s" % SCRIPTS)
    # The one file outside the mode's folder that draws with ChineseFont: the
    # main menu's app-icon diorama hangs a character in the air.
    sources.append(os.path.join(ROOT, "Assets", "Scripts", "MenuIconRigs.cs"))

    for path in sources:
        # utf-8-sig: these files carry a BOM so that compilers outside Unity
        # do not read them in the machine's ANSI codepage.
        text = open(path, encoding="utf-8-sig").read()
        # Strip comments first — a comment explaining a character should not
        # be a reason to ship its glyph.
        text = re.sub(r"//[^\n]*", "", text)
        text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
        for literal in re.findall(r'"((?:[^"\\\n]|\\.)*)"', text):
            chars.update(literal)

    # Control and escape leftovers are not glyphs.
    return set(c for c in chars if ord(c) >= 0x20)


def main():
    src = source_font()
    chars = wanted_characters()
    cjk = sorted(c for c in chars if ord(c) > 0x2E7F)
    print("source : %s" % src)
    print("chars  : %d total, %d CJK" % (len(chars), len(cjk)))

    font = TTFont(src, fontNumber=0)
    if "fvar" in font:
        print("weight : pinning wght=%d (was default %g)"
              % (WEIGHT, font["fvar"].axes[0].defaultValue))
        font = instancer.instantiateVariableFont(font, {"wght": WEIGHT}, updateFontNames=True)

    cmap = font.getBestCmap()
    missing = [c for c in sorted(chars) if ord(c) not in cmap]
    if missing:
        print("WARNING: not in the source font: %s" % "".join(missing))

    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.notdef_outline = True
    options.drop_tables += ["DSIG"]
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(unicodes=[ord(c) for c in chars])
    subsetter.subset(font)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    font.save(OUT)
    print("wrote  : %s (%.0f KB, %d glyphs)"
          % (os.path.relpath(OUT, ROOT), os.path.getsize(OUT) / 1024.0,
             font["maxp"].numGlyphs))


if __name__ == "__main__":
    main()

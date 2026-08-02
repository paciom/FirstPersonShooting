#!/usr/bin/env python
"""Build the INFINITE deck: the 2000 most frequent Chinese characters, in order.

Chinese Quest's themed decks are hand-picked and finite. The infinite deck is
neither: it walks a frequency list from the most common character down, so a
player who keeps going keeps meeting words that are genuinely worth knowing
next. See ChineseProgress for the spaced repetition that rides on top of it.

Two public sources, joined:

  * ORDER comes from Jun Da's Modern Chinese Character Frequency List
    (lingua.mtsu.edu), which is the standard citation for this and is derived
    from a 258-million-character corpus.
  * PINYIN AND MEANING come from CC-CEDICT (CC BY-SA 4.0). Jun Da's file
    carries glosses of its own, but they are unaligned — a character with one
    reading and four senses is formatted identically to one with four
    readings — so they cannot be split reliably. CEDICT is one entry per
    reading and is clean.

The output is a plain TSV at Assets/Resources/Chinese/frequency.txt, loaded as
a TextAsset. Not a generated .cs file: 2000 entries is data, and data that
large in source is a file nobody can read and every diff has to scroll past.

    python Tools/buildfrequency.py

Downloads both sources on each run (~4 MB). Re-run it only to change the
selection rules — the output is committed.
"""

import gzip
import io
import os
import re
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Chinese", "frequency.txt")

FREQ_URL = "https://lingua.mtsu.edu/chinese-computing/statistics/char/list.php?Which=MO"
CEDICT_URL = "https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz"

WANTED = 2000

TONES = {
    "a": "āáǎà", "e": "ēéěè", "i": "īíǐì",
    "o": "ōóǒò", "u": "ūúǔù", "v": "ǖǘǚǜ",
}

# Definitions that teach a quiz nothing. A gloss the player cannot match to a
# picture in their head is a gloss that makes the question a coin flip.
REJECT = re.compile(
    r"^(surname|variant of|old variant|Japanese variant|used in|see |see also|"
    r"abbr\.|abbreviation|CL:|erhua variant|archaic variant|ancient|"
    r"corruption of|equivalent to|same as|component in|radical in|"
    r"Taiwan pr\.|also pr\.|\(onom|phonetic)",
    re.I)

# Bracketed cross-references and classifier notes inside a definition.
STRIP = re.compile(r"\[[^\]]*\]|\|[^/]*")


def fetch(url, binary=False):
    request = urllib.request.Request(url, headers={"User-Agent": "photon-arena-build"})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
    return data if binary else data


def numbered_to_marked(syllable):
    """`xiong2` -> `xióng`. The tone mark goes where pinyin says it goes."""
    match = re.match(r"^([a-zA-Z:]+)([1-5])?$", syllable)
    if not match:
        return syllable.lower()
    letters, tone = match.group(1).lower().replace("u:", "v"), match.group(2)
    if not tone or tone == "5":
        return letters.replace("v", "ü")

    # Standard placement: a and e always win; in "ou" the o takes it;
    # otherwise it lands on the last vowel.
    order = "aoe"
    target = -1
    for vowel in order:
        if vowel in letters:
            target = letters.index(vowel)
            break
    if target < 0 and "ou" in letters:
        target = letters.index("o")
    if target < 0:
        for i in range(len(letters) - 1, -1, -1):
            if letters[i] in "iuv":
                target = i
                break
    if target < 0:
        return letters.replace("v", "ü")

    marked = TONES[letters[target]][int(tone) - 1]
    return (letters[:target] + marked + letters[target + 1:]).replace("v", "ü")


def to_pinyin(reading):
    return " ".join(numbered_to_marked(s) for s in reading.split())


def clean_definition(definition):
    definition = STRIP.sub("", definition).strip()
    # Parenthetical qualifiers are noise on a card 320 px wide. Whitespace is
    # collapsed AFTER they are cut, or "to be (located) at" leaves a double
    # space behind where the parenthesis was.
    definition = re.sub(r"\([^)]*\)", " ", definition)
    definition = re.sub(r"\s+", " ", definition).strip(" ,;")
    return definition


def score(definition, position):
    """
    Lower is better. Position dominates, because CC-CEDICT already lists the
    senses in the order a reader wants them — the scoring only breaks ties and
    rejects the pathological. Length is pulled toward ten characters rather
    than minimised: shortest-wins picked "a" for 一 over "one", and "1" over
    both.
    """
    penalty = position * 4
    penalty += abs(len(definition) - 10)
    if len(definition) < 3:
        penalty += 40
    if definition.isdigit():
        penalty += 60
    penalty += 4 * definition.count(";")
    if definition.startswith("to "):
        penalty -= 3          # verbs read fine as "to run"
    return penalty


def load_cedict():
    """simplified -> list of (pinyin, best-definition, score) per reading."""
    raw = fetch(CEDICT_URL, binary=True)
    entries = {}
    text = gzip.decompress(raw).decode("utf-8")
    pattern = re.compile(r"^(\S+) (\S+) \[([^\]]*)\] /(.*)/$")
    for line in text.splitlines():
        if line.startswith("#"):
            continue
        match = pattern.match(line)
        if not match:
            continue
        simplified, reading, body = match.group(2), match.group(3), match.group(4)
        if len(simplified) != 1:
            continue
        # A capitalised reading is a proper noun in CEDICT's convention.
        if reading[:1].isupper():
            continue

        best, bestScore = None, None
        for position, definition in enumerate(body.split("/")):
            if position > 5:
                break         # the tail of a CEDICT entry is the rare senses
            definition = definition.strip()
            if not definition or REJECT.match(definition):
                continue
            definition = clean_definition(definition)
            if not definition or len(definition) > 26:
                continue
            value = score(definition, position)
            if bestScore is None or value < bestScore:
                best, bestScore = definition, value
        if best:
            # Reading order is meaningful in CEDICT — the common pronunciation
            # is listed first — so a later reading has to be a lot better to
            # win. Without this weight 的 came out as dí "really and truly",
            # its fourth reading, because that gloss happens to score well.
            bucket = entries.setdefault(simplified, [])
            entries[simplified] = bucket
            bucket.append((to_pinyin(reading), best, bestScore + 14 * len(bucket)))
    return entries


def load_frequency():
    raw = fetch(FREQ_URL, binary=True).decode("gb18030", "replace")
    block = raw[raw.find("<pre"):]
    order = []
    for row in block.split("<br>"):
        cells = re.sub(r"<[^>]+>", "", row).strip().split("\t")
        if len(cells) < 2:
            continue
        char = cells[1].strip()
        if len(char) == 1 and "一" <= char <= "鿿":
            order.append(char)
    return order


def main():
    print("fetching frequency order ...")
    order = load_frequency()
    print("  %d characters ranked" % len(order))

    print("fetching CC-CEDICT ...")
    cedict = load_cedict()
    print("  %d single characters glossed" % len(cedict))

    rows = []
    skipped = 0
    for char in order:
        if len(rows) >= WANTED:
            break
        readings = cedict.get(char)
        if not readings:
            skipped += 1
            continue
        # The best gloss across ALL readings, not the first reading's. File
        # order is not usefulness — CEDICT lists 了 under liao3 before le.
        pinyin, definition, _ = min(readings, key=lambda r: r[2])
        rows.append((char, pinyin, definition))

    if len(rows) < WANTED:
        print("WARNING: only %d of %d wanted" % (len(rows), WANTED))
    print("  %d usable, %d skipped for want of a clean gloss" % (len(rows), skipped))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("# The %d most frequent Chinese characters, most common first.\n" % len(rows))
        handle.write("# GENERATED by Tools/buildfrequency.py - do not hand-edit.\n")
        handle.write("# Order: Jun Da, Modern Chinese Character Frequency List.\n")
        handle.write("# Readings and meanings: CC-CEDICT, CC BY-SA 4.0.\n")
        handle.write("# hanzi<TAB>pinyin<TAB>english\n")
        for char, pinyin, definition in rows:
            handle.write("%s\t%s\t%s\n" % (char, pinyin, definition))

    print("wrote %s (%.0f KB)" % (os.path.relpath(OUT, ROOT), os.path.getsize(OUT) / 1024.0))
    for row in rows[:12]:
        print("   ", "  ".join(row))


if __name__ == "__main__":
    main()

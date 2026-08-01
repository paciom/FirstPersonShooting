# Chinese Quest assets

Everything the Chinese learning mode needs that cannot be generated from a
script at runtime. Both files here are **built by tools, not authored** — do
not hand-edit them; edit `Assets/Scripts/Chinese/ChineseLexicon.cs` and re-run
the two commands below.

## `frequency.txt` — the INFINITE deck

The 2000 most frequent Chinese characters, most common first — the syllabus
deck, as against the eleven hand-picked themed ones in `ChineseLexicon.cs`.

```bash
python Tools/buildfrequency.py
```

Joins two public sources. **Order** is Jun Da's Modern Chinese Character
Frequency List (lingua.mtsu.edu), derived from a 258-million-character corpus.
**Readings and meanings** are CC-CEDICT (CC BY-SA 4.0) — Jun Da's file carries
glosses too, but a character with one reading and four senses is formatted
identically to one with four readings, so they cannot be split reliably.

A TextAsset rather than generated C#: 2000 entries is data, and data that large
in source is a file nobody can read and every diff has to scroll past.
`ChineseProgress` is what walks it — frequency order for new words, a 50
question gap before any repeat, retirement after three right in a row.

Both tools below read this file as well as the lexicon, so re-running
`buildfrequency.py` means re-running both.

## `NotoSansSC-Quest.ttf` — the glyphs

Unity's built-in `LegacyRuntime.ttf`, which every other screen in this project
draws with, contains no CJK glyphs. A missing glyph in legacy UI `Text` is a
silent blank box, not an error. Desktop Unity can fall back to an OS font, but
a WebGL build has no OS fonts, so without this file the mode ships as rows of
empty rectangles.

This is [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)
2.04, **SIL Open Font License 1.1** (redistribution permitted; the licence text
travels inside the font's own name table). The full family is ~18 MB of 31,000
glyphs, so `Tools/subsetfont.py` pins the variable weight axis to Medium 500
and cuts it to the ~2,000 characters these modes can actually draw — 640 KB.

```bash
python Tools/subsetfont.py
```

The subsetter reads every string literal in `Assets/Scripts/Chinese/*.cs`, so
Chinese added to the lexicon **or** to the mode's own UI text is picked up
automatically. `ChineseFont.cs` logs a warning naming the script if the game
ever asks for a character the packed font is missing.

## `Voice/*.wav` — the pronunciations

The mode reads each word out loud when the player gets it right, and again
when it reveals a missed answer. Unity has no text-to-speech and the browser's
`SpeechSynthesis` API only exists in WebGL, so the clips are baked offline with
Windows' own zh-CN SAPI voice and ship as ordinary AudioClips that behave the
same on every platform.

```bash
powershell -ExecutionPolicy Bypass -File Tools/chinesevoice.ps1
```

Only missing clips are baked; pass `-Force` to redo them all. Needs a Chinese
voice installed (Settings → Time & language → Language & region → Chinese
(Simplified) → Language options → Speech; "Microsoft Huihui" is the usual one).
The script skips itself with a clear error if none is present.

Each clip is **trimmed and peak-normalised** on the way out, which is not a
nicety. Raw SAPI output ran from 3,908 to 28,973 peak across the set — a 17 dB
spread that left quiet words inaudible under the victory sting, reading as "the
pronunciation is broken" rather than "the pronunciation is quiet" — and padded
every word with ~0.2 s of lead-in and up to a second of trailing room. The clip
fires the moment an answer is chosen, so that lead-in was latency on the one
sound the mode exists to deliver. After polishing: uniform −1 dBFS, 0.25–0.88 s,
2.0 MB for the set instead of 7.1 MB.

Filenames are the word's tone-numbered pinyin — `xióng māo` → `xiong2_mao1` —
which is `ChineseLexicon.ToneSlug` in C# and `Get-ToneSlug` in the script. Those
two must agree; changing one without the other orphans every clip. Homophones
share a file on purpose (心 and 新 are both `xin1`, and both are correct).

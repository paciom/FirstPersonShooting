# Chinese Quest assets

Everything the Chinese learning mode needs that cannot be generated from a
script at runtime. Both files here are **built by tools, not authored** — do
not hand-edit them; edit `Assets/Scripts/Chinese/ChineseLexicon.cs` and re-run
the two commands below.

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
and cuts it to the ~190 characters this mode can actually draw — 121 KB.

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
every word with ~0.2 s of lead-in and up to a second of trailing room.
`ChineseVoice.LengthOf` feeds the reveal beat, so that silence was also dead
screen time. After polishing: uniform −1 dBFS, 0.25–0.88 s, 2.0 MB for the set
instead of 7.1 MB.

Filenames are the word's tone-numbered pinyin — `xióng māo` → `xiong2_mao1` —
which is `ChineseLexicon.ToneSlug` in C# and `Get-ToneSlug` in the script. Those
two must agree; changing one without the other orphans every clip. Homophones
share a file on purpose (心 and 新 are both `xin1`, and both are correct).

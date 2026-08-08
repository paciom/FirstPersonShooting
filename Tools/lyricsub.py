"""Times known lyrics against a generated song and emits SRT entries.

MiniMax's music endpoint returns audio only — no word timing — but we hold
the exact lyrics we asked it to sing. So: faster-whisper transcribes the
track with word timestamps, the transcript words are sequence-aligned to the
lyric words (difflib on normalized tokens), and each lyric LINE takes the
span of its matched transcript words. Lines nothing matched (mishears are
common on sung vocals) are interpolated between their timed neighbours, so
every line lands somewhere sensible.

  python lyricsub.py song.mp3 lyrics.txt --offset 311.9 --json out.json

Emits a JSON list of {start, end, text} in FINAL-MIX seconds (offset added),
one per lyric line; a driver script merges the per-track lists into one SRT.
"""
import argparse
import difflib
import json
import re
import sys

TAG = re.compile(r"^\[.*\]$")


def norm(word):
    return re.sub(r"[^a-z0-9']", "", word.lower())


def lyric_lines(path):
    lines = []
    with open(path, encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if not line or TAG.match(line):
                continue
            words = [norm(w) for w in line.split()]
            words = [w for w in words if w]
            if words:
                lines.append({"text": line, "words": words})
    return lines


def transcribe(path):
    from faster_whisper import WhisperModel
    model = WhisperModel("small", device="cpu", compute_type="int8")
    segments, _ = model.transcribe(path, language="en", word_timestamps=True,
                                   vad_filter=False)
    words = []
    for segment in segments:
        for word in segment.words or []:
            token = norm(word.word)
            if token:
                words.append({"w": token, "start": word.start, "end": word.end})
    return words


def align(lines, words):
    flat = []
    for index, line in enumerate(lines):
        for word in line["words"]:
            flat.append((index, word))
    matcher = difflib.SequenceMatcher(
        a=[entry[1] for entry in flat],
        b=[word["w"] for word in words], autojunk=False)

    starts = {}
    ends = {}
    for block in matcher.get_matching_blocks():
        for step in range(block.size):
            line_index = flat[block.a + step][0]
            timed = words[block.b + step]
            starts.setdefault(line_index, timed["start"])
            starts[line_index] = min(starts[line_index], timed["start"])
            ends[line_index] = max(ends.get(line_index, 0), timed["end"])

    timed_lines = []
    for index, line in enumerate(lines):
        timed_lines.append({
            "text": line["text"],
            "start": starts.get(index),
            "end": ends.get(index),
        })

    # Interpolate the unmatched: each run of missed lines between two timed
    # neighbours splits that gap evenly, so a mishear still scrolls past at a
    # plausible moment instead of vanishing.
    index = 0
    while index < len(timed_lines):
        if timed_lines[index]["start"] is not None:
            index += 1
            continue
        run_start = index
        while index < len(timed_lines) and timed_lines[index]["start"] is None:
            index += 1
        prev_end = timed_lines[run_start - 1]["end"] if run_start > 0 else 0.0
        next_start = (timed_lines[index]["start"] if index < len(timed_lines)
                      else prev_end + 4.0 * (index - run_start))
        span = max(next_start - prev_end, 1.0 * (index - run_start))
        slot = span / (index - run_start)
        for step, j in enumerate(range(run_start, index)):
            timed_lines[j]["start"] = prev_end + slot * step + slot * 0.1
            timed_lines[j]["end"] = prev_end + slot * (step + 1) - slot * 0.1

    # Clamp overlaps introduced by interpolation.
    for index in range(1, len(timed_lines)):
        if timed_lines[index]["start"] < timed_lines[index - 1]["end"]:
            timed_lines[index]["start"] = timed_lines[index - 1]["end"]
            if timed_lines[index]["end"] < timed_lines[index]["start"] + 0.8:
                timed_lines[index]["end"] = timed_lines[index]["start"] + 0.8
    return timed_lines


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("audio")
    parser.add_argument("lyrics")
    parser.add_argument("--offset", type=float, default=0.0)
    parser.add_argument("--json", required=True)
    args = parser.parse_args()

    lines = lyric_lines(args.lyrics)
    if not lines:
        sys.exit("no lyric lines found")
    words = transcribe(args.audio)
    print(f"  {len(words)} transcript words vs {len(lines)} lyric lines")
    timed = align(lines, words)
    matched = sum(1 for line in timed if line["start"] is not None)
    for line in timed:
        line["start"] += args.offset
        line["end"] += args.offset
    with open(args.json, "w", encoding="utf-8") as handle:
        json.dump(timed, handle, indent=1)
    print(f"  wrote {args.json} ({matched}/{len(timed)} lines timed)")


if __name__ == "__main__":
    main()

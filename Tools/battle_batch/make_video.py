"""Scores one recorded battle from the song pool.

  python make_video.py battle07

Song selection is deterministic: video i takes pool songs (3(i-1) + 13j) mod
60 for j = 0.. until the soundtrack covers the tape — stride 13 is coprime to
60, so a video never repeats a song and neighbouring videos share only a few.
The overage is trimmed evenly from every track EXCEPT the last, whose natural
ending lands on the video's final frame; 2 s crossfades hide the cuts.
Subtitles come from the pool's offset-zero Whisper timings, shifted to each
song's place in this video, with a 4 s title card opening every song.
"""
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from songs_a import SONGS_A
from songs_b import SONGS_B
from songs_c import SONGS_C

HERE = os.path.dirname(os.path.abspath(__file__))
POOL = os.path.join(HERE, "pool")
RENDERS = r"D:\Claude\FirstPersongShooting\Renders"
# Deal only from songs whose audio actually exists — the pool can run short
# of the full 60 when MiniMax credits run out mid-batch.
SONGS = [song for song in SONGS_A + SONGS_B + SONGS_C
         if os.path.exists(os.path.join(POOL, song["id"] + ".mp3"))]
TITLE = {song["id"]: song["title"] for song in SONGS}
FADE = 2.0
CARD = 4.0


def run(args):
    return subprocess.run(args, check=True, capture_output=True, text=True)


def duration(path):
    reply = run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                 "-of", "csv=p=0", path])
    return float(reply.stdout.strip())


def main():
    name = sys.argv[1]
    index = int(re.search(r"(\d+)$", name).group(1))
    video = os.path.join(RENDERS, name + ".mp4")
    target = duration(video)

    picks = []
    effective = 0.0
    j = 0
    while effective < target:
        song = SONGS[(3 * (index - 1) + 13 * j) % len(SONGS)]["id"]
        length = duration(os.path.join(POOL, song + ".mp3"))
        picks.append([song, length])
        effective = sum(p[1] for p in picks) - FADE * (len(picks) - 1)
        j += 1
    overage = effective - target
    # Every track but the last gives up an equal slice; the last keeps its
    # written ending for the win card.
    cut = overage / (len(picks) - 1)
    trimmed = [[song, length - cut] for song, length in picks[:-1]] + [picks[-1]]
    print(f"{name}: target {target:.1f}s, {len(picks)} songs, "
          f"cut {cut:.1f}s from each of {len(picks) - 1}")

    inputs = []
    for song, _ in trimmed:
        inputs += ["-i", os.path.join(POOL, song + ".mp3")]
    chain = ""
    for k, (song, length) in enumerate(trimmed[:-1]):
        chain += f"[{k}:a]atrim=0:{length:.3f},asetpts=PTS-STARTPTS[t{k}];"
    chain += f"[{len(trimmed) - 1}:a]anull[t{len(trimmed) - 1}];"
    prev = "t0"
    for k in range(1, len(trimmed)):
        out = f"x{k}" if k < len(trimmed) - 1 else "mix"
        chain += f"[{prev}][t{k}]acrossfade=d={FADE}[{out}];"
        prev = out
    chain = chain.rstrip(";")
    wav = os.path.join(POOL, name + "_score.wav")
    run(["ffmpeg", "-y", "-v", "error"] + inputs +
        ["-filter_complex", chain, "-map", "[mix]", "-c:a", "pcm_s16le", wav])

    offsets = []
    at = 0.0
    for song, length in trimmed:
        offsets.append(at)
        at += length - FADE
    entries = []
    for (song, length), offset in zip(trimmed, offsets):
        entries.append({"start": offset, "end": offset + CARD,
                        "text": f"\u266a {TITLE[song]} \u266a"})
        with open(os.path.join(POOL, song + "_sub.json"), encoding="utf-8") as handle:
            for line in json.load(handle):
                if line["start"] < length - 1.0:
                    entries.append({"start": line["start"] + offset,
                                    "end": min(line["end"] + offset,
                                               offset + length),
                                    "text": line["text"]})
    entries.sort(key=lambda line: line["start"])

    def stamp(seconds):
        ms = int(round(min(seconds, target) * 1000))
        return (f"{ms // 3600000:02d}:{ms // 60000 % 60:02d}:"
                f"{ms // 1000 % 60:02d},{ms % 1000:03d}")

    srt = os.path.join(RENDERS, name + ".srt")
    with open(srt, "w", encoding="utf-8") as handle:
        for line_index, line in enumerate(entries, 1):
            handle.write(f"{line_index}\n{stamp(line['start'])} --> "
                         f"{stamp(line['end'])}\n{line['text']}\n\n")

    measure = run(["ffmpeg", "-v", "info", "-i", wav, "-af",
                   "loudnorm=I=-14:TP=-1.5:LRA=20:print_format=json",
                   "-f", "null", "-"]).stderr
    stats = json.loads(measure[measure.rindex("{"):measure.rindex("}") + 1])
    loudnorm = ("loudnorm=I=-14:TP=-1.5:LRA=20:linear=true:"
                f"measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
                f"measured_LRA={stats['input_lra']}:"
                f"measured_thresh={stats['input_thresh']}:"
                f"offset={stats['target_offset']}")
    out = os.path.join(RENDERS, name + "_scored.mp4")
    run(["ffmpeg", "-y", "-v", "error", "-i", video, "-i", wav, "-i", srt,
         "-map", "0:v", "-map", "1:a", "-map", "2:s", "-c:v", "copy",
         "-af", loudnorm, "-c:a", "aac", "-b:a", "192k",
         "-c:s", "mov_text", "-metadata:s:s:0", "language=eng", out])
    os.remove(wav)
    print(f"{name}: wrote {out} ({duration(out):.1f}s), {len(entries)} cues, "
          f"songs: {', '.join(TITLE[song] for song, _ in trimmed)}")


if __name__ == "__main__":
    main()

"""Builds the scored dogfight video: soundtrack, lyric subtitles, final mux.

Reads the eight tracks' real durations, trims the overage out of the FIRST
track's tail (the join's crossfade hides the cut), chains everything with
2-second crossfades so the last track's natural ending lands on the video's
final frame, Whisper-times every track's lyrics (lyricsub.py), and muxes:

  dogfight4v4_scored.mp4  — video stream copied, loudnormed AAC audio,
                            mov_text subtitle track
  dogfight4v4.srt         — the same subtitles for YouTube's caption upload

Run from Tools/dogfight_music:  python assemble.py
"""
import json
import subprocess
import sys

VIDEO = r"D:\Claude\FirstPersongShooting\Renders\dogfight4v4.mp4"
OUT = r"D:\Claude\FirstPersongShooting\Renders\dogfight4v4_scored.mp4"
SRT = r"D:\Claude\FirstPersongShooting\Renders\dogfight4v4.srt"
ORDER = ["t1_launch", "t2_surge", "t3_anthem", "t7_rocket", "t9_afterburner",
         "t4_hybrid", "t8_ignite", "t5_pressure", "t6_victory"]
TITLES = {
    "t1_launch": "Take Off",
    "t2_surge": "Surge",
    "t3_anthem": "Own the Sky",
    "t7_rocket": "Supersonic",
    "t9_afterburner": "Afterburner",
    "t4_hybrid": "Storm the Sky",
    "t8_ignite": "Ignite",
    "t5_pressure": "Pressure",
    "t6_victory": "Victory",
}
FADE = 2.0
TITLE_SECONDS = 4.0


def run(args, **kw):
    return subprocess.run(args, check=True, capture_output=True, text=True, **kw)


def duration(path):
    reply = run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                 "-of", "csv=p=0", path])
    return float(reply.stdout.strip())


def main():
    target = duration(VIDEO)
    lengths = {name: duration(name + ".mp3") for name in ORDER}
    raw = sum(lengths.values())
    effective = raw - FADE * (len(ORDER) - 1)
    overage = effective - target
    print(f"target {target:.2f}s  raw {raw:.2f}s  effective {effective:.2f}s  "
          f"overage {overage:+.2f}s")
    if overage < 0:
        sys.exit("soundtrack is SHORTER than the video — add or lengthen a track")

    trimmed = dict(lengths)
    trimmed[ORDER[0]] = lengths[ORDER[0]] - overage
    if trimmed[ORDER[0]] < 60:
        sys.exit(f"trim would leave {ORDER[0]} at {trimmed[ORDER[0]]:.1f}s — "
                 "spread the cut across tracks instead")

    # --- soundtrack ---------------------------------------------------------
    inputs = []
    for name in ORDER:
        inputs += ["-i", name + ".mp3"]
    chain = (f"[0:a]atrim=0:{trimmed[ORDER[0]]:.3f},asetpts=PTS-STARTPTS[a0];"
             + f"[a0][1:a]acrossfade=d={FADE}[x1];")
    for index in range(2, len(ORDER)):
        src = f"x{index - 1}"
        dst = f"x{index}" if index < len(ORDER) - 1 else "out"
        chain += f"[{src}][{index}:a]acrossfade=d={FADE}[{dst}];"
    chain = chain.rstrip(";")
    run(["ffmpeg", "-y", "-v", "error"] + inputs +
        ["-filter_complex", chain, "-map", "[out]",
         "-c:a", "pcm_s16le", "soundtrack.wav"])
    print(f"soundtrack.wav {duration('soundtrack.wav'):.3f}s")

    # --- lyric timing -------------------------------------------------------
    offsets = {}
    at = 0.0
    for index, name in enumerate(ORDER):
        offsets[name] = at
        at += trimmed[name] - (FADE if index < len(ORDER) - 1 else 0)
    entries = []
    import os
    for name in ORDER:
        print(name, flush=True)
        # Whisper is the slow step; a present sidecar means this track's
        # timing is already done (delete the _sub.json to force a redo).
        if not os.path.exists(name + "_sub.json"):
            run([sys.executable, r"..\lyricsub.py", name + ".mp3",
                 name + "_lyrics.txt",
                 "--offset", f"{offsets[name]:.3f}", "--json", name + "_sub.json"])
        with open(name + "_sub.json", encoding="utf-8") as handle:
            lines = json.load(handle)
        limit = offsets[name] + trimmed[name]
        lines = [line for line in lines if line["start"] < limit - 1.0]
        # The title card always opens the song; players stack it with an
        # early first line, which reads fine (title above, lyric below).
        entries.append({"start": offsets[name],
                        "end": offsets[name] + TITLE_SECONDS,
                        "text": f"♪ {TITLES[name]} ♪"})
        entries += lines
    entries.sort(key=lambda line: line["start"])

    def stamp(seconds):
        ms = int(round(seconds * 1000))
        return (f"{ms // 3600000:02d}:{ms // 60000 % 60:02d}:"
                f"{ms // 1000 % 60:02d},{ms % 1000:03d}")

    with open(SRT, "w", encoding="utf-8") as handle:
        for index, line in enumerate(entries, 1):
            end = min(line["end"], target)
            handle.write(f"{index}\n{stamp(line['start'])} --> {stamp(end)}\n"
                         f"{line['text']}\n\n")
    print(f"wrote {SRT} ({len(entries)} lines)")

    # --- loudness + mux -----------------------------------------------------
    measure = run(["ffmpeg", "-v", "info", "-i", "soundtrack.wav", "-af",
                   "loudnorm=I=-14:TP=-1.5:LRA=20:print_format=json",
                   "-f", "null", "-"]).stderr
    stats = json.loads(measure[measure.rindex("{"):measure.rindex("}") + 1])
    loudnorm = ("loudnorm=I=-14:TP=-1.5:LRA=20:linear=true:"
                f"measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
                f"measured_LRA={stats['input_lra']}:"
                f"measured_thresh={stats['input_thresh']}:"
                f"offset={stats['target_offset']}")
    run(["ffmpeg", "-y", "-v", "error", "-i", VIDEO, "-i", "soundtrack.wav",
         "-i", SRT, "-map", "0:v", "-map", "1:a", "-map", "2:s",
         # No -shortest: the subtitle stream ends at its last cue, minutes
         # before the video does, and -shortest would truncate to IT —
         # audio and video already match by construction.
         "-c:v", "copy", "-af", loudnorm, "-c:a", "aac", "-b:a", "192k",
         "-c:s", "mov_text", "-metadata:s:s:0", "language=eng", OUT])
    print(f"wrote {OUT} {duration(OUT):.3f}s")
    print("track starts: " + "  ".join(
        f"{name}@{offsets[name]:.0f}s" for name in ORDER))


if __name__ == "__main__":
    main()

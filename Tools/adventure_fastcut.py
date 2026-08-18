#!/usr/bin/env python3
"""Cut a beat FAST: many short cuts from the clips already generated.

    python Tools/adventure_fastcut.py n01
    python Tools/adventure_fastcut.py n01 --out Renders/adventure/n01_fast.mp4

A beat's nine clips are ~4.06s each, which is 36 seconds of material for a
30-second beat. The ordinary cut (Tools/adventure_video.py) uses one slice per
clip and lands on ~3.3s a shot, which reads slow. This one takes TWO slices out
of most clips at different in-points, punches in on some of them, and speeds up
the action beats — seventeen cuts in thirty seconds, averaging 1.76s, with no
new generation and no new spend.

Two things it does that a naive concat does not:

  * Punch-ins. Re-using a clip twice reads as a repeat unless the second slice
    is framed differently, so those slices crop in 10-20% and rescale. Same
    material, different shot.
  * One continuous audio bed. Cutting the video every 1.8s and taking each
    slice's audio with it makes the rain stutter at every join, so the audio is
    built separately from the clips in story order and laid under the whole
    thing.
"""

import argparse
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIPS = os.path.join(ROOT, "Tools", "adventure_clips")
OUT_DIR = os.path.join(ROOT, "Renders", "adventure")
WORK = os.path.join(CLIPS, "_fast")
FPS = 30

# (source shot, in-point, output seconds, zoom, speed). Order is the story
# order of the beat; the repeats are deliberate — a beat cut this fast returns
# to a face or a hand rather than showing it once and moving on.
CUTS = {
    "n01": [
        (1, 0.20, 1.2, 1.00, 1.0),   # the furnaces, wide
        (1, 2.40, 1.0, 1.15, 1.0),   # punch in on the molten glow
        (2, 0.30, 1.6, 1.00, 1.0),   # Titan rises into frame
        (2, 2.20, 1.2, 1.12, 1.0),   # closer on the bulk
        (3, 0.20, 2.4, 1.00, 1.0),   # the hand opens, empty
        (4, 0.40, 1.2, 1.00, 1.0),   # the visor narrows
        (4, 2.00, 1.0, 1.20, 1.0),   # right into the slit
        (3, 2.40, 1.4, 1.08, 1.0),   # back to the hand, still empty
        (5, 0.30, 2.2, 1.00, 1.0),   # fingers fold shut on nothing
        (5, 2.40, 1.0, 1.15, 1.0),   # rain off the knuckles
        (6, 0.20, 2.2, 1.00, 1.15),  # the sprint, pushed
        (6, 2.60, 1.2, 1.10, 1.20),  # faster still
        (7, 0.10, 2.8, 1.00, 1.10),  # over the rail
        (7, 2.60, 1.4, 1.00, 1.00),  # the fall
        (8, 0.30, 2.4, 1.00, 1.00),  # the landing
        (9, 0.30, 3.2, 1.00, 1.00),  # he stops dead
        (9, 1.40, 2.6, 1.10, 1.00),  # and the shadow behind him
    ],
}


def source(node, shot):
    return os.path.join(CLIPS, f"{node}_s{shot}.mp4")


def build(node, out):
    cuts = CUTS.get(node)
    if not cuts:
        sys.exit(f"no fast-cut plan for {node}; add one to CUTS")
    os.makedirs(WORK, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)

    total = sum(c[2] for c in cuts)
    print(f"{node}: {len(cuts)} cuts, {total:.1f}s, average {total/len(cuts):.2f}s a cut")

    parts = []
    for i, (shot, start, seconds, zoom, speed) in enumerate(cuts):
        src = source(node, shot)
        if not os.path.exists(src):
            sys.exit("missing clip: " + src)
        part = os.path.join(WORK, f"{node}_{i:02d}.mp4")
        # A sped-up slice has to READ more source than it plays.
        need = seconds * speed
        chain = []
        if zoom > 1.0:
            chain.append(f"crop=iw/{zoom:.4f}:ih/{zoom:.4f}")
        chain.append("scale=1280:720:force_original_aspect_ratio=increase")
        chain.append("crop=1280:720")
        if speed != 1.0:
            chain.append(f"setpts=PTS/{speed:.4f}")
        chain.append(f"fps={FPS}")
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                        "-ss", f"{start:.2f}", "-i", src, "-t", f"{need:.2f}",
                        "-vf", ",".join(chain), "-an",
                        "-c:v", "libx264", "-preset", "medium", "-crf", "19",
                        part], check=True)
        parts.append(part)

    listing = os.path.join(WORK, node + "_video.txt")
    with open(listing, "w", encoding="utf-8") as handle:
        for part in parts:
            handle.write("file '%s'\n" % part.replace("\\", "/"))
    silent = os.path.join(WORK, node + "_silent.mp4")
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "concat", "-safe", "0", "-i", listing,
                    "-c", "copy", silent], check=True)

    # The bed: every clip's audio in story order, laid under the whole cut so
    # the rain does not restart seventeen times.
    shots = sorted({c[0] for c in cuts})
    bed_list = os.path.join(WORK, node + "_audio.txt")
    with open(bed_list, "w", encoding="utf-8") as handle:
        for shot in shots:
            handle.write("file '%s'\n" % source(node, shot).replace("\\", "/"))
    bed = os.path.join(WORK, node + "_bed.m4a")
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "concat", "-safe", "0", "-i", bed_list,
                    "-vn", "-t", f"{total:.2f}",
                    "-af", f"afade=t=in:st=0:d=0.4,afade=t=out:st={total-0.6:.2f}:d=0.6",
                    "-c:a", "aac", "-b:a", "160k", bed], check=True)

    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                    "-i", silent, "-i", bed, "-map", "0:v", "-map", "1:a",
                    "-c:v", "copy", "-c:a", "copy", "-shortest", out], check=True)
    print("wrote", out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("node")
    parser.add_argument("--out", default="")
    args = parser.parse_args()
    out = args.out or os.path.join(OUT_DIR, args.node + "_fast.mp4")
    build(args.node, out)


if __name__ == "__main__":
    main()

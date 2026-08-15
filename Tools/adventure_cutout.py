#!/usr/bin/env python3
"""Cut the real robots, jets and tanks out of their reference renders.

    python Tools/adventure_cutout.py            # build every missing cutout
    python Tools/adventure_cutout.py --force    # rebuild all
    python Tools/adventure_cutout.py --list     # what the library holds

The adventure stills are composited, not painted: an image model paints the
ROOM and these cutouts put the ACTUAL characters in it. That split is the
house rule (see the menu art pipeline) and it exists because a painted robot
is somebody else's robot — text prompts reliably invent a different head.

Sources, all flat-background renders already in the repo:
  ExternalData/<Robot>.PNG              hero pose, flat gray
  ExternalData/JetRefs/<Robot>/*.png    six jet angles, flat gray
  ExternalData/<Robot>_Tank_*.mp4       tank form, last frame (needs ffmpeg)
  PreviewCaptures/<ROBOT>_front.png     neutral pose on flat navy

Output: Tools/adventure_cutouts/<name>.png, RGBA, tightly trimmed.

The keying is flood-fill-from-the-border rather than a plain colour distance,
so dark navy ON the robot survives against a dark navy background; then only
the largest remaining blob is kept, which is what drops the "Ai" watermark and
the video letterbox bars without hand-cropping every file.
"""

import os
import subprocess
import sys
from collections import deque

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Tools", "adventure_cutouts")
EXTERNAL = os.path.join(ROOT, "ExternalData")
PREVIEWS = os.path.join(ROOT, "PreviewCaptures")

# name -> (kind, source). Jet angles: 1 nose-on ... 6 rear. See --list.
ROBOTS = ["Panther", "Titan", "Samurai", "Scout", "Racer", "Hawk", "Knite"]
JET_DIRS = {"Panther": "Panthor", "Titan": "Titan", "Samurai": "Samurai",
            "Scout": "Scout", "Racer": "Racer", "Hawk": "Hawk", "Bolt": "Bolt",
            "Knight": "Knight", "Ranger": "Ranger"}
TANKS = {"Panther": "Panthor_Tank_Transformation.mp4",
         "Titan": "Titan_Tank_Transformation.mp4",
         "Samurai": "Samurai_Tank_Transformation.mp4",
         "Hawk": "Hawk_Tank_Transformation.mp4",
         "Knight": "Knight_Tank_Transformation.mp4",
         "Scout": "Scout_Tank_Transformation.mp4"}
PREVIEW_ROBOTS = ["PANTHER", "TITAN", "SAMURAI", "BOLT", "RANGER", "SCOUT",
                  "HAWK", "KNIGHT", "RACER"]


def sources():
    """name -> (path, extract_frame_from_video)."""
    found = {}
    for robot in ROBOTS:
        path = os.path.join(EXTERNAL, robot + ".PNG")
        if os.path.exists(path):
            name = "knight" if robot == "Knite" else robot.lower()
            found[name + "_hero"] = (path, None)
    for robot, folder in JET_DIRS.items():
        base = os.path.join(EXTERNAL, "JetRefs", folder)
        for i in range(1, 7):
            path = os.path.join(base, f"angle_{i}.png")
            if os.path.exists(path):
                found[f"{robot.lower()}_jet{i}"] = (path, None)
    for robot, clip in TANKS.items():
        path = os.path.join(EXTERNAL, clip)
        if os.path.exists(path):
            found[f"{robot.lower()}_tank"] = (path, True)
    for robot in PREVIEW_ROBOTS:
        for face in ("front", "back"):
            path = os.path.join(PREVIEWS, f"{robot}_{face}.png")
            if os.path.exists(path):
                found[f"{robot.lower()}_{face}"] = (path, None)
    return found


def video_frame(path, cache):
    """Last frame of a transformation clip: the finished vehicle."""
    if not os.path.exists(cache):
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                        "-sseof", "-0.6", "-i", path, "-frames:v", "1", cache],
                       check=True)
    return cache


def field_colour(rgb):
    """The background the subject sits on, sampled from the SIDE columns at
    mid height. Not the whole border: a video frame's letterbox bars would
    drag the median away from the field they frame."""
    h = rgb.shape[0]
    band = slice(h // 4, 3 * h // 4)
    sides = np.concatenate([rgb[band, 0], rgb[band, 1], rgb[band, -1], rgb[band, -2]])
    return np.median(sides, axis=0)


def trim_bars(image):
    """Drop edge rows and columns that are not the field: letterbox bars from
    the transformation clips, and the UI chrome baked into some renders. A row
    goes if its median colour is nowhere near the field colour — which is true
    of a bar even when a watermark makes the row itself non-uniform."""
    rgb = np.asarray(image.convert("RGB"), dtype=np.float32)
    bg = field_colour(rgb)

    def far(line):
        return np.sqrt(((np.median(line, axis=0) - bg) ** 2).sum()) > 40.0

    top, bottom, left, right = 0, rgb.shape[0], 0, rgb.shape[1]
    while bottom - top > 64 and far(rgb[top]):
        top += 1
    while bottom - top > 64 and far(rgb[bottom - 1]):
        bottom -= 1
    while right - left > 64 and far(rgb[top:bottom, left]):
        left += 1
    while right - left > 64 and far(rgb[top:bottom, right - 1]):
        right -= 1
    return image.crop((left, top, right, bottom))


def key_out(image):
    """RGBA with the flat background removed, trimmed to the subject."""
    image = trim_bars(image)
    rgb = np.asarray(image.convert("RGB"), dtype=np.int16)
    h, w = rgb.shape[:2]

    bg = field_colour(rgb.astype(np.float32))
    dist = np.sqrt(((rgb - bg) ** 2).sum(axis=2))

    # Flood fill the background in from every edge, through pixels close to
    # the background colour. Interior pixels that happen to match (navy on a
    # navy field) are never reached, so they survive.
    tol = 26.0
    close = dist < tol
    is_bg = np.zeros((h, w), dtype=bool)
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if close[y, x] and not is_bg[y, x]:
                is_bg[y, x] = True
                queue.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if close[y, x] and not is_bg[y, x]:
                is_bg[y, x] = True
                queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < h and 0 <= nx < w and close[ny, nx] and not is_bg[ny, nx]:
                is_bg[ny, nx] = True
                queue.append((ny, nx))

    solid = ~is_bg
    # Keep only the biggest blob: kills the "Ai" watermark, UI chrome and the
    # letterbox bars that survive as their own islands.
    label = np.zeros((h, w), dtype=np.int32)
    best, best_size, current = 0, 0, 0
    for sy in range(h):
        for sx in range(w):
            if not solid[sy, sx] or label[sy, sx]:
                continue
            current += 1
            size = 0
            label[sy, sx] = current
            queue.append((sy, sx))
            while queue:
                y, x = queue.popleft()
                size += 1
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if (0 <= ny < h and 0 <= nx < w and solid[ny, nx]
                            and not label[ny, nx]):
                        label[ny, nx] = current
                        queue.append((ny, nx))
            if size > best_size:
                best, best_size = current, size
    keep = label == best

    # Alpha comes from the FILL MASK, not from colour distance: a dark cockpit
    # canopy or a navy panel reads as "close to the background" and a
    # distance-based alpha punches a hole straight through the middle of the
    # subject. Blur the binary mask by a pixel so the silhouette is not a
    # staircase, and let the blur only ever soften the edge inward.
    solid_alpha = Image.fromarray((keep * 255).astype(np.uint8), "L")
    alpha = np.asarray(solid_alpha.filter(ImageFilter.GaussianBlur(0.9)),
                       dtype=np.float32) / 255.0
    alpha[~keep] = np.minimum(alpha[~keep], 0.5)   # a half-pixel of overhang only

    # Despill: pull the flat grey out of the semi-transparent rim.
    rgbf = rgb.astype(np.float32)
    safe = np.maximum(alpha, 1e-3)[..., None]
    unmixed = np.clip((rgbf - bg[None, None, :] * (1.0 - safe)) / safe, 0, 255)

    out = np.dstack([unmixed.astype(np.uint8), (alpha * 255).astype(np.uint8)])
    cut = Image.fromarray(out, "RGBA")
    return cut.crop(cut.getbbox())


def build(name, path, is_video, force=False):
    target = os.path.join(OUT, name + ".png")
    if os.path.exists(target) and not force:
        return "skip"
    if is_video:
        path = video_frame(path, os.path.join(OUT, "_frames", name + ".png"))
    cut = key_out(Image.open(path))
    cut.save(target)
    return f"{cut.width}x{cut.height}"


def main(argv):
    force = "--force" in argv
    found = sources()
    if "--list" in argv:
        for name in sorted(found):
            print(" ", name)
        print(f"{len(found)} sources")
        return
    os.makedirs(os.path.join(OUT, "_frames"), exist_ok=True)
    wanted = [a for a in argv if not a.startswith("--")] or sorted(found)
    for name in wanted:
        if name not in found:
            sys.exit("unknown cutout: " + name)
        path, is_video = found[name]
        try:
            print(f"  {name:24s} {build(name, path, is_video, force)}")
        except Exception as error:
            print(f"  {name:24s} FAIL {error}")


if __name__ == "__main__":
    main(sys.argv[1:])

"""Render each hero's JET — the last stage of its transformation — to a cutout.

    python Tools/jetstills.py            # all nine, into Tools/jet_render
    python Tools/jetstills.py ranger     # just one

The jet transformation clips in Assets/Video are the *source* the stages were
built from, and they carry a generator watermark in the corner. The stages
themselves do not: `Assets/Models/Stages/<hero>-jet/stage8.glb` IS the jet the
game flies in DOGFIGHT, so rendering it is both cleaner and more honest than
grabbing a frame off the video.

Output is RGBA at `SIZE`, cropped to the jet. Two consumers, both of which want
a jet they can drop onto something else:

  Tools/menucomposite.py   — jets over the title screen's line-up
  Tools/build_site_assets.py — the roster and hero band on jah.cc
"""

import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import previewglb as P  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
STAGES = os.path.join(ROOT, "Assets", "Models", "Stages")
OUT = os.path.join(HERE, "jet_render")

HEROES = ["ranger", "titan", "scout", "hawk", "bolt",
          "samurai", "panther", "knight", "racer"]

SIZE = 720
# Rendered at 2x and shrunk: the rasteriser has no antialiasing of its own, and
# a jet is mostly long thin edges, which is exactly what aliasing ruins.
SUPER = 2
# Three-quarter front, nose to the left — a jet reads as fast from the front
# quarter and as a plank from the side.
YAW = 62.0


def jet(hero, size=SIZE, yaw=YAW):
    stages = sorted(
        (f for f in os.listdir(os.path.join(STAGES, f"{hero}-jet"))
         if f.startswith("stage") and f.endswith(".glb")),
        key=lambda f: int(f[5:-4]))
    if not stages:
        raise RuntimeError(f"{hero}: no jet stages")
    path = os.path.join(STAGES, f"{hero}-jet", stages[-1])

    big = P.render(path, size=size * SUPER, yaw=yaw, alpha=True)

    # Premultiply, shrink, un-premultiply. Skipping this leaves every edge
    # ringed in the renderer's navy backdrop.
    src = np.asarray(big, dtype=np.float32)
    a = src[..., 3:4] / 255.0
    pre = np.concatenate([src[..., :3] * a, src[..., 3:4]], axis=2)
    small = np.asarray(
        Image.fromarray(pre.astype(np.uint8), "RGBA").resize(
            (size, size), Image.LANCZOS), dtype=np.float32)
    sa = small[..., 3:4] / 255.0
    rgb = np.where(sa > 0.004, small[..., :3] / np.maximum(sa, 1e-4), 0.0)
    out = Image.fromarray(
        np.concatenate([np.clip(rgb, 0, 255), small[..., 3:4]], axis=2)
        .astype(np.uint8), "RGBA")

    box = out.getbbox()
    return out.crop(box) if box else out


# The flight: which jet sits where on a 16:9 sky, as fractions of the frame.
# (hero, centre x, centre y, width, degrees of bank, mirrored, opacity)
#
# Everything is kept out of the middle-top box, because that is where the title
# lockup lands on BOTH consumers — the game's menu and the web hero — and where
# the line-up stands below it. What is left is the two side skies.
#
# Two edges bound those skies. The web hero covers this layer, and at a laptop's
# shape that crops about 9% off each side, so nothing may live outside x 0.09 to
# 0.91. The game's menu lays its card deck over the bottom 41%, so nothing may
# hang below y 0.40 either. Between them: a band down each side.
FLIGHT = [
    ("ranger",  0.175, 0.172, 0.150,  -8.0, False, 1.00),
    ("panther", 0.825, 0.166, 0.125,   7.0, True,  1.00),
    ("bolt",    0.865, 0.315, 0.070, -10.0, False, 0.75),
]


def flight(size=(1920, 1080), cache=None):
    """The three-jet sky both the title screen and the web hero fly."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    for hero, cx, cy, w, bank, mirror, alpha in FLIGHT:
        src = (cache or {}).get(hero)
        if src is None:
            path = os.path.join(OUT, f"{hero}.png")
            src = Image.open(path).convert("RGBA") if os.path.exists(path) \
                else jet(hero)
        if mirror:
            src = src.transpose(Image.FLIP_LEFT_RIGHT)

        width = max(8, int(size[0] * w))
        src = src.resize((width, max(1, round(src.height * width / src.width))),
                         Image.LANCZOS)
        src = src.rotate(bank, resample=Image.BICUBIC, expand=True)
        if alpha < 1.0:
            faded = src.getchannel("A").point(lambda v: int(v * alpha))
            src.putalpha(faded)

        # An engine-lit haze, so a jet on a painted sky is lit by the same air
        # as everything else rather than pasted onto it.
        glow = Image.new("RGBA", src.size, (90, 220, 255, 0))
        glow.putalpha(src.getchannel("A").filter(
            ImageFilter.GaussianBlur(max(3, width // 22))).point(
                lambda v: int(v * 0.42)))
        at = (int(size[0] * cx - src.width / 2), int(size[1] * cy - src.height / 2))
        layer.alpha_composite(glow, at)
        layer.alpha_composite(src, at)
    return layer


def main(argv):
    os.makedirs(OUT, exist_ok=True)
    wanted = [a.lower() for a in argv] or HEROES
    cache = {}
    for hero in wanted:
        if hero not in HEROES:
            print(f"  !! unknown hero {hero}")
            continue
        img = jet(hero)
        cache[hero] = img
        path = os.path.join(OUT, f"{hero}.png")
        img.save(path)
        print(f"  {hero:9s} {img.width}x{img.height}"
              f"  {os.path.getsize(path) / 1024:5.0f} KB")

    layer = flight(cache=cache)
    path = os.path.join(OUT, "_flight.png")
    layer.save(path)
    print(f"  {'_flight':9s} {layer.width}x{layer.height}"
          f"  {os.path.getsize(path) / 1024:5.0f} KB")


if __name__ == "__main__":
    main(sys.argv[1:])

#!/usr/bin/env python3
"""Composite the adventure stills: painted room + the real robots.

    python Tools/adventure_compose.py            # every node with a plate
    python Tools/adventure_compose.py n19        # one node
    python Tools/adventure_compose.py --plates   # (re)paint missing plates only

Three stages, and the split is the whole point (see the menu art pipeline):

  1. Tools/adventure_art.py     paints the ROOM, empty of characters.
  2. Tools/adventure_cutout.py  cuts the ACTUAL robots, jets and tanks out of
                                the reference renders in ExternalData.
  3. this script                puts (2) into (1) and grades it in.

Prompting a model for "an orange panther robot" gets a handsome robot that is
not Panther, which this project has already rejected once. The cutouts are the
real models, so the character is never wrong.

The placement table below is the art direction: for each node, which cutouts
stand where, how tall, facing which way, and how far into the haze.
"""

import json
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADVENTURES = os.path.join(ROOT, "Assets", "Resources", "Adventures")
PLATES = os.path.join(ROOT, "Tools", "adventure_plates")
CUTOUTS = os.path.join(ROOT, "Tools", "adventure_cutouts")
STORY = "quiet-confirmation"
SIZE = (1280, 720)


class Place:
    """One cutout in one shot.

    x, y     where it stands, as fractions of the frame (y is the ground the
             subject stands on, not its centre — that is what keeps a robot's
             feet on the floor when you resize it)
    h        subject height as a fraction of frame height
    flip     mirror it
    haze     0 = full strength, 1 = lost in the fog. Atmospheric distance.
    shadow   contact shadow width relative to the subject; 0 for anything
             airborne
    """

    def __init__(self, cutout, x, y, h, flip=False, haze=0.0, shadow=0.9, glow=0.0,
                 rim=None, rim_from="left"):
        self.cutout, self.x, self.y, self.h = cutout, x, y, h
        self.flip, self.haze, self.shadow, self.glow = flip, haze, shadow, glow
        self.rim, self.rim_from = rim, rim_from


# Art direction, one entry per node. Nodes absent from here fall back to the
# bare plate, which is a legitimate choice for the prop and place beats.
PLACEMENT = {
    # Foundry beats are lit ember; Warden beats are lit cyan. The rim colour
    # is which side of the war is lighting the shot.
    "n01": [Place("panther_hero", 0.44, 0.95, 0.34, haze=0.10, rim=(150, 78, 26)),
            Place("samurai_hero", 0.74, 0.44, 0.11, haze=0.55, shadow=0.0)],
    "n02": [Place("panther_hero", 0.34, 0.94, 0.52, haze=0.08, rim=(40, 110, 140))],
    "n03": [Place("panther_hero", 0.50, 0.92, 0.36, haze=0.12, rim=(170, 170, 170))],
    "n04": [Place("panther_hero", 0.46, 0.90, 0.46, haze=0.08, rim=(40, 130, 160))],
    "n05": [Place("panther_hero", 0.70, 0.98, 0.76, flip=True, rim=(40, 130, 160))],
    "n06": [Place("panther_hero", 0.30, 0.96, 0.60, rim=(60, 120, 150), rim_from="right")],
    "n07": [Place("panther_hero", 0.50, 0.96, 0.58, rim=(200, 200, 200))],
    "n08": [Place("panther_hero", 0.22, 0.84, 0.30, haze=0.15, rim=(40, 120, 150))],
    "n09": [],                              # the name tag carries this one alone
    "n10": [],                              # heel print in the mud
    "n11": [Place("panther_hero", 0.30, 0.52, 0.20, haze=0.28, shadow=0.4,
                  rim=(40, 130, 160))],
    "n12": [Place("panther_back", 0.74, 1.04, 0.95, haze=0.0, shadow=0.0,
                  rim=(180, 90, 30), rim_from="right")],
    "n13": [Place("ranger_front", 0.56, 0.86, 0.30, haze=0.12, rim=(40, 130, 160))],
    "n14": [Place("panther_hero", 0.28, 0.64, 0.13, haze=0.30, shadow=0.5)],
    "n15": [Place("ranger_front", 0.30, 0.96, 0.55, haze=0.06, rim=(40, 140, 170),
                  rim_from="right")],
    "n16": [Place("samurai_hero", 0.64, 0.94, 0.52, haze=0.05, rim=(190, 130, 60)),
            Place("panther_back", 0.16, 1.06, 0.88, shadow=0.0, haze=0.0)],
    "n17": [Place("panther_hero", 0.46, 0.96, 0.50, rim=(180, 90, 30), rim_from="right")],
    "n18": [Place("panther_hero", 0.30, 0.80, 0.17, haze=0.30, rim=(200, 110, 35))],
    "n19": [Place("panther_hero", 0.68, 0.97, 0.78, flip=True, haze=0.08, glow=0.18,
                  rim=(30, 120, 150), rim_from="left")],
    "n20": [],                              # the dying leaves in close-up
    "n21": [Place("panther_hero", 0.30, 0.58, 0.19, haze=0.26, shadow=0.5,
                  rim=(40, 130, 160))],
    "n22": [Place("samurai_hero", 0.68, 0.95, 0.52, haze=0.05, rim=(190, 130, 60)),
            Place("panther_back", 0.18, 1.06, 0.86, shadow=0.0, haze=0.0)],
    "n23": [Place("samurai_hero", 0.60, 0.64, 0.38, shadow=0.0, haze=0.15,
                  rim=(60, 120, 150))],
    "n24": [Place("panther_hero", 0.50, 0.95, 0.30, rim=(180, 95, 30)),
            Place("ranger_front", 0.30, 0.93, 0.23, haze=0.22, rim=(40, 130, 160)),
            Place("scout_front", 0.39, 0.94, 0.22, haze=0.24, rim=(40, 130, 160)),
            Place("knight_front", 0.62, 0.94, 0.23, haze=0.24, rim=(40, 130, 160)),
            Place("hawk_front", 0.71, 0.93, 0.21, haze=0.28, rim=(40, 130, 160))],
    "n25": [Place("panther_hero", 0.56, 0.99, 0.78, rim=(190, 100, 30),
                  rim_from="right")],
    "n26": [],                              # one word on a wet display
    "n27": [],                              # the tray and the lantern
    "n28": [Place("titan_hero", 0.72, 0.93, 0.70, haze=0.06, rim=(120, 140, 170),
                  rim_from="right"),
            Place("panther_hero", 0.24, 0.98, 0.48, haze=0.04, rim=(120, 140, 170))],
    "n29": [Place("panther_hero", 0.50, 0.99, 0.82, rim=(40, 140, 170)),
            Place("scout_front", 0.15, 0.96, 0.52, haze=0.20, rim=(40, 130, 160)),
            Place("ranger_front", 0.86, 0.96, 0.52, haze=0.20, rim=(40, 130, 160))],

    "e_catch": [Place("panther_hero", 0.82, 1.02, 0.92, flip=True, shadow=0.0,
                      rim=(40, 130, 160)),
                Place("bolt_front", 0.40, 0.74, 0.30, shadow=0.0, haze=0.05,
                      rim=(40, 130, 160), rim_from="right")],
    "e_witness": [Place("titan_hero", 0.56, 0.96, 0.80, rim=(150, 150, 170)),
                  Place("bolt_front", 0.41, 0.63, 0.22, shadow=0.0,
                        rim=(150, 150, 170))],
    "e_watch": [],                          # a fresh name tag on the shelf
    "e_shelf": [Place("panther_hero", 0.63, 0.80, 0.40, haze=0.18, shadow=0.35,
                      rim=(150, 170, 190), rim_from="right")],
    "e_dusting": [Place("panther_hero", 0.62, 0.95, 0.58, haze=0.05,
                        rim=(170, 160, 140), rim_from="right"),
                  Place("ranger_front", 0.34, 0.95, 0.42, haze=0.10,
                        rim=(170, 160, 140), rim_from="right")],
    "e_dead_proof": [],                     # the palm and the grey flakes
    "e_captain": [Place("panther_hero", 0.50, 0.96, 0.52, haze=0.05,
                        rim=(200, 110, 35))],
}


def load_story():
    with open(os.path.join(ADVENTURES, STORY + ".json"), "r", encoding="utf-8") as f:
        return json.load(f)


def key_shot(node):
    for shot in node.get("shots", []):
        if shot.get("key"):
            return shot
    return node.get("shots", [{}])[0]


def scene_light(plate, box):
    """The colour and brightness of the plate around where the subject will
    stand — what the cutout has to be graded into so it belongs there."""
    x0, y0, x1, y1 = box
    pad = 40
    region = plate.crop((max(0, x0 - pad), max(0, y0 - pad),
                         min(plate.width, x1 + pad), min(plate.height, y1 + pad)))
    small = np.asarray(region.resize((16, 16)), dtype=np.float32)
    return small.reshape(-1, 3).mean(axis=0)


TINT_STRENGTH = 0.28     # how far a robot's own colour bends toward the scene


def grade(cut, light, haze, glow, rim=None, rim_from="left"):
    """Push a flat-lit render into a lit scene without losing the character.

    The trap here, hit on the first attempt: multiplying an orange robot by a
    teal night light turns him GOLD. His colour IS his identity, so the scene
    tint is a partial blend, never a full multiply — and his glowing panels
    are emissive, so they are exempt from the dimming entirely.
    """
    rgb = np.asarray(cut.convert("RGB"), dtype=np.float32)
    alpha = np.asarray(cut.getchannel("A"), dtype=np.float32) / 255.0
    original = rgb.copy()

    # Emissive: the bright saturated panels and eye slits are light sources,
    # not surfaces. They keep their own brightness in any weather.
    peak = rgb.max(axis=2)
    chroma = peak - rgb.min(axis=2)
    emissive = np.clip((peak - 150.0) / 60.0, 0, 1) * np.clip((chroma - 40.0) / 60.0, 0, 1)

    # Match exposure to the plate: reference renders are lit far brighter than
    # a rainy night, and that mismatch alone reads as "pasted".
    target = float(np.clip(light.mean(), 24.0, 200.0))
    current = max(1.0, float((rgb * alpha[..., None]).sum() / max(alpha.sum() * 3, 1)))
    rgb *= np.clip((target * 1.5) / current, 0.25, 1.15)

    # Partial tint toward the scene's colour, then lose contrast with distance.
    tint = light / max(light.mean(), 1.0)
    rgb = rgb * (1.0 - TINT_STRENGTH) + rgb * tint[None, None, :] * TINT_STRENGTH
    rgb = rgb * (1.0 - haze) + light[None, None, :] * haze

    # Lift the shadow side with the scene's own ambient rather than to black.
    shade = 1.0 - np.clip(rgb.mean(axis=2) / 140.0, 0, 1)
    rgb += light[None, None, :] * 0.22 * shade[..., None]

    if glow > 0:
        ramp = np.linspace(1.0 + glow, 1.0 - glow * 0.3, rgb.shape[0])[:, None, None]
        rgb *= ramp

    # Emissive panels come back to full strength, plus a little bloom later.
    rgb = rgb * (1.0 - emissive[..., None]) + original * emissive[..., None]

    if rim is not None:
        # A hard edge light down one side: the single cheapest thing that makes
        # a flat cutout sit inside a scene instead of on top of it.
        mask = Image.fromarray((alpha * 255).astype(np.uint8), "L")
        inner = mask.filter(ImageFilter.MinFilter(5))
        edge = (np.asarray(mask, dtype=np.float32)
                - np.asarray(inner, dtype=np.float32)) / 255.0
        edge = np.asarray(Image.fromarray((edge * 255).astype(np.uint8), "L")
                          .filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0
        width = rgb.shape[1]
        side = np.linspace(1.0, 0.0, width) if rim_from == "left" else np.linspace(0.0, 1.0, width)
        rgb += np.asarray(rim, dtype=np.float32)[None, None, :] * (edge * side[None, :])[..., None]

    out = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8),
                     (alpha * 255).astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def contact_shadow(size, width, strength=0.62):
    """An ellipse under the feet. Without it every cutout hovers."""
    w, h = size
    pad = int(w * 0.5)
    shadow = Image.new("L", (w + pad * 2, int(h) + pad * 2), 0)
    draw = ImageDraw.Draw(shadow)
    cx, cy = shadow.width / 2, shadow.height / 2
    draw.ellipse([cx - width / 2, cy - h / 2, cx + width / 2, cy + h / 2],
                 fill=int(255 * strength))
    return shadow.filter(ImageFilter.GaussianBlur(max(4, w * 0.07)))


def compose(node_id, places, force=False):
    plate_path = os.path.join(PLATES, node_id + ".png")
    if not os.path.exists(plate_path):
        return "no plate"
    target = os.path.join(ADVENTURES, STORY, node_id + ".png")
    if os.path.exists(target) and not force:
        return "skip"

    plate = Image.open(plate_path).convert("RGB").resize(SIZE, Image.LANCZOS)
    frame = plate.copy()

    for place in places:
        path = os.path.join(CUTOUTS, place.cutout + ".png")
        if not os.path.exists(path):
            return "missing cutout " + place.cutout
        cut = Image.open(path).convert("RGBA")
        if place.flip:
            cut = cut.transpose(Image.FLIP_LEFT_RIGHT)

        height = max(8, int(SIZE[1] * place.h))
        width = max(8, int(cut.width * height / cut.height))
        cut = cut.resize((width, height), Image.LANCZOS)

        left = int(SIZE[0] * place.x) - width // 2
        top = int(SIZE[1] * place.y) - height
        light = scene_light(plate, (left, top, left + width, top + height))

        if place.shadow > 0:
            shadow = contact_shadow((width, max(10, height * 0.13)),
                                    width * place.shadow)
            dark = Image.new("RGB", shadow.size, (0, 0, 0))
            frame.paste(dark, (left + width // 2 - shadow.width // 2,
                               int(SIZE[1] * place.y) - shadow.height // 2), shadow)

        lit = grade(cut, light, place.haze, place.glow, place.rim, place.rim_from)
        frame.paste(lit, (left, top), lit)

    # One unifying pass so the paste and the paint share a grain and a haze.
    veil = Image.new("RGB", SIZE, tuple(int(v) for v in
                                        np.asarray(plate.resize((8, 8)),
                                                   dtype=np.float32).reshape(-1, 3).mean(axis=0)))
    frame = Image.blend(frame, veil, 0.06)
    frame = ImageChops.multiply(frame, vignette())

    os.makedirs(os.path.dirname(target), exist_ok=True)
    frame.save(target)
    return "ok"


_VIGNETTE = None


def vignette():
    global _VIGNETTE
    if _VIGNETTE is None:
        y, x = np.mgrid[0:SIZE[1], 0:SIZE[0]]
        nx = (x / SIZE[0] - 0.5) * 2
        ny = (y / SIZE[1] - 0.5) * 2
        radius = np.sqrt(nx ** 2 + ny ** 2) / 1.42
        mask = np.clip(1.0 - 0.42 * radius ** 2.2, 0, 1)
        _VIGNETTE = Image.fromarray(
            (np.dstack([mask] * 3) * 255).astype(np.uint8), "RGB")
    return _VIGNETTE


def main(argv):
    force = "--force" in argv
    story = load_story()
    wanted = [a for a in argv if not a.startswith("--")]
    for node in story["nodes"]:
        nid = node["id"]
        if wanted and nid not in wanted:
            continue
        print(f"  {nid:14s} {compose(nid, PLACEMENT.get(nid, []), force)}")


if __name__ == "__main__":
    main(sys.argv[1:])

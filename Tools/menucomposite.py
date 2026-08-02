"""Lay the game's own rendered robots onto the painted background plates.

    python Tools/menuart.py        # the plates          -> Tools/menu_plates
    (Unity)  MenuArtForge.Batch    # the robots          -> Tools/menu_render
    python Tools/menucomposite.py  # the two, married    -> Assets/Resources/Menu

The cards show real Titan, Hawk, Panther and the rest in real Brawl poses,
because a painted robot is not this game's robot -- it is somebody else's.
The plates only supply the room they stand in.

MenuArtForge renders each rig twice, on black and on white, because URP does
not hand back a usable alpha channel. That pair is all the cutout needs:

    K = P                     (premultiplied colour over black)
    W = P + (1 - a)           (the same over white)
    W - K = 1 - a

so the difference IS the inverse coverage, and K is already premultiplied --
which means the composite is a multiply and an add, with no division and no
un-premultiply step to introduce fringing.
"""

import os
import sys

from PIL import Image, ImageChops, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PLATES = os.path.join(HERE, "menu_plates")
RENDERS = os.path.join(HERE, "menu_render")
OUT = os.path.join(HERE, "..", "Assets", "Resources", "Menu")

CARD = (1280, 720)
HERO = (1920, 1080)

# MenuIcon enum name -> plate name. The enum is what MenuArtForge names its
# files; the plate keys are the lowercase form MainMenu asks Resources for.
CARDS = [
    "AIvAI", "PlayerVsAI", "Brawl", "BrawlWar", "BrawlShow", "TankRaid",
    "OnlinePvP", "Commander", "CommanderWar", "TowerDefense", "ChineseQuest",
    "ChineseRun", "ArenaBuilder",
]

# Where the hero line-up sits on the title plate. The menu's card deck covers
# everything below ~41% of the screen, so the line-up is sized and seated to
# stand in the band BETWEEN the logo and the deck: heads and shoulders clear
# of the tray, legs sinking into it. Filling the frame with them just hid them
# behind the cards.
HERO_TAKE = "HeroLineup"
HERO_FEET = 0.60
HERO_HEIGHT = 0.26

# Cards get the same treatment. The rigs frame their subject wherever the
# diorama's camera happened to put it, which after the floor discs were hidden
# left robots hanging in the middle of the frame; planting the bottom of the
# subject near the bottom of the card is what sits them on the painted ground.
CARD_FEET = 0.93
CARD_HEIGHT = 0.80


def cutout(name, size):
    """(premultiplied colour, inverse coverage) for one render pair, at size."""
    black = Image.open(os.path.join(RENDERS, f"{name}_k.png")).convert("RGB")
    white = Image.open(os.path.join(RENDERS, f"{name}_w.png")).convert("RGB")
    if black.size != white.size:
        raise RuntimeError(f"{name}: render pair disagrees on size")

    inverse = ImageChops.subtract(white, black)
    # Resize BEFORE compositing: both layers are linear in coverage, so the
    # downsample is the supersample that gives these edges their anti-aliasing.
    return (black.resize(size, Image.LANCZOS),
            inverse.resize(size, Image.LANCZOS))


def over(plate, colour, inverse):
    """Premultiplied source over a background: plate * (1 - a) + colour."""
    return ImageChops.add(ImageChops.multiply(plate, inverse), colour)


def grounded(plate, inverse, strength=0.62, squash=0.13, blur=14):
    """Darken the plate with a squashed, blurred copy of the subject.

    The rigs' own floor discs are hidden for these renders, so without this the
    robots hang in the air over the painted ground. Deriving the shadow from
    the coverage mask rather than stamping an ellipse keeps it the shape of
    whatever is actually standing there -- a wide stance, a raised leg mid-kick.
    """
    coverage = ImageChops.invert(inverse.convert("L"))
    box = coverage.point(lambda v: 255 if v > 24 else 0).getbbox()
    if box is None:
        return plate

    width, height = plate.size
    shadow_h = max(2, int((box[3] - box[1]) * squash))
    stamp = coverage.crop(box).resize((box[2] - box[0], shadow_h), Image.LANCZOS)

    mask = Image.new("L", (width, height), 0)
    # Centred on the feet line, so half the blur falls in front of the subject
    # and half behind it.
    mask.paste(stamp, (box[0], box[3] - shadow_h // 2))
    mask = mask.filter(ImageFilter.GaussianBlur(blur))
    mask = mask.point(lambda v: int(255 - v * strength))

    return ImageChops.multiply(plate, Image.merge("RGB", (mask, mask, mask)))


def alpha_bbox(inverse):
    """Bounds of the covered pixels, from the inverse-coverage layer."""
    coverage = ImageChops.invert(inverse.convert("L"))
    # A hard floor: the plates' faint haze would otherwise count as subject.
    return coverage.point(lambda v: 255 if v > 24 else 0).getbbox()


def place(colour, inverse, size, feet, height):
    """Scale the subject to `height` of the frame and plant its base at `feet`."""
    box = alpha_bbox(inverse)
    if box is None:
        raise RuntimeError("rendered empty")

    scale = min((height * size[1]) / (box[3] - box[1]), 1.0)
    scaled = (max(1, int(size[0] * scale)), max(1, int(size[1] * scale)))
    colour = colour.resize(scaled, Image.LANCZOS)
    inverse = inverse.resize(scaled, Image.LANCZOS)
    offset = (int(size[0] / 2 - scaled[0] / 2),
              int(feet * size[1] - box[3] * scale))

    # Pad back to the full frame. White in the inverse layer means "no
    # coverage", so the untouched border must be white, not black.
    canvas = Image.new("RGB", size, (0, 0, 0))
    canvas.paste(colour, offset)
    veil = Image.new("RGB", size, (255, 255, 255))
    veil.paste(inverse, offset)
    return canvas, veil


def build_card(name):
    plate_key = name.lower()
    plate_path = os.path.join(PLATES, f"{plate_key}.png")
    if not os.path.exists(plate_path):
        raise RuntimeError(f"no plate for {plate_key} — run Tools/menuart.py")

    plate = Image.open(plate_path).convert("RGB").resize(CARD, Image.LANCZOS)
    colour, inverse = place(*cutout(name, CARD), CARD, CARD_FEET, CARD_HEIGHT)
    over(grounded(plate, inverse), colour, inverse).save(
        os.path.join(OUT, f"{plate_key}.png"))
    return plate_key


def build_keyart():
    """The title screen: the roster line-up standing on the painted horizon."""
    plate = Image.open(os.path.join(PLATES, "keyart.png")).convert("RGB").resize(
        HERO, Image.LANCZOS)
    colour, inverse = place(*cutout(HERO_TAKE, HERO), HERO, HERO_FEET, HERO_HEIGHT)
    over(grounded(plate, inverse, strength=0.5, blur=22), colour, inverse).save(
        os.path.join(OUT, "keyart.png"))
    return "keyart"


def main(argv):
    if not os.path.isdir(RENDERS):
        sys.exit(f"no renders in {RENDERS} — run MenuArtForge.Batch in Unity first")
    os.makedirs(OUT, exist_ok=True)

    wanted = [a for a in argv if not a.startswith("--")]
    done, failed = [], []
    for name in CARDS:
        if wanted and name.lower() not in [w.lower() for w in wanted]:
            continue
        try:
            done.append(build_card(name))
        except Exception as exc:
            print(f"  FAIL  {name}: {exc}")
            failed.append(name)
    if not wanted or "keyart" in [w.lower() for w in wanted]:
        try:
            done.append(build_keyart())
        except Exception as exc:
            print(f"  FAIL  keyart: {exc}")
            failed.append("keyart")

    for key in done:
        print(f"  ok    {key}")
    if failed:
        sys.exit(f"failed: {failed}")


if __name__ == "__main__":
    main(sys.argv[1:])

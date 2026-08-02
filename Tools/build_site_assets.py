"""Turn the game's own render outputs into the web assets www.jah.cc serves.

    python Tools/build_site_assets.py

Everything the home page shows is already rendered somewhere in this repo — the
painted plates married to real robot renders (Assets/Resources/Menu), the
turntable captures (PreviewCaptures) and the transform clips (Assets/Video).
None of it is web-shaped: 1 MB PNGs, 960-square 5 s clips with an audio track,
and robots sitting on an opaque background instead of cut out.

This script is the seam. Re-render the game art, run this, redeploy — no hand
retouching in between, which is the same contract Tools/menucomposite.py has
with the menu.

Outputs land in Web/assets and are NOT committed as hand-made art: they are
derived, and this file is the recipe.
"""

import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
MENU = os.path.join(ROOT, "Assets", "Resources", "Menu")
PLATES = os.path.join(HERE, "menu_plates")
RENDERS = os.path.join(HERE, "menu_render")
CAPS = os.path.join(ROOT, "PreviewCaptures")
VIDEO = os.path.join(ROOT, "Assets", "Video")
OUT = os.path.join(ROOT, "Web", "assets")

# The flat colour MenuArtForge clears the preview camera to. Known exactly, which
# is what makes the cut-out below an un-mix rather than a guess.
CAP_BG = np.array([5, 13, 25], dtype=np.float32)

# Menu art key -> the file the site wants. Same keys MainMenu asks Resources for.
MODES = [
    "aivai", "playervsai", "onlinepvp", "brawl", "brawlwar", "brawlshow",
    "commander", "commanderwar", "towerdefense", "tankraid",
    "chinesequest", "chineserun", "arenabuilder",
]

HEROES = ["RANGER", "TITAN", "SCOUT", "HAWK", "BOLT",
          "SAMURAI", "PANTHER", "KNIGHT", "RACER"]


def webp(img, path, width=None, quality=80):
    """Save `img` as webp, optionally resized to `width`, and report the size."""
    if width and img.width != width:
        h = round(img.height * width / img.width)
        img = img.resize((width, h), Image.LANCZOS)
    img.save(path, "WEBP", quality=quality, method=6)
    print(f"  {os.path.basename(path):32s} {img.width}x{img.height}"
          f"  {os.path.getsize(path) / 1024:6.0f} KB")


def cutout(path):
    """Lift a robot off the known preview background, with true soft edges.

    A single render over one background cannot be un-mixed in general — colour
    and coverage are two unknowns in one equation. What saves it here is that
    the background is *flat and known*: the pixels that still equal it are
    background, and the only ambiguity left is the one-pixel antialiased rim.

    So coverage comes from connectivity, not from brightness: flood the region
    of near-background pixels reachable from the border. A dark visor enclosed
    by bright armour is never reached, and therefore never punched through —
    which is exactly what a brightness threshold gets wrong.
    """
    src = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    dist = np.abs(src - CAP_BG).max(axis=2)

    # Soft coverage for the rim: 0 at the background colour, 1 by `span` away.
    span = 26.0
    soft = np.clip(dist / span, 0.0, 1.0)

    # Connectivity: which near-background pixels touch the border. Iterative
    # dilation of a border seed through the near-bg region — a few dozen passes
    # over a 560-square is cheaper than importing a labelling library.
    near_bg = dist < 14.0
    reach = np.zeros_like(near_bg)
    reach[0, :] = near_bg[0, :]
    reach[-1, :] = near_bg[-1, :]
    reach[:, 0] = near_bg[:, 0]
    reach[:, -1] = near_bg[:, -1]
    while True:
        grown = reach.copy()
        grown[1:, :] |= reach[:-1, :]
        grown[:-1, :] |= reach[1:, :]
        grown[:, 1:] |= reach[:, :-1]
        grown[:, :-1] |= reach[:, 1:]
        grown &= near_bg
        if grown.sum() == reach.sum():
            break
        reach = grown

    alpha = np.where(reach, soft, 1.0)

    # Un-premultiply against the background we know was underneath.
    a = alpha[..., None]
    colour = np.where(a > 0.004, (src - (1.0 - a) * CAP_BG) / np.maximum(a, 1e-4), 0.0)
    rgba = np.concatenate([np.clip(colour, 0, 255), alpha[..., None] * 255], axis=2)
    img = Image.fromarray(rgba.astype(np.uint8), "RGBA")

    box = img.getbbox()
    return img.crop(box) if box else img


def lineup():
    """The five-robot line-up, on its own layer with real alpha.

    MenuArtForge renders every rig twice — over black and over white — because
    URP hands back no usable alpha. That pair solves exactly:

        K = P                (premultiplied colour over black)
        W = P + (1 - a)      (the same over white)
        W - K = 1 - a

    so the difference IS the inverse coverage and K is already premultiplied.
    Tools/menucomposite.py uses this to seat robots on the menu plates; the web
    hero needs the same layer, kept separate so the title can sit above it
    rather than across its chest.
    """
    k = np.asarray(Image.open(os.path.join(RENDERS, "HeroLineup_k.png"))
                   .convert("RGB"), dtype=np.float32)
    w = np.asarray(Image.open(os.path.join(RENDERS, "HeroLineup_w.png"))
                   .convert("RGB"), dtype=np.float32)

    alpha = np.clip(1.0 - (w - k).mean(axis=2) / 255.0, 0.0, 1.0)
    a = alpha[..., None]
    colour = np.where(a > 0.004, k / np.maximum(a, 1e-4), 0.0)
    rgba = np.concatenate([np.clip(colour, 0, 255), a * 255], axis=2)
    img = Image.fromarray(rgba.astype(np.uint8), "RGBA")
    box = img.getbbox()
    return img.crop(box) if box else img


def backdrop(src):
    """The flat colour a clip was rendered against, read off its corners."""
    tmp = os.path.join(OUT, "_probe.png")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", "0.6", "-i", src,
                    "-frames:v", "1", tmp], check=True)
    im = Image.open(tmp).convert("RGB")
    w, h = im.size
    corners = [im.getpixel(p) for p in
               ((4, 4), (w - 5, 4), (4, h - 5), (w - 5, h - 5))]
    os.remove(tmp)
    return tuple(int(np.median([c[i] for c in corners])) for i in range(3))


def clip(src, dst, size=560, crf=30):
    """Re-encode a transform clip: no audio, half size, web-seekable.

    Eight of the nine clips were captured against the game's own near-black; one
    (titan) came back on a mid grey, which reads as a grey box dropped into the
    page. Rather than hand-fix that one file, key whatever flat backdrop the
    clip actually has onto the site's panel colour when it is too light to pass.
    The tolerance is deliberately tight — Titan's hands and visor are grey too,
    and a loose key eats them.
    """
    bg = backdrop(src)
    chain = f"scale={size}:{size}:flags=lanczos"
    args = ["ffmpeg", "-y", "-loglevel", "error", "-i", src]
    if sum(bg) / 3 > 40:
        args += ["-f", "lavfi", "-i", f"color=c=0x071B26:s={size}x{size}"]
        args += ["-filter_complex",
                 f"[0:v]{chain},colorkey=0x{bg[0]:02X}{bg[1]:02X}{bg[2]:02X}:0.10:0.03[k];"
                 f"[1:v][k]overlay=shortest=1"]
    else:
        args += ["-vf", chain]
    args += ["-an", "-c:v", "libx264", "-crf", str(crf), "-preset", "slow",
             "-pix_fmt", "yuv420p", "-movflags", "+faststart", dst]
    subprocess.run(args, check=True)
    print(f"  {os.path.basename(dst):32s} {size}x{size}"
          f"  {os.path.getsize(dst) / 1024:6.0f} KB")


def poster(src, dst, at="0.15", width=560):
    subprocess.run([
        "ffmpeg", "-y", "-loglevel", "error", "-ss", at, "-i", src,
        "-frames:v", "1", "-vf", f"scale={width}:-1:flags=lanczos",
        "-q:v", "6", dst,
    ], check=True)


def main():
    if not shutil.which("ffmpeg"):
        sys.exit("ffmpeg is not on PATH — the transform clips need it.")
    os.makedirs(OUT, exist_ok=True)

    print("key art")
    key = Image.open(os.path.join(MENU, "keyart.png")).convert("RGB")
    webp(key, os.path.join(OUT, "keyart-1920.webp"), 1920, quality=76)
    webp(key, os.path.join(OUT, "keyart-1280.webp"), 1280, quality=78)
    webp(key, os.path.join(OUT, "keyart-800.webp"), 800, quality=78)
    # The share card: 1200x630 is the Open Graph frame, and the robots have to
    # survive the crop, so take the middle band rather than a centred square.
    w, h = key.size
    band = round(w * 630 / 1200)
    top = round(h * 0.20)
    key.crop((0, top, w, min(h, top + band))).resize((1200, 630), Image.LANCZOS) \
        .save(os.path.join(OUT, "og.jpg"), "JPEG", quality=86, optimize=True)
    print(f"  {'og.jpg':32s} 1200x630"
          f"  {os.path.getsize(os.path.join(OUT, 'og.jpg')) / 1024:6.0f} KB")

    # The hero is layered rather than flat: the painted plate WITHOUT robots on
    # the back, the line-up cut out on top. Flat key art puts the title across
    # the cast's chest and there is no way to move either one.
    print("hero layers")
    plate = Image.open(os.path.join(PLATES, "keyart.png")).convert("RGB")
    webp(plate, os.path.join(OUT, "plate-1920.webp"), 1920, quality=74)
    webp(plate, os.path.join(OUT, "plate-1280.webp"), 1280, quality=76)
    webp(plate, os.path.join(OUT, "plate-800.webp"), 800, quality=76)
    cast = lineup()
    webp(cast, os.path.join(OUT, "lineup-1600.webp"), 1600, quality=84)
    webp(cast, os.path.join(OUT, "lineup-900.webp"), 900, quality=84)

    print("emblem")
    em = Image.open(os.path.join(MENU, "emblem.png")).convert("RGBA")
    webp(em, os.path.join(OUT, "emblem-256.webp"), 256, quality=88)
    em.resize((180, 180), Image.LANCZOS).save(os.path.join(OUT, "apple-touch-icon.png"))
    em.resize((64, 64), Image.LANCZOS).save(os.path.join(OUT, "favicon.png"))

    print("mode cards")
    for key_name in MODES:
        src = os.path.join(MENU, f"{key_name}.png")
        if not os.path.exists(src):
            print(f"  !! missing {src}")
            continue
        webp(Image.open(src).convert("RGB"),
             os.path.join(OUT, f"mode-{key_name}.webp"), 720, quality=74)

    print("hero cut-outs")
    for name in HEROES:
        src = os.path.join(CAPS, f"{name}_front.png")
        if not os.path.exists(src):
            print(f"  !! missing {src}")
            continue
        webp(cutout(src), os.path.join(OUT, f"hero-{name.lower()}.webp"),
             420, quality=86)

    print("transform clips")
    for name in HEROES:
        src = os.path.join(VIDEO, f"{name.lower()}-transform.mp4")
        if not os.path.exists(src):
            print(f"  !! missing {src}")
            continue
        dst = os.path.join(OUT, f"transform-{name.lower()}.mp4")
        clip(src, dst)
        # Poster comes off the ENCODED clip, so a keyed backdrop is in it too.
        poster(dst, os.path.join(OUT, f"transform-{name.lower()}.jpg"))

    total = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT))
    print(f"\nWeb/assets: {total / 1024 / 1024:.1f} MB")


if __name__ == "__main__":
    main()

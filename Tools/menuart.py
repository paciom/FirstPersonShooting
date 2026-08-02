"""Generate the main-menu key art and the mode cards with MiniMax image-01.

The home screen used to render thirteen live 3D dioramas -- thirteen cameras
and render textures every frame, which cost more than the game behind them.
These painted cards replace them: static art, loaded once from Resources.

The prompts all share STYLE so the cards read as one set. Every prompt ends
with NO_TEXT because the model will happily invent garbled lettering on a
game card if you let it.

    python Tools/menuart.py            # everything that is missing
    python Tools/menuart.py keyart     # just these keys
    python Tools/menuart.py --force    # redo even if the file exists
"""

import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Menu")
ENDPOINT = "https://api.minimax.io/v1/image_generation"

# The robots are toon-shaded, high-saturation, hard-surface transformers on a
# near-black navy field (see PreviewCaptures/*_front.png). Every prompt is
# anchored to that so the thirteen cards look like one art pass.
STYLE = (
    "stylized 3D rendered video game art, clean toon-shaded hard-surface "
    "transforming mecha with glossy plate armor, saturated cyan blue teal and "
    "golden orange color scheme, dramatic cinematic rim lighting, glowing "
    "energy accents, deep navy background, volumetric haze, high contrast, "
    "polished AAA mobile game key art quality, vibrant and heroic"
)
NO_TEXT = "no text, no letters, no words, no numbers, no logo, no watermark, no UI"

# key -> (aspect, subject). Card keys match MenuIcon enum names, lowercased.
ART = {
    # The menu draws its title across the top of this one, so the composition
    # matters as much as the subject: the model only leaves the upper third
    # genuinely empty if you tell it so first, before describing anything.
    "keyart": ("16:9", (
        "IMPORTANT COMPOSITION: the top 40 percent of the image is completely "
        "empty glowing sky with nothing in it, no robots and no structures "
        "intrude into the upper 40 percent. A wide heroic team of armored "
        "transforming robots stands shoulder to shoulder along the bottom half "
        "of the picture seen from a low angle, their heads reaching only up to "
        "the middle line of the image, above them nothing but a vast empty "
        "glowing teal sky with soft clouds and drifting light motes, dark "
        "smoke at the very bottom edge, symmetrical hero shot"
    )),
    # Unused by the menu today, but this is the alternate hero framing worth
    # keeping around: one giant robot dead centre under an arena dome halo.
    "keyart_hero": ("16:9", (
        "IMPORTANT COMPOSITION: the top 40 percent of the image is completely "
        "empty glowing sky with nothing in it. In the lower 60 percent one "
        "giant heroic teal and orange armored robot stands facing the camera "
        "at a low hero angle with two smaller robots flanking it, thick dark "
        "mist swallows the very bottom edge, a huge soft glowing circular "
        "arena dome halo fills the empty sky above them, symmetrical"
    )),
    "emblem": ("1:1", (
        "ornate heraldic esports crest emblem, a robot knight visor helmet "
        "centered on an angular shield, spread mechanical wings, a pair of "
        "crossed energy blades behind, brushed gold and glowing cyan, "
        "perfectly symmetrical, centered, isolated on flat black background, "
        "sharp vector-clean metal insignia"
    )),

    "aivai": ("16:9", (
        "two armored transforming robots duelling across a neon laser arena, "
        "both firing bright cyan and orange energy bolts, streaking tracer "
        "beams crossing the middle of the frame, sparks on their armor"
    )),
    "playervsai": ("16:9", (
        "first person shooter view: a glowing futuristic energy blaster held "
        "in the lower right foreground, aimed down a neon arena at an armored "
        "robot charging the camera, muzzle flash, cyan targeting reticle glow"
    )),
    "brawl": ("16:9", (
        "two armored mecha in a brutal close quarters fistfight, one landing a "
        "massive punch on the other's chest plate, shockwave ring at the "
        "impact, debris and sparks flying, hand to hand fighting game splash"
    )),
    "brawlwar": ("16:9", (
        "two armored mecha clashing in midair, one throwing a flying kick and "
        "the other blocking with crossed forearms, energy shockwave between "
        "them, floodlit fighting ring below, arena crowd lights in the haze"
    )),
    "brawlshow": ("16:9", (
        "a single elegant armored mecha frozen mid martial arts kata, one leg "
        "extended in a high crane kick, ribbons of light trailing its limbs, "
        "single dramatic spotlight from above, dark dojo arena, graceful and "
        "ceremonial"
    )),
    "tankraid": ("16:9", (
        "steep top down aerial view of one heroic hero tank speeding up a "
        "neon highway strip, surrounded by a swarm of small enemy tanks and "
        "turrets firing, glowing projectile streaks everywhere, arcade "
        "vertical shooter battlefield"
    )),
    "onlinepvp": ("16:9", (
        "two armored robots facing each other in profile on opposite sides of "
        "a glowing holographic wireframe planet, network lines arcing between "
        "continents, versus standoff composition, split cyan and orange "
        "lighting"
    )),
    "commander": ("16:9", (
        "real time strategy overhead battlefield view: a fortified futuristic "
        "command base with glowing hexagonal buildings and turrets, ranks of "
        "small robot units and tanks marching out in formation, holographic "
        "grid overlay on the terrain"
    )),
    "commanderwar": ("16:9", (
        "sweeping overhead view of two huge robot armies colliding across a "
        "canyon battlefield, hundreds of tiny units and tanks, crossfire of "
        "cyan and orange tracer fire, explosions and smoke columns, epic scale "
        "war"
    )),
    "towerdefense": ("16:9", (
        "a winding canyon path lined with glowing futuristic defense turrets "
        "and missile towers all firing down at a long column of advancing "
        "enemy robots, tracer arcs, tower defense diorama, three quarter view"
    )),
    "chinesequest": ("16:9", (
        "a friendly armored robot student sitting cross legged before a "
        "floating glowing scroll and red paper lanterns, jade and gold "
        "ornaments, ink brush and inkstone, warm red and gold Chinese "
        "temple courtyard at night, magical study scene"
    )),
    "chineserun": ("16:9", (
        "an armored robot sprinting straight toward the camera down an endless "
        "neon corridor, glowing gates and floating jade rings rushing past, "
        "motion blur speed lines, red and gold lantern light streaking"
    )),
    "arenabuilder": ("16:9", (
        "a futuristic arena under construction assembling itself in midair "
        "from glowing holographic blueprint blocks, translucent cyan wireframe "
        "pieces snapping into solid platforms and ramps, drafting table "
        "hologram, creative sandbox feel"
    )),
}


# The prompt optimizer rewrites the prompt before rendering, and it treats a
# composition instruction as flavour text -- it puts the robots right back in
# the sky. Any prompt whose LAYOUT matters has to go through verbatim.
NO_OPTIMIZE = {"keyart", "keyart_hero"}

# Keys whose flat backdrop should become alpha, so the art can sit over the
# key art instead of inside a black box.
CUTOUT = {"emblem"}


def cut_out_background(path):
    """Turn the near-black surround into alpha, keeping the subject solid.

    A plain luminance ramp would also eat the subject's own dark crevices and
    let the background show through them. So the silhouette is found first --
    flood fill the dark region inward from a corner, and whatever the fill
    cannot reach is subject, holes included, and stays fully opaque. Only the
    genuinely outside pixels get the ramp, which is what fades the glow halo
    out instead of clipping it into a hard rim.
    """
    img = Image.open(path).convert("RGB")
    lum = img.convert("L")

    # The threshold has to be loose enough that the subject's own glow halo
    # counts as background -- at a tight threshold the halo walls the fill off
    # and comes out as an opaque black blob around the art. The halo is dim
    # enough that the ramp below then fades it to a soft glow.
    dark = lum.point(lambda v: 255 if v <= 45 else 0, mode="L")
    ImageDraw.floodfill(dark, (0, 0), 128)
    outside = dark.point(lambda v: 255 if v == 128 else 0, mode="L")

    lo, hi = 6, 48
    halo = lum.point(lambda v: 0 if v <= lo else min(255, int(255 * (v - lo) / (hi - lo))))
    alpha = Image.composite(halo, Image.new("L", img.size, 255), outside)
    # Half a pixel of softening so the silhouette edge is not a stair-step.
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.8))

    img.putalpha(alpha)
    img.save(path)


def generate(key, aspect, subject, force=False):
    path = os.path.abspath(os.path.join(OUT, key + ".png"))
    if os.path.exists(path) and not force:
        print(f"  skip  {key} (exists)")
        return path

    body = json.dumps({
        "model": "image-01",
        "prompt": f"{subject}. {STYLE}. {NO_TEXT}",
        "aspect_ratio": aspect,
        "response_format": "url",
        "n": 1,
        "prompt_optimizer": key not in NO_OPTIMIZE,
    }).encode()
    req = urllib.request.Request(ENDPOINT, data=body, headers={
        "Authorization": "Bearer " + os.environ["MINIMAX_API_KEY"],
        "Content-Type": "application/json",
    })
    payload = json.loads(urllib.request.urlopen(req, timeout=300).read())
    if payload.get("base_resp", {}).get("status_code") != 0:
        raise RuntimeError(f"{key}: {payload.get('base_resp')}")

    url = payload["data"]["image_urls"][0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(url, timeout=300) as src, open(path, "wb") as dst:
        dst.write(src.read())
    if key in CUTOUT:
        cut_out_background(path)
    print(f"  ok    {key} -> {path}")
    return path


def main(argv):
    force = "--force" in argv
    keys = [a for a in argv if not a.startswith("--")] or list(ART)
    unknown = [k for k in keys if k not in ART]
    if unknown:
        sys.exit(f"unknown keys: {unknown}\nknown: {sorted(ART)}")

    # The API is per-request slow but happily parallel; four at a time keeps
    # the whole set under a minute without tripping the rate limit.
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures = {k: pool.submit(generate, k, *ART[k], force=force) for k in keys}
    failed = []
    for k, f in futures.items():
        try:
            f.result()
        except Exception as exc:  # keep going; report the whole batch at once
            print(f"  FAIL  {k}: {exc}")
            failed.append(k)
    if failed:
        sys.exit(f"failed: {failed}")


if __name__ == "__main__":
    main(sys.argv[1:])

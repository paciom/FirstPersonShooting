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
PLATE_OUT = os.path.join(os.path.dirname(__file__), "menu_plates")
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
# Applied to the card plates only: they are backdrops for a 3D render laid on
# top, so anything robot-shaped in them would double up with the real robot.
PLATE = ("empty environment background plate, no robots, no mecha, no people, "
         "no characters, no creatures, defocused shallow depth of field, soft "
         "blurred background bokeh, wide establishing shot")

# key -> (aspect, subject). Card keys match MenuIcon enum names, lowercased.
ART = {
    # The menu draws its title across the top of this one, so the composition
    # matters as much as the subject: the model only leaves the upper third
    # genuinely empty if you tell it so first, before describing anything.
    "keyart": ("16:9", (
        "IMPORTANT COMPOSITION: the top 45 percent of the image is completely "
        "empty glowing sky with nothing in it. A vast empty glowing teal and "
        "cyan sky over a distant futuristic arena city on the horizon, the "
        "city tiny and far away along the bottom third, sweeping searchlight "
        "beams, soft clouds, drifting light motes, dark smoke and haze along "
        "the very bottom edge, symmetrical, wide cinematic establishing shot, "
        "completely empty of robots mecha people characters or foreground "
        "objects"
    )),
    "emblem": ("1:1", (
        "ornate heraldic esports crest emblem, a robot knight visor helmet "
        "centered on an angular shield, spread mechanical wings, a pair of "
        "crossed energy blades behind, brushed gold and glowing cyan, "
        "perfectly symmetrical, centered, isolated on flat black background, "
        "sharp vector-clean metal insignia"
    )),

    # --- background plates -------------------------------------------
    # DELIBERATELY EMPTY OF CHARACTERS. The robots on these cards are the
    # game's own, rendered by MenuArtForge and composited on top by
    # Tools/menucomposite.py -- a painted robot underneath would collide with
    # the real one. They are also prompted defocused: a soft, shallow-focus
    # plate sits under a sharp 3D render without fighting it for perspective,
    # which a crisp environment with its own strong vanishing point does.
    "aivai": ("16:9", (
        "empty futuristic laser arena interior at night, glowing cyan floor "
        "grid stretching away, dark tiered stands, drifting smoke, streaks of "
        "stray energy fire in the distance"
    )),
    "playervsai": ("16:9", (
        "empty neon arena corridor seen head on, glowing target rings and "
        "warning stripes on the walls, harsh cyan floodlight down the middle, "
        "haze and lens flare"
    )),
    "brawl": ("16:9", (
        "empty floodlit fighting ring at night, dark crowd stands with "
        "thousands of tiny lights, hot spotlights raking down through smoke, "
        "sparks drifting"
    )),
    "brawlwar": ("16:9", (
        "empty combat arena under a huge glowing dome, banks of stadium "
        "floodlights, heavy atmospheric haze, orange and cyan light beams "
        "crossing"
    )),
    "brawlshow": ("16:9", (
        "empty ceremonial dojo stage at night, one dramatic overhead "
        "spotlight pooling on a dark polished floor, red and gold banners far "
        "back in shadow, drifting motes"
    )),
    "tankraid": ("16:9", (
        "empty neon highway strip seen from high above, glowing lane markings "
        "and hazard chevrons, scorch marks and craters, dark ground either "
        "side, tracer streaks in the distance"
    )),
    "onlinepvp": ("16:9", (
        "a glowing holographic wireframe planet floating in dark space, "
        "network lines arcing between continents, cyan and orange data "
        "streams, empty foreground"
    )),
    "commander": ("16:9", (
        "empty futuristic battlefield terrain seen from high above, "
        "holographic command grid projected over rock and metal plating, "
        "glowing resource nodes, smoke drifting, no vehicles"
    )),
    "commanderwar": ("16:9", (
        "vast empty war-torn plain seen from high above, trench lines and "
        "craters, columns of smoke, distant fires, crossing tracer light in "
        "the far distance, no vehicles"
    )),
    "towerdefense": ("16:9", (
        "empty winding canyon path seen three quarter from above, glowing "
        "waypoint markers along the route, sheer rock walls, mist in the "
        "gully, no towers and no units"
    )),
    "chinesequest": ("16:9", (
        "empty Chinese temple courtyard at night, rows of glowing red paper "
        "lanterns, red lacquered columns, jade and gold ornament, wet stone "
        "floor, warm mist"
    )),
    "chineserun": ("16:9", (
        "empty endless neon corridor rushing toward the viewer, glowing gates "
        "and floating jade rings, red and gold lantern light streaking with "
        "motion blur"
    )),
    "arenabuilder": ("16:9", (
        "empty holographic drafting void, a faint cyan wireframe construction "
        "grid receding into darkness, translucent blueprint planes and "
        "floating measurement guides, no buildings"
    )),
}


# The prompt optimizer rewrites the prompt before rendering, and it treats a
# composition instruction as flavour text -- it puts the robots right back in
# the sky. Any prompt whose LAYOUT matters has to go through verbatim.
NO_OPTIMIZE = {"keyart"}

# Keys whose flat backdrop should become alpha, so the art can sit over the
# key art instead of inside a black box.
CUTOUT = {"emblem"}

# Everything except the crest is a backdrop for rendered robots. These stage
# in Tools/menu_plates and only reach Resources once composited.
PLATES = {k for k in ART if k != "emblem"}


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
    plate = key in PLATES
    path = os.path.abspath(os.path.join(PLATE_OUT if plate else OUT, key + ".png"))
    if os.path.exists(path) and not force:
        print(f"  skip  {key} (exists)")
        return path

    body = json.dumps({
        "model": "image-01",
        "prompt": f"{subject}. {STYLE}. {PLATE + '. ' if plate else ''}{NO_TEXT}",
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

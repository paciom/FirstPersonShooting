"""Previews a team repaint without opening Unity.

Reimplements PA_TeamRecolor.shader over the textures embedded in a .glb and
writes out repainted copies, so Tools/previewglb.py can render what the away
team will actually look like. Exists because the repaint is a judgement call
about colour that cannot be checked from the source: the bug it was written for
— a two-tone robot collapsing to one colour — is invisible in the code and
obvious in a picture.

KEEP IN STEP with PA_TeamRecolor.shader and TeamPaint.cs. All three do the same
arithmetic; this one is the only one that can be run from a shell.

  python teampaintpreview.py <out-dir> <robot> [spread ...]

Emits <out-dir>/<robot>_spread<N>.glb per spread value, plus a spread of 0
(the old collapse-to-one-hue behaviour) for comparison.
"""
import colorsys
import io
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shrink_glb_textures import read_glb, write_glb  # noqa: E402

ROOT = "D:/Claude/FirstPersongShooting"

# Matched to TeamPaint.cs.
STRENGTH = 1.0
GREY_CUTOFF = 0.15
GREY_SOFTNESS = 0.25
SATURATION_FLOOR = 0.5
NEUTRAL_WASH = 0.22

AWAY_TEAM = (1.0, 0.25, 0.9)            # magenta

# Each robot's dominant saturated hue, in degrees, measured off its own albedo.
# This is the pivot: it is the colour that becomes the team colour exactly. It
# HAS to be per robot — five of the nine are cool-dominant and four are warm,
# so any single global pivot leaves one group barely repainted.
#
# The GAME does not read this table: ArenaBuilder.DominantHue measures the same
# thing at build time and stores it on the roster entry, so a robot added later
# needs no edit there. These values are here so a preview can be rendered
# without Unity, and they are what that code produced.
DOMINANT_HUE = {
    "bolt": 200, "hawk": 40, "knight": 30, "panther": 20, "racer": 10,
    "ranger": 180, "samurai": 180, "scout": 180, "titan": 200,
}


def smoothstep(edge0, edge1, x):
    t = min(1.0, max(0.0, (x - edge0) / max(1e-6, edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def hue_of(rgb):
    return colorsys.rgb_to_hsv(*rgb)[0]


def recolor_pixel(r, g, b, team_hue, anchor_hue, spread):
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)

    weight = STRENGTH * smoothstep(GREY_CUTOFF, GREY_CUTOFF + GREY_SOFTNESS, s)

    # Fold, not rotate: the distance from the robot's OWN dominant hue is taken
    # as an absolute value, so the dominant colour lands exactly on the team hue
    # and everything else fans out to one side of it. A signed rotation sends
    # some robots' accents cool instead — which put the bolt's yellow trim in
    # violet, next to the cyan team it is supposed to contrast with.
    delta = abs(((h - anchor_hue + 0.5) % 1.0) - 0.5)
    hue = (team_hue + spread * delta) % 1.0

    pr, pg, pb = colorsys.hsv_to_rgb(hue, max(s, SATURATION_FLOOR), v)
    out = [c + (p - c) * weight for c, p in
           zip((r / 255.0, g / 255.0, b / 255.0), (pr, pg, pb))]

    wr, wg, wb = colorsys.hsv_to_rgb(team_hue, SATURATION_FLOOR, v)
    wash = NEUTRAL_WASH * (1.0 - weight)
    out = [c + (w - c) * wash for c, w in zip(out, (wr, wg, wb))]
    return tuple(int(round(min(1.0, max(0.0, c)) * 255)) for c in out)


def repaint_image(image, team_hue, anchor_hue, spread):
    """Per-pixel over a downscaled copy — this is a preview, not a shipped asset."""
    rgb = image.convert("RGB")
    if max(rgb.size) > 512:
        scale = 512 / max(rgb.size)
        rgb = rgb.resize((max(1, int(rgb.width * scale)), max(1, int(rgb.height * scale))),
                         Image.LANCZOS)

    # Cache by quantised colour: these atlases are flat-shaded panels, so a few
    # hundred distinct colours cover a 512x512 image.
    cache, out = {}, []
    for pixel in rgb.getdata():
        key = (pixel[0] >> 2, pixel[1] >> 2, pixel[2] >> 2)
        if key not in cache:
            cache[key] = recolor_pixel(*pixel, team_hue, anchor_hue, spread)
        out.append(cache[key])
    result = Image.new("RGB", rgb.size)
    result.putdata(out)
    return result


def repaint_glb(source, out_path, spread, anchor_degrees):
    gltf, binary = read_glb(source)
    team_hue = hue_of(AWAY_TEAM)
    anchor_hue = (anchor_degrees % 360) / 360.0

    binary = bytearray(binary)
    views = gltf.get("bufferViews", [])
    rebuilt = bytearray()
    remap = {}

    # Rebuilt rather than patched in place: a repainted PNG is a different size
    # from the JPEG it replaces, so every later view's offset would shift.
    for index, view in enumerate(views):
        start = view.get("byteOffset", 0)
        chunk = bytes(binary[start:start + view["byteLength"]])
        remap[index] = chunk

    for image in gltf.get("images", []):
        if "bufferView" not in image:
            continue
        index = image["bufferView"]
        try:
            decoded = Image.open(io.BytesIO(remap[index]))
        except Exception:
            continue
        painted = repaint_image(decoded, team_hue, anchor_hue, spread)
        buffer = io.BytesIO()
        painted.save(buffer, format="PNG", optimize=True)
        remap[index] = buffer.getvalue()
        image["mimeType"] = "image/png"

    for index, view in enumerate(views):
        rebuilt += b"\x00" * (-len(rebuilt) % 4)
        view["byteOffset"] = len(rebuilt)
        view["byteLength"] = len(remap[index])
        view["buffer"] = 0
        rebuilt += remap[index]

    gltf["buffers"] = [{"byteLength": len(rebuilt)}]
    write_glb(out_path, gltf, bytes(rebuilt))
    return out_path


if __name__ == "__main__":
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    out_dir, robot = sys.argv[1], sys.argv[2]
    spreads = [float(s) for s in sys.argv[3:]] or [0.0, 0.55]
    os.makedirs(out_dir, exist_ok=True)

    source = f"{ROOT}/Assets/Models/Stages/{robot}/stage1.glb"
    if not os.path.exists(source):
        source = f"{ROOT}/Assets/Models/Meshy/{robot}-rig.glb"

    anchor = DOMINANT_HUE.get(robot)
    if anchor is None:
        raise SystemExit(f"No dominant hue recorded for '{robot}'.")

    for spread in spreads:
        name = f"{robot}_spread{int(round(spread * 100)):03d}.glb"
        print("wrote", repaint_glb(source, os.path.join(out_dir, name), spread, anchor))

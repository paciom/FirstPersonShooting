#!/usr/bin/env python3
"""Paint the ROOM for each beat of an adventure (MiniMax image-01).

    python Tools/adventure_art.py                 # everything missing
    python Tools/adventure_art.py n01 n19         # just these beats
    python Tools/adventure_art.py --force n19     # repaint one
    python Tools/adventure_art.py --check         # prompts vs JSON, no spend

These are PLATES, not finished stills: every prompt is explicitly empty of
robots, because the characters are composited in afterwards from the real
reference renders (Tools/adventure_cutout.py, then Tools/adventure_compose.py).

That split is the house rule, and it is not squeamishness. Asked for "a sleek
orange panther robot", an image model returns a handsome robot that is not
Panther — this project rejected exactly that once already for the menu cards.
The room is the half a painter is good at; the character is the half only the
real model gets right.

Each plate is framed from that node's KEY shot in the story JSON (the one shot
marked "key"), and reserves empty ground where the compositor will stand
somebody. Output: Tools/adventure_plates/<node>.png
"""

import base64
import json
import os
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADVENTURES = os.path.join(ROOT, "Assets", "Resources", "Adventures")
OUT = os.path.join(ROOT, "Tools", "adventure_plates")
STORY = "quiet-confirmation"
ENDPOINT = "https://api.minimax.io/v1/image_generation"

# ---------------------------------------------------------------- the DNA

STYLE = (
    "rendered in stylized 3D CGI like a modern animated feature: smooth shaded "
    "surfaces with soft gradients, glossy specular highlights, real depth and "
    "perspective, soft contact shadows and ambient occlusion, volumetric light "
    "through rain, shallow depth of field, clean low-poly toy-robot models with "
    "matte plastic-and-metal materials and glowing emissive panels, dark rainy "
    "night palette with cyan and ember accents. NOT a 2D illustration, NOT "
    "cel-shaded, NO flat colour fills, NO black outlines, NO comic book look"
)
EMPTY = ("COMPLETELY EMPTY of robots, mecha, characters, people, animals and "
         "vehicles. Nothing alive anywhere in the frame.")
NO_TEXT = ("no text, no letters, no words, no numbers, no logo, no watermark, "
           "no UI, no black bars, no letterboxing")

# Sets. Every beat is shot in one of these, so naming them once is what keeps
# thirty-six plates looking like one location scout.
GANTRY = ("a high steel gantry walkway above a canyon of blast furnaces built "
          "into a hollowed hill, molten orange light pouring up through the "
          "grating, chimneys and smoke, hard rain")
PIPE = ("the ribbed rusted interior of a two-hundred-year-old fuel pipeline, "
        "cramped, wet, a hatch of pale light at the far end")
SCAR = ("a strip-mine scar at night: a canyon of torn earth kilometres long "
        "cut through a dead gray city, faint steam rising off warm ground")
STREET = ("a dead gray city street scoured flat by two centuries of dust, "
          "ruined houses, wrecked cars, deep puddles, no plants anywhere, rain")
ROOFS = ("wet ruined rooftops above a dead gray city district at night, "
         "parapets, fallen billboards, rain sheeting off every edge")
DEPOT = ("a floodlit Warden supply yard at night: a cargo sled, stacked trays "
         "of dark soil, sealed water drums, cyan work lights, wet concrete")
HAVEN = ("the inside of a ruined stadium at night lit by cyan lanterns: rows "
         "of empty soil trays on the field, and along one wall a long polished "
         "shelf of dormant robot cores, each a dark sphere on a stand with a "
         "small name tag")
YARD = ("a small ruined suburban backyard at night in light rain, a collapsed "
        "wooden treehouse in a dead oak, a cut chain-link fence, a rusted "
        "watering can, wet gray dirt")
TREEHOUSE = ("the inside of a ruined wooden treehouse at night, one small warm "
             "lamp on a little table, broken rail open to the rain")
TRANSPORT = ("the inside of a heavy industrial transport at night, racks of "
             "cutting torches swaying, ember-orange work light, rain outside")
CRAWLER = ("a mountain-sized strip-mining machine filling the mouth of a "
           "valley: kilometre-wide treads, gantries and furnace glow in its "
           "belly, a bow blade throwing a wave of dust, seen at dawn in rain")
DECK = ("the open command deck high on a colossal mining machine at gray dawn, "
        "wet steel plating, a heavy rail, rain streaming, the valley far below")
SPROUT = ("a single tiny green sprout with two leaves growing out of clean wet "
          "dirt, glowing faintly, with a small cyan lantern on the ground "
          "beside it")

# node -> plate prompt. Framing follows that node's key shot; the "empty"
# clause says where the compositor needs bare ground.
PLATES = {
    "n01": f"wide shot at the foot of {GANTRY}, seen from the dust flats below, the "
           f"lit wall towering above with one small figureless walkway. The lower "
           f"middle of the frame is open wet ground. {EMPTY}",
    "n02": f"low angle deep inside {PIPE}, looking down the tunnel, a swarm of tiny "
           f"four-legged survey drones with red eyes crawling along the far wall. The "
           f"near half of the pipe floor is empty. no robots, no characters, no people",
    "n03": f"wide low shot down {SCAR}, one harsh white searchlight beam swinging in "
           f"from the right and pooling on the open ground. The centre of the frame is "
           f"empty lit ground. {EMPTY}",
    "n04": f"high angle looking down into {DEPOT}, a tipped-over cargo sled spilling "
           f"soil trays across the wet concrete, a ring of cyan lantern lights around "
           f"the edges. The centre is empty floodlit ground. {EMPTY}",
    "n05": f"low angle on a rain-soaked ruined street corner at night, a small smashed "
           f"four-legged survey drone lying on the wet asphalt with a pale cyan "
           f"hologram projecting up out of its broken casing: an ABSTRACT glowing "
           f"grid map of city blocks, pure lines and one bright ring, no writing on "
           f"it. No shop fronts, no signs, no neon lettering anywhere. {EMPTY} except "
           f"the tiny broken drone",
    "n06": f"a small scuffed dented pre-Leaving human security drone hovering in heavy "
           f"rain above {ROOFS}, one patient blue lens iris, chipped white paint, "
           f"rotors, hopelessly out of date. Empty rooftop in the foreground. no other "
           f"robots, no mecha, no people",
    "n07": f"wide low angle on {STREET} at night with a blinding searchlight beam "
           f"flooding in from behind camera, and a heavy industrial transport stacked "
           f"with cutting torch racks grinding out of the dust in the background. The "
           f"middle of the frame is empty lit wet ground. no robots, no mecha, no people",
    "n08": f"crane view over a parapet across {ROOFS}, and far below in a gap between "
           f"ruined houses a single small backyard ringed by fifty tiny cyan lantern "
           f"lights in a perfect circle, too far away to see what is inside. The near "
           f"rooftop is empty. no robots in the foreground, no mecha, no people",
    "n09": f"extreme close along a long metal shelf inside {HAVEN}: rows of dark "
           f"dormant robot cores, each a smooth sphere sitting in a cradle with a "
           f"small blank metal name plate in front of it, cyan lantern light raking "
           f"across them, a dropped polishing cloth on the floor. The shelf and the "
           f"spheres fill the frame. {EMPTY}",
    "n10": f"macro at ground level in {YARD}: a single deep sharp-edged boot print "
           f"pressed into wet mud filling slowly with rainwater, filling most of the "
           f"frame, with freshly cut chain-link wire ends hanging above it. {EMPTY}",
    "n11": f"high angle down into a huge rusted recycler machine in a blacked-out "
           f"salvage yard, its steel jaw slowly turning, three tiny cyan lantern lights "
           f"trapped deep inside it, sparks from a torn-open junction box nearby. no "
           f"robots visible, no characters, no people",
    "n12": f"over-shoulder view inside {TRANSPORT}, a lit cutting torch held out "
           f"handle-first into the foreground, roaring orange flame, empty bench seats "
           f"either side. no robots, no characters, no people",
    "n13": f"eye level in {YARD} at night, a ring of fifty cyan lanterns set on the "
           f"ground opening like a door onto bare dark soil, rain falling through the "
           f"light. The gap in the ring is empty ground. {EMPTY}",
    "n14": f"top-down aerial straight down onto {YARD}, a flawless glowing cyan circle "
           f"forty meters across painted on the bare soil by a sensor pulse, the "
           f"collapsed treehouse at the edge of frame. {EMPTY}",
    "n15": f"close on a battered cyan lantern held up into frame by nobody, wicking to "
           f"full brightness, dust and emergency light behind it in {HAVEN}. {EMPTY}",
    "n16": f"over-shoulder framing inside {TREEHOUSE}, a child's crayon drawing on "
           f"yellowed paper propped in the lamplight on the little table showing a "
           f"stick-figure girl and a big smiling robot holding a watering can. {EMPTY}",
    "n17": f"wide shot of a flooded ruined crossroads at night, one way leading to a "
           f"dark garden fence, every other way filling with converging orange "
           f"headlights, a burning transport silhouetted far behind. The centre of the "
           f"crossroads is empty wet ground. {EMPTY}",
    "n18": f"crane view over {YARD} at night, a perfect ring of molten orange trench "
           f"two meters deep freshly cut in the dirt right around the yard, sealing it "
           f"in, steam and smoke rising off the cut. {EMPTY}",
    "n19": f"extreme low angle at ground level in {YARD}, {SPROUT} in the centre-left "
           f"of frame. IMPORTANT: the right half of the frame is open empty wet ground "
           f"with nothing standing in it. {EMPTY}",
    "n20": f"macro insert in the rain on a wet rooftop: two small green leaves and a "
           f"clod of soil, the leaf edges curling inward and going gray. {EMPTY}",
    "n21": f"crane view looking down into the centre of a ring of fifty cyan lanterns "
           f"on wet dirt at night, and at the exact centre of the circle {SPROUT}. The "
           f"ring itself is only lanterns on the ground. {EMPTY}",
    "n22": f"two-shot framing across a little table inside {TREEHOUSE}, the lamp flame "
           f"leaning between two empty seats, rain loud on the roof. {EMPTY}",
    "n23": f"wide shot from inside {TREEHOUSE} looking out through the broken rail "
           f"into a black downpour, the little table empty, one lamp burning. {EMPTY}",
    "n24": f"crane view rising over a muddy dead-end in front of {YARD} in heavy rain, "
           f"the ground churned, six sets of hard orange headlights aimed in from the "
           f"far side. The near ground is empty mud. no robots, no mecha, no people",
    "n25": f"low angle in {YARD} at night, {SPROUT} shivering in the foreground, and "
           f"far off to the east a bank of orange torch-glow rising behind the ruins. "
           f"Open wet ground behind the sprout. {EMPTY}",
    "n26": f"macro on a rain-covered industrial display panel at night, water running "
           f"over the glass, showing only an abstract pale cyan waveform line and one "
           f"glowing dot. ABSOLUTELY NO letters, no words, no glyphs, no writing, no "
           f"symbols of any kind on the screen. {EMPTY}",
    "n27": f"macro insert of a shallow tray of wet dark soil and a small lit cyan "
           f"lantern being held out into frame at night in the rain, other lanterns "
           f"burning out of focus behind. {EMPTY}",
    "n28": f"wide low angle across the open top deck of a colossal moving machine at "
           f"gray dawn: flat wet steel plating underfoot, a heavy industrial guard "
           f"rail running across the frame, rain streaming off it, and a vast valley "
           f"and distant ridge far below beyond the rail. NOT a railway, no train "
           f"tracks, no rails on the ground. {EMPTY}",
    "n29": f"wide shot of {CRAWLER} bearing down on a low garden wall, dust breaking "
           f"over the wall like surf, a line of small cyan lanterns set along the wall. "
           f"The foreground along the wall is empty. no robots, no mecha, no people",

    "e_catch": f"a wet steel boarding ladder running up the flank of {CRAWLER}, seen "
               f"close in the rain fifteen meters up, rungs streaming, the treads "
               f"grinding past far below. {EMPTY}",
    "e_witness": f"{DECK} at gray dawn, rain easing, a dented sage-green seed tin with "
                 f"a faded hand-painted flower label lying on the wet plating in the "
                 f"foreground light. {EMPTY}",
    "e_watch": f"close on the long polished shelf of dormant robot cores in {HAVEN}, a "
               f"fresh blank name tag set at the near end, a polishing cloth beside it, "
               f"rows of cores glowing faintly away into the dark. {EMPTY}",
    "e_shelf": f"the inside of the stadium at {HAVEN} but decades later and healed: "
               f"the soil trays are full of green shoots, and the open stadium door "
               f"pours in daylight from a BLUE sky across the whole field. The shelf of "
               f"cores is in the foreground shadow, empty of anyone. {EMPTY}",
    "e_dusting": f"morning light through the ruined roof of {HAVEN}, the long shelf of "
                 f"name-tagged cores catching the light, a polishing cloth folded on "
                 f"the shelf edge, smoke on the horizon through a gap in the wall. "
                 f"{EMPTY}",
    "e_dead_proof": f"macro in the rain on {GANTRY}: one ENORMOUS open ROBOT PALM made "
                    f"of riveted painted steel plates, mechanical fingers, upturned and "
                    f"filling the frame, with two small grey flakes falling into it. A "
                    f"machine hand only — no skin, no flesh, no human hand, no "
                    f"fingernails, no robot body visible",
    "e_captain": f"wide shot along an empty high gantry over {GANTRY} at dawn, furnace "
                 f"light along the rail, nothing burning, the whole walkway deserted. "
                 f"{EMPTY}",
}


def nodes():
    with open(os.path.join(ADVENTURES, STORY + ".json"), "r", encoding="utf-8") as f:
        return [n["id"] for n in json.load(f)["nodes"]]


def check():
    ids = nodes()
    missing = [n for n in ids if n not in PLATES]
    extra = [k for k in PLATES if k not in ids]
    for n in missing:
        print("  no plate prompt for node " + n)
    for k in extra:
        print("  plate prompt for a node that does not exist: " + k)
    longest = max((len(f"{p}. {STYLE}. {NO_TEXT}"), k) for k, p in PLATES.items())
    print(f"  longest prompt {longest[0]} chars ({longest[1]}); MiniMax rejects over 1500")
    print("%d plate prompts, %d nodes" % (len(PLATES), len(ids)))
    return not missing and not extra and longest[0] <= 1500


def generate(name, force=False):
    path = os.path.join(OUT, name + ".png")
    if os.path.exists(path) and not force:
        print(f"  skip  {name}")
        return path
    body = json.dumps({
        "model": "image-01",
        "prompt": f"{PLATES[name]}. {STYLE}. {NO_TEXT}",
        "aspect_ratio": "16:9",
        "response_format": "url",
        "n": 1,
        # OFF on purpose: the optimizer rewrites these toward cinematic
        # photorealism and quietly puts robots back into an empty plate.
        "prompt_optimizer": False,
    }).encode()
    req = urllib.request.Request(ENDPOINT, data=body, headers={
        "Authorization": "Bearer " + os.environ["MINIMAX_API_KEY"],
        "Content-Type": "application/json",
    })
    payload = json.loads(urllib.request.urlopen(req, timeout=300).read())
    if payload.get("base_resp", {}).get("status_code") != 0:
        raise RuntimeError(f"{name}: {payload.get('base_resp')}")
    os.makedirs(OUT, exist_ok=True)
    with urllib.request.urlopen(payload["data"]["image_urls"][0], timeout=300) as src, \
            open(path, "wb") as dst:
        dst.write(src.read())
    print(f"  ok    {name}")
    return path


# ---------------------------------------------------------------- one-pass
# The other way to make these stills: hand a model the robot's own reference
# render and ask for the whole frame, character and room together, in one go.
# That is the better pipeline when it works — no compositing, and the character
# can be in poses the reference renders cannot strike.
#
# It does not work on MiniMax. Tested four ways (clean cutout on white, the
# front preview render, width/height instead of aspect_ratio, optimizer on and
# off): subject_reference returns a handsome robot that is not Panther every
# time, and drops the toon look for photoreal gloom.
#
# It should work on BytePlus Seedream 4.0, which does real reference
# conditioning — but that model answers "ModelNotOpen" on this account: it has
# to be activated once in the Ark console. Everything below is ready for the
# moment it is, and until then it fails with that message and changes nothing.

ARK_MODEL = "seedream-4-0-250828"
CAST_REFS = {                       # which real render stands in for whom
    "panther": "Tools/adventure_cutouts/panther_hero.png",
    "titan": "Tools/adventure_cutouts/titan_hero.png",
    "samurai": "Tools/adventure_cutouts/samurai_hero.png",
    "bolt": "Tools/adventure_cutouts/bolt_front.png",
    "warden": "Tools/adventure_cutouts/ranger_front.png",
}


def ark_config():
    env = {}
    path = os.path.join(ROOT, ".secrets", "byteplus.env")
    for line in open(path, encoding="utf-8"):
        if "=" in line:
            key, value = line.strip().split("=", 1)
            env[key] = value
    return env["BYTEPLUS_ARK_BASE_URL"].rstrip("/"), env["ARK_API_KEY"]


def data_uri(path):
    with open(os.path.join(ROOT, path), "rb") as handle:
        return "data:image/png;base64," + base64.b64encode(handle.read()).decode()


def cast_in(node):
    """Who is in this beat, read off the key shot's own words."""
    text = " ".join(s.get("action", "") + " " + s.get("vo", "")
                    for s in node.get("shots", [])).lower()
    return [name for name in CAST_REFS if name in text]


def generate_ark(node, force=False):
    """One pass: the room and the real robots together, conditioned on the
    reference renders."""
    name = node["id"]
    path = os.path.join(ADVENTURES, STORY, name + ".png")
    if os.path.exists(path) and not force:
        print(f"  skip  {name}")
        return
    key = next((s for s in node.get("shots", []) if s.get("key")), node["shots"][0])
    who = cast_in(node)
    refs = [data_uri(CAST_REFS[w]) for w in who]
    prompt = (
        f"{key['cam']} shot. {key['action']} "
        + (f"Use the robots in the reference images EXACTLY as the characters: same "
           f"colours, same proportions, same head designs, same plating. " if refs else "")
        + f"Setting: {PLATES[name].split('. ' + EMPTY)[0]}. {STYLE}. {NO_TEXT}")

    host, api_key = ark_config()
    body = {"model": ARK_MODEL, "prompt": prompt, "size": "1280x720",
            "response_format": "url", "watermark": False}
    if refs:
        body["image"] = refs if len(refs) > 1 else refs[0]
    req = urllib.request.Request(host + "/images/generations",
        data=json.dumps(body).encode(),
        headers={"Authorization": "Bearer " + api_key, "Content-Type": "application/json"})
    try:
        payload = json.loads(urllib.request.urlopen(req, timeout=300).read())
    except urllib.error.HTTPError as error:
        detail = json.loads(error.read().decode()).get("error", {})
        if detail.get("code") == "ModelNotOpen":
            raise SystemExit(
                f"\n{ARK_MODEL} is not activated on this BytePlus account.\n"
                "Activate it once in the Ark console (Model Services -> Seedream 4.0),\n"
                "then re-run:  python Tools/adventure_art.py --ark --force\n")
        raise RuntimeError(f"{name}: {detail}")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    url = payload["data"][0]["url"]
    with urllib.request.urlopen(url, timeout=300) as src, open(path, "wb") as dst:
        dst.write(src.read())
    print(f"  ok    {name}  ({', '.join(who) or 'no cast'})")


def main(argv):
    if "--check" in argv:
        sys.exit(0 if check() else 1)
    force = "--force" in argv
    wanted = [a for a in argv if not a.startswith("--")] or nodes()
    for name in wanted:
        if name not in PLATES:
            sys.exit("unknown beat: " + name)

    if "--ark" in argv:
        with open(os.path.join(ADVENTURES, STORY + ".json"), "r", encoding="utf-8") as f:
            story = {n["id"]: n for n in json.load(f)["nodes"]}
        for name in wanted:
            generate_ark(story[name], force=force)
        return
    failures = []

    def run(name):
        try:
            generate(name, force=force)
        except Exception as error:
            print(f"  FAIL  {name}: {error}")
            failures.append(name)

    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(run, wanted))
    if failures:
        print("retry with: python Tools/adventure_art.py " + " ".join(failures))
        sys.exit(1)


if __name__ == "__main__":
    main(sys.argv[1:])

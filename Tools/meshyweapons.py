"""Generates the held models for the sixty arsenal weapons.

Same two-stage split as meshyvehicles.py, and for the same reason: `preview`
buys geometry only (~5 credits each) and `refine` (~25) only textures the mesh
it is given, so the shape is already final when the thumbnail appears. Paying to
texture sixty guns nobody has looked at is 1500 credits on a coin flip; paying
for sixty previews is 300 to find out. Preview everything, look at the sheet,
then refine by name.

NAMES ARE THE CONTRACT. Each key here is the kebab-case of the weapon's C# class
(PrismSplitter -> prism-splitter), because WeaponArt.KeyFor derives the same
string at runtime and loads Assets/Resources/Weapons/<key>.glb. A file named
anything else is a weapon that silently keeps the fallback silhouette.

Colours live in the refine texture_prompt, not the base prompt -- refine's
texture_prompt overrides whatever the base prompt asked for, which is how a
previous fleet came out orange and teal regardless.

Every prompt asks for a HELD weapon with a grip, barrel along its length and
nothing else in frame. The runtime turns the longest axis down +Z and scales to
half a metre (WeaponArt.NormalizeAlongZ), so a model that arrives as a gun on a
stand is a gun with a stand welded to its barrel.

  python meshyweapons.py preview [name ...]      # all 60 if no names given
  python meshyweapons.py status
  python meshyweapons.py thumbs <dir>
  python meshyweapons.py refine <name> [name ...]
  python meshyweapons.py download [name ...]
"""
import json
import os
import sys
import urllib.error
import urllib.request

ROOT = "D:/Claude/FirstPersongShooting"
KEY_PATH = f"{ROOT}/.secrets/meshy_key.txt"
# Task ids are not secrets, and refine needs them days later — so they live in
# the repo rather than next to the key, where a cleanup would take them out.
STATE_PATH = f"{ROOT}/Tools/weapon_tasks.json"
OUT_DIR = f"{ROOT}/Assets/Resources/Weapons"
API = "https://api.meshy.ai/openapi"

STYLE = ("chunky stylised low-poly toy look, clean flat panels, glowing light strips, "
         "friendly sci-fi, held weapon with a pistol grip and trigger, barrel pointing "
         "along its length, floating in empty space, no base, no pedestal, no stand, "
         "no hands, no arms")
NEGATIVE = ("base, pedestal, stand, platform, ground plane, shadow plane, text, logo, "
            "hand, arm, human, soldier, gore, realistic military, rust, dirt")

# (shape, colours). Shape goes in the preview prompt and decides the geometry;
# colours go in the refine texture_prompt and decide nothing but the paint.
#
# Each shape says what the gun DOES, because that is what makes it recognisable
# in a rack of sixty thumbnails: the ice guns are crystalline, the sound guns
# have speaker cones, the goo guns have tanks and hoses.
WEAPONS = {
    # ---- CORE: the four every robot has always carried -------------------
    "laser-blaster": ("compact snub-nosed energy pistol with a short vented barrel "
                      "and a glowing power cell behind the grip",
                      "white and light grey shell with bright cyan light strips"),
    "photon-beam": ("slim beam rifle with a long focusing tube and three stacked lens "
                    "rings near the muzzle",
                    "pale grey body with a hot cyan emitter and white glow rings"),
    "plasma-lobber": ("stubby wide-mouthed grenade launcher with a fat round chamber "
                      "and a top-mounted sight",
                      "olive grey casing with lime green plasma glow"),
    "rail-zapper": ("long thin railgun with twin parallel rails and a heavy capacitor "
                    "block over the grip",
                    "gunmetal frame with violet arcing light between the rails"),

    # ---- LIGHT & photon --------------------------------------------------
    "prism-splitter": ("angular rifle with a large faceted glass prism mounted where the "
                       "barrel splits into three short emitters",
                       "white shell with a rainbow-refracting prism and pale gold trim"),
    "strobe-burst": ("short wide-mouthed flash gun with a ring of bulb emitters around "
                     "the muzzle and a drum battery",
                     "bright white casing with yellow bulbs and warm glow"),
    "sunflare-cannon": ("heavy shoulder cannon with a wide dish muzzle and radiating fins "
                        "like a sunburst",
                        "cream and gold plating with an orange-white burning core"),
    "glowworm-launcher": ("soft rounded launcher with a curved segmented tube and small "
                          "round pods loaded along the top",
                          "mint green shell with glowing yellow-green pods"),
    "mirror-ricochet": ("boxy rifle with angled mirror plates folded along the barrel and "
                        "a reflective muzzle wedge",
                        "polished silver panels with pale blue reflections"),
    "halo-ring-gun": ("ring launcher with a large open circular muzzle hoop and a thin "
                      "body underneath",
                      "white body with a glowing gold ring"),
    "blacklight-marker": ("slim marker pistol with a stubby UV lamp head and a small side "
                          "canister",
                          "matte charcoal body with deep violet lamp glow"),

    # ---- ELECTRIC & magnetic ---------------------------------------------
    "arc-whip": ("whip-handle weapon with a short thick grip and a coiled cable spooled "
                 "on a drum at the front",
                 "black grip with copper coils and crackling electric blue cable"),
    "tesla-turret-thrower": ("launcher holding a small folded tesla coil turret in an open "
                             "front cradle",
                             "steel grey frame with a copper coil and blue sparks"),
    "magnet-ram": ("blunt heavy projector with a horseshoe magnet head and thick cable "
                   "wrapping",
                   "red and blue magnet head on a dark steel body"),
    "static-shotgun": ("short double-barrel shotgun with electrode prongs splayed at each "
                       "muzzle",
                       "dark gunmetal with pale yellow static glow at the prongs"),
    "volt-boomerang": ("thrower with a flat V-shaped boomerang blade seated in a slot on "
                       "top of a compact grip",
                       "white blade edged in electric blue on a slate grey thrower"),
    "ion-rain": ("upward-angled flak launcher with a cluster of four thin tubes and a "
                 "curved shoulder brace",
                 "steel blue tubes with pale cyan ion glow"),

    # ---- PLASMA & heat ---------------------------------------------------
    "comet-sling": ("sling launcher with a curved forked arm and a glowing orb held in "
                    "the fork",
                    "bronze arms with a blazing orange-white comet orb"),
    "ember-gatling": ("compact gatling gun with six short rotating barrels and a side "
                      "ammo drum",
                      "soot black barrels glowing ember orange, brass drum"),
    "magma-mortar": ("squat mortar with a thick flared tube angled upward and a chunky "
                     "base plate",
                     "charcoal iron with cracked glowing lava seams"),
    "phoenix-dart": ("sleek dart rifle with swept feather-like fins along the barrel",
                     "crimson and gold plumage plating with a fiery muzzle"),
    "heat-haze-projector": ("wide-mouthed heat projector with radiator fins stacked along "
                            "the top and a mesh muzzle",
                            "sand beige plates with shimmering orange fin glow"),

    # ---- CRYO & ice ------------------------------------------------------
    "frostbite-beam": ("beam rifle with a long frosted crystal barrel and a coolant "
                       "bottle underneath",
                       "icy white shell with pale blue frost and a clear crystal barrel"),
    "icicle-flechette": ("needle gun with a cluster of long thin ice spikes fanned at the "
                         "muzzle",
                         "frosted white body with translucent pale blue spikes"),
    "snowglobe-grenade": ("stubby launcher with a clear round globe chamber holding "
                          "swirling snow",
                          "white and silver casing with a clear glass globe"),
    "glacier-wall": ("broad projector with a flat wide emitter plate like a shield and "
                     "two side cylinders",
                     "pale ice blue plate with deep blue crystal edging"),

    # ---- SOUND & waves ---------------------------------------------------
    "bass-dropper": ("heavy speaker cannon with one huge round subwoofer cone at the "
                     "muzzle and a boxy amp body",
                     "matte black cabinet with a purple cone and glowing level bars"),
    "sonic-screech": ("narrow horn rifle with a long flared trumpet muzzle",
                      "brushed brass horn on a slate grey body"),
    "echo-locator": ("scanner pistol with a small parabolic dish and a ring of tiny "
                     "emitters around it",
                     "off-white shell with a teal dish and soft green pulses"),
    "drumline-cannon": ("launcher built around three stacked drum heads with a short "
                        "muzzle in the middle",
                        "red drum shells with chrome rims and warm orange glow"),
    "wave-rider": ("wide flat emitter with a rippled fan-shaped muzzle and side rails",
                   "aqua blue panels with white foam-crest highlights"),

    # ---- GRAVITY & force -------------------------------------------------
    "black-hole-yoyo": ("launcher with a dark sphere held on a short arm in a forked "
                        "front cradle",
                        "deep space black sphere with a violet accretion ring, dark frame"),
    "repulsor-palm": ("palm-mounted emitter shaped like a broad round plate with a glowing "
                      "centre disc and knuckle bar",
                      "white plating with a bright cyan repulsor disc"),
    "moonboots-beam": ("upward-angled lifter with a broad conical emitter and two side "
                       "stabiliser vanes",
                       "pale silver-grey with soft lilac anti-gravity glow"),
    "meteor-caller": ("heavy sky-aimed launcher with a wide open bore and a targeting "
                      "ring above it",
                      "dark rock grey with fiery orange bore glow"),
    "orbit-launcher": ("launcher with two thin curved rails arcing over the barrel like an "
                       "orbit ring",
                       "white and navy with a bright gold orbit ring"),

    # ---- GOO, bubbles & slime --------------------------------------------
    "bubble-blower": ("round-mouthed bubble gun with a wide hoop muzzle and a clear soap "
                      "tank underneath",
                      "glossy pastel pink shell with a clear tank and iridescent hoop"),
    "goo-gusher": ("hose gun with a fat nozzle and a slime tank strapped over the top",
                   "yellow tank with bright green slime and dark grey nozzle"),
    "bouncy-ball-cannon": ("wide-bore cannon with a clear hopper of coloured balls feeding "
                           "the top",
                           "bright blue casing with a clear hopper of rainbow balls"),
    "glue-grenade": ("chunky grenade launcher with a thick round canister chamber and a "
                     "drip-shaped muzzle",
                     "mustard yellow body with sticky amber glue and grey trim"),
    "paint-bomber": ("paintball-style launcher with a fat hopper on top and a stubby "
                     "barrel",
                     "white body splattered with pink, cyan and yellow paint"),

    # ---- NATURE & elemental ----------------------------------------------
    "tornado-tube": ("long open tube launcher with spiral vanes running down the inside",
                     "slate grey tube with pale swirling white-green wind glow"),
    "vine-snare": ("organic launcher with thick woven vines wrapping the barrel and a "
                   "bud-shaped muzzle",
                   "bark brown body with living green vines and a pink bud"),
    "thundercloud-pet": ("short launcher with a small round cloud shape cradled above the "
                         "barrel",
                         "dark grey cloud with yellow lightning, pale blue launcher"),
    "sandstorm-sprayer": ("wide fan sprayer with a broad slotted muzzle and a hopper of "
                          "sand behind it",
                          "desert tan plating with swirling ochre sand"),
    "geyser-rod": ("tall thin rod launcher with a valve wheel and a jet nozzle at the tip",
                   "copper pipe body with white steam and blue water glow"),

    # ---- GADGETS & exotic -------------------------------------------------
    "portal-pistol": ("rounded sci-fi pistol with a glowing oval aperture set into the "
                      "front instead of a barrel",
                      "white and orange shell with a swirling blue portal aperture"),
    "clone-decoy-caster": ("caster with a small holographic figurine standing in an open "
                           "projector cradle",
                           "pale grey body with a flickering translucent cyan hologram"),
    "time-bubble-bomb": ("launcher holding a round hourglass-shaped charge in a clear "
                         "front chamber",
                         "brushed gold frame with a pale amber time bubble"),
    "swarm-hive": ("hexagonal hive box launcher with an open honeycomb face at the front",
                   "amber honeycomb face on a dark yellow-brown box"),
    "ricochet-disc": ("disc thrower with a flat circular blade seated in a slotted "
                      "launcher arm",
                      "chrome disc with cyan edge glow on a dark grey thrower"),
    "shrink-ray": ("bulbous ray gun with a tapering coil barrel and a big dial on the side",
                   "retro mint green shell with a pink coil and chrome dial"),
    "mimic-cube": ("boxy launcher with a small floating cube held in an open front frame",
                   "matte white frame with a glossy checkered cube"),
    "fireworks-finale": ("bundle of five short firework tubes strapped together over a "
                         "simple grip",
                         "festive red and gold tubes with sparkling multicolour tips"),

    # ---- MISSILES ---------------------------------------------------------
    "seeker-missile": ("shoulder-fired missile launcher with a single fat rocket loaded in "
                       "an open rail and a boxy sight",
                       "olive drab tube with a white rocket and cyan seeker eye"),
    "hornet-swarm": ("boxed multi-missile launcher with a four-by-two grid of small "
                     "rocket tubes",
                     "black and yellow hornet-striped box with orange tips"),
    "cluster-missile": ("bulky launcher with one wide segmented missile showing split "
                        "lines along its body",
                        "grey-white missile with red banding on a dark launcher"),
    "sky-striker": ("upward-angled launcher with a long slim missile and a folding "
                    "elevation brace",
                    "sky blue and white missile with a pale grey launcher"),
    "skimmer-rocket": ("low flat launcher with a wide winged rocket lying along the rail",
                       "sea grey rocket with orange wing edges"),
    "siege-torpedo": ("heavy squat launcher with one very fat blunt-nosed torpedo",
                      "iron grey torpedo with heavy red warning bands"),
}


def key():
    with open(KEY_PATH) as f:
        return f.read().strip()


def call(method, path, payload=None):
    request = urllib.request.Request(
        f"{API}{path}",
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={"Authorization": f"Bearer {key()}", "Content-Type": "application/json"},
        method=method,
    )
    try:
        with urllib.request.urlopen(request) as response:
            return json.loads(response.read().decode())
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:400]
        raise SystemExit(f"HTTP {e.code} on {method} {path}\n{body}")


def state():
    if not os.path.exists(STATE_PATH):
        return {}
    with open(STATE_PATH) as f:
        return json.load(f)


def save(data):
    os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
    with open(STATE_PATH, "w") as f:
        json.dump(data, f, indent=2)


def balance():
    return call("GET", "/v1/balance")["balance"]


def pick(names):
    """Named weapons, or all of them. Unknown names are a typo, not a new gun."""
    if not names:
        return list(WEAPONS)
    for name in names:
        if name not in WEAPONS:
            raise SystemExit(f"unknown weapon '{name}' — see WEAPONS in this file")
    return list(names)


# --------------------------------------------------------------------- preview

def preview(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in pick(names):
        shape, _ = WEAPONS[name]
        # Skip anything already previewed: this runs over sixty weapons and a
        # re-run after a network drop must not buy the ones that landed twice.
        if data.get(name, {}).get("preview"):
            print(f"{name:22s} already previewed")
            continue
        prompt = f"a futuristic toy sci-fi weapon, {shape}, {STYLE}"
        result = call("POST", "/v2/text-to-3d", {
            "mode": "preview",
            "prompt": prompt[:800],
            "negative_prompt": NEGATIVE,
            # v2 accepts only "realistic"; the toy look comes from the prompt.
            "art_style": "realistic",
            "should_remesh": True,
            "symmetry_mode": "on",
            # Lower than the vehicles' 6000: these are held props seen at arm's
            # length in a corner of the screen, and sixty of them ship in a
            # WebGL build that is already large.
            "target_polycount": 3000,
        })
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["preview"] = task
        print(f"{name:22s} preview {task}")
        save(data)
    save(data)
    print(f"balance after:  {balance()}")


def status():
    data = state()
    for name, tasks in sorted(data.items()):
        line = [f"{name:22s}"]
        for stage in ("preview", "refine"):
            task = tasks.get(stage)
            if not task:
                continue
            info = call("GET", f"/v2/text-to-3d/{task}")
            line.append(f"{stage} {info['status']:10s} {info.get('progress', 0):3d}%")
            if info.get("thumbnail_url"):
                tasks[f"{stage}_thumb"] = info["thumbnail_url"]
            if info.get("model_urls"):
                tasks[f"{stage}_models"] = info["model_urls"]
        print("  ".join(line))
    save(data)


# ---------------------------------------------------------------------- refine

def refine(names):
    if not names:
        raise SystemExit("refine takes names on purpose — look at the thumbs first")
    data = state()
    print(f"balance before: {balance()}")
    for name in pick(names):
        preview_task = data.get(name, {}).get("preview")
        if not preview_task:
            print(f"{name:22s} no preview to refine")
            continue
        _, colours = WEAPONS[name]
        result = call("POST", "/v2/text-to-3d", {
            "mode": "refine",
            "preview_task_id": preview_task,
            "texture_prompt": f"{colours}, clean flat panels, glowing light strips, "
                              f"stylised toy finish",
        })
        task = result.get("result") or result.get("id")
        data[name]["refine"] = task
        print(f"{name:22s} refine {task}")
        save(data)
    save(data)
    print(f"balance after:  {balance()}")


# -------------------------------------------------------------------- download

def download(names):
    data = state()
    os.makedirs(OUT_DIR, exist_ok=True)
    for name in pick(names):
        tasks = data.get(name, {})
        task = tasks.get("refine") or tasks.get("preview")
        if not task:
            print(f"{name:22s} nothing generated yet")
            continue
        info = call("GET", f"/v2/text-to-3d/{task}")
        if info["status"] != "SUCCEEDED":
            print(f"{name:22s} {info['status']} — not ready")
            continue
        url = info["model_urls"]["glb"]
        # The name is the contract — WeaponArt.KeyFor derives exactly this at
        # runtime, and a file under any other name is a weapon that silently
        # keeps the fallback silhouette.
        path = f"{OUT_DIR}/{name}.glb"
        urllib.request.urlretrieve(url, path)
        print(f"{name:22s} {os.path.getsize(path) / 1024:7.0f} KB  {path}")


def thumbs(out_dir):
    """Saves the newest thumbnail for each weapon, for eyeballing before refine."""
    data = state()
    os.makedirs(out_dir, exist_ok=True)
    for name, tasks in sorted(data.items()):
        url = tasks.get("refine_thumb") or tasks.get("preview_thumb")
        if not url:
            print(f"{name:22s} no thumbnail yet — run status")
            continue
        path = os.path.join(out_dir, f"{name}.png")
        urllib.request.urlretrieve(url, path)
        print(f"{name:22s} {path}")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    rest = sys.argv[2:]
    if command == "preview":
        preview(rest)
    elif command == "status":
        status()
    elif command == "refine":
        refine(rest)
    elif command == "download":
        download(rest)
    elif command == "thumbs":
        thumbs(rest[0] if rest else f"{ROOT}/Tools/weapon_thumbs")
    else:
        raise SystemExit(__doc__)

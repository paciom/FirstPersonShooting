"""Generates the Brawl mode's fight animations via Meshy rig + animate.

WHY A RE-RIG. The nine robots were rigged once already (that is where
-rig/-walk/-run.glb came from) but no rig task ids were persisted, and the
animation endpoint takes a rig_task_id, not a model. So each robot is rigged
again (~5 credits) purely to obtain a live task id to hang animations off.
The re-rigged skeleton is NOT guaranteed to match the one the forged prefabs
already animate with — that is the probe's whole job: download one robot's
clips and diff joint paths against the existing rig before any fleet spend.
If they disagree, the fallback is wholesale (refresh that robot's
-rig/-walk/-run from the new rig output, re-run fixmeshymaterials + the
walker forge) so everything shares one skeleton again.

Uploads reuse meshyretexture's geometry-only rebuild (~210 KB instead of the
6.6 MB rig): rigging needs the surface, not the old skeleton. The original
albedo is extracted from the rig GLB and sent alongside, so the rigged output
comes back textured on the SAME UVs — which is what makes the wholesale
fallback viable at all.

Fight clip GLBs are downloaded into Assets/Models/Meshy/Fight/ — a subfolder,
so ArenaBuilder's non-recursive roster scan never mistakes them for robots.
Their materials are left untouched: only the animation curves are harvested
(BrawlMoveForge clones clips out), the meshes are never rendered.

Spend discipline (the 35-credit lesson): rig and animate REQUIRE explicit
robot names — there is no "default everything" on paying commands.

  python meshyfight.py rig ranger                  # ~5 credits each
  python meshyfight.py status                      # poll tasks, capture URLs
  python meshyfight.py animate ranger punch kick flykick   # ~3 credits each
  python meshyfight.py animate ranger              # ... all ten moves
  python meshyfight.py download ranger             # -> Assets/Models/Meshy/Fight/
"""
import base64
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meshyvehicles import balance, call            # noqa: E402
from meshyretexture import geometry_only, parse    # noqa: E402

ROOT = "D:/Claude/FirstPersongShooting"
MODELS = f"{ROOT}/Assets/Models/Meshy"
OUT_DIR = f"{MODELS}/Fight"
STATE_PATH = f"{ROOT}/Tools/fight_tasks.json"

ROBOTS = ("ranger", "titan", "scout", "hawk", "bolt",
          "samurai", "panther", "knight", "racer")

# Meshy animation library action ids, verified against
# docs.meshy.ai/en/api/animation-library on 2026-07-30. Names on the left are
# the Brawl vocabulary — BrawlMoveForge looks for Fight/<robot>-<move>.glb.
MOVES = {
    "stance":    89,    # Combat Stance        -> brawl idle
    "punch":     96,    # Kung Fu Punch
    "kick":      207,   # Roundhouse Kick
    "highkick":  215,   # High Kick            -> spare kick variant
    "flykick":   94,    # Flying Fist Kick
    "block":     138,   # Block1
    "hit":       174,   # Face Punch Reaction
    "knockdown": 187,   # Knock Down
    "blast":     125,   # Charged Spell Cast   -> PHOTON BLAST cast
    "victory":   59,    # Victory Cheer
}


def state():
    if not os.path.exists(STATE_PATH):
        return {}
    with open(STATE_PATH) as handle:
        return json.load(handle)


def save(data):
    with open(STATE_PATH, "w") as handle:
        json.dump(data, handle, indent=2)


def albedo_data_uri(name):
    """The rig GLB's embedded base-colour PNG, as a data URI (or None)."""
    gltf, buffer = parse(f"{MODELS}/{name}-rig.glb")
    try:
        material = gltf["materials"][0]
        texture = material["pbrMetallicRoughness"]["baseColorTexture"]["index"]
        image = gltf["images"][gltf["textures"][texture]["source"]]
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        data = buffer[start:start + view["byteLength"]]
    except (KeyError, IndexError):
        return None
    mime = image.get("mimeType", "image/png")
    return f"data:{mime};base64," + base64.b64encode(data).decode()


def find_urls(obj, prefix=""):
    """Every https URL in a response, flattened to {dotted.path: url}.

    The rigging and animation response schemas differ and are not fully
    documented; harvesting every URL and choosing by key later is sturdier
    than betting on exact field names.
    """
    found = {}
    if isinstance(obj, dict):
        for key, value in obj.items():
            found.update(find_urls(value, f"{prefix}{key}."))
    elif isinstance(obj, list):
        for index, value in enumerate(obj):
            found.update(find_urls(value, f"{prefix}{index}."))
    elif isinstance(obj, str) and obj.startswith("http"):
        found[prefix.rstrip(".")] = obj
    return found


def require_names(names, what):
    if not names:
        raise SystemExit(f"{what} costs credits — name the robots explicitly.")
    for name in names:
        if name not in ROBOTS:
            raise SystemExit(f"unknown robot {name!r} (know: {', '.join(ROBOTS)})")


# ----------------------------------------------------------------------- rig

def rig(names):
    require_names(names, "rig")
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        glb = geometry_only(name)
        payload = {
            "model_url": "data:application/octet-stream;base64,"
                         + base64.b64encode(glb).decode(),
            "height_meters": 1.7,
        }
        texture = albedo_data_uri(name)
        if texture:
            payload["texture_image_url"] = texture
        result = call("POST", "/v1/rigging", payload)
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["rig"] = task
        print(f"{name:9s} {len(glb) / 1024:6.0f} KB geometry uploaded -> {task}")
    save(data)
    print(f"balance after:  {balance()}")


# ------------------------------------------------------------------- animate

def animate(names_and_moves):
    names = [n for n in names_and_moves if n in ROBOTS]
    moves = [m for m in names_and_moves if m in MOVES]
    unknown = [x for x in names_and_moves if x not in ROBOTS and x not in MOVES]
    if unknown:
        raise SystemExit(f"neither robot nor move: {', '.join(unknown)}")
    require_names(names, "animate")
    moves = moves or list(MOVES)

    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        entry = data.get(name, {})
        task = entry.get("rig")
        if not task:
            raise SystemExit(f"{name}: no rig task — run `rig {name}` first")
        if not entry.get("rig_urls"):
            raise SystemExit(f"{name}: rig not finished — run `status` until it is")
        for move in moves:
            result = call("POST", "/v1/animations", {
                "rig_task_id": task,
                "action_id": MOVES[move],
            })
            move_task = result.get("result") or result.get("id")
            entry.setdefault("moves", {})[move] = {"task": move_task}
            print(f"{name:9s} {move:9s} (action {MOVES[move]:3d}) -> {move_task}")
        data[name] = entry
    save(data)
    print(f"balance after:  {balance()}")


# --------------------------------------------------------------------- status

def status():
    data = state()
    for name, entry in sorted(data.items()):
        if entry.get("rig") and not entry.get("rig_urls"):
            info = call("GET", f"/v1/rigging/{entry['rig']}")
            line = f"{name:9s} rig       {info.get('status', '?'):10s} " \
                   f"{info.get('progress', 0):3d}%"
            urls = find_urls(info)
            if info.get("status") == "SUCCEEDED" and urls:
                entry["rig_urls"] = urls
                line += f"  {len(urls)} outputs"
            error = (info.get("task_error") or {}).get("message")
            if error:
                line += "  ERROR " + error
            print(line)
        elif entry.get("rig"):
            print(f"{name:9s} rig       SUCCEEDED   (cached)")
        for move, record in sorted(entry.get("moves", {}).items()):
            if record.get("urls"):
                print(f"{name:9s} {move:9s} SUCCEEDED   (cached)")
                continue
            info = call("GET", f"/v1/animations/{record['task']}")
            line = f"{name:9s} {move:9s} {info.get('status', '?'):10s} " \
                   f"{info.get('progress', 0):3d}%"
            urls = find_urls(info)
            if info.get("status") == "SUCCEEDED" and urls:
                record["urls"] = urls
                line += f"  {len(urls)} outputs"
            error = (info.get("task_error") or {}).get("message")
            if error:
                line += "  ERROR " + error
            print(line)
    save(data)


# ------------------------------------------------------------------- download

def pick_glb(urls):
    """The animated/rigged GLB out of a harvested URL map — .glb keys first."""
    for key, url in sorted(urls.items()):
        if "glb" in key.lower():
            return url
    return None


def download(names):
    require_names(names, "download")   # free, but the same explicitness
    data = state()
    os.makedirs(OUT_DIR, exist_ok=True)
    import urllib.request
    for name in names:
        entry = data.get(name, {})
        rigged = pick_glb(entry.get("rig_urls", {}))
        if rigged:
            path = f"{OUT_DIR}/{name}-rigged.glb"
            urllib.request.urlretrieve(rigged, path)
            print(f"{name:9s} rigged    {os.path.getsize(path) / 1024:7.0f} KB")
        for move, record in sorted(entry.get("moves", {}).items()):
            url = pick_glb(record.get("urls", {}))
            if not url:
                print(f"{name:9s} {move:9s} no output yet — run status")
                continue
            path = f"{OUT_DIR}/{name}-{move}.glb"
            urllib.request.urlretrieve(url, path)
            print(f"{name:9s} {move:9s} {os.path.getsize(path) / 1024:7.0f} KB")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    rest = sys.argv[2:]
    if command == "rig":
        rig(rest)
    elif command == "animate":
        animate(rest)
    elif command == "status":
        status()
    elif command == "download":
        download(rest)
    else:
        raise SystemExit(__doc__)

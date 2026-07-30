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
  python meshyfight.py analyze ranger              # find strike windows -> fight_trims.txt
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

def pick_glb(urls, want=""):
    """The animated GLB out of a harvested URL map.

    `want` narrows by key substring — needed on rig tasks, where a plain
    "first glb" match lands on running_armature_glb_url (armature-only, no
    mesh: the 59 KB mistake this parameter exists to prevent).
    """
    for key, url in sorted(urls.items()):
        key = key.lower()
        if "glb" in key and "armature" not in key and want in key:
            return url
    return None


def download(names):
    require_names(names, "download")   # free, but the same explicitness
    data = state()
    os.makedirs(OUT_DIR, exist_ok=True)
    import urllib.request
    for name in names:
        entry = data.get(name, {})
        rig_urls = entry.get("rig_urls", {})
        # The rigged character and its basic walk both ride the new skeleton;
        # the Brawl fighter prefab is forged from these, so the fight clips
        # never have to agree with the ORIGINAL rig (they provably don't:
        # ranger's re-rig moved Hips 10 cm and some joint frames 20°).
        for suffix, want in (("rigged", "rigged_character"), ("walking", "walking_glb")):
            url = pick_glb(rig_urls, want)
            if url:
                path = f"{OUT_DIR}/{name}-{suffix}.glb"
                urllib.request.urlretrieve(url, path)
                print(f"{name:9s} {suffix:9s} {os.path.getsize(path) / 1024:7.0f} KB")
        for move, record in sorted(entry.get("moves", {}).items()):
            url = pick_glb(record.get("urls", {}))
            if not url:
                print(f"{name:9s} {move:9s} no output yet — run status")
                continue
            path = f"{OUT_DIR}/{name}-{move}.glb"
            urllib.request.urlretrieve(url, path)
            print(f"{name:9s} {move:9s} {os.path.getsize(path) / 1024:7.0f} KB")


# -------------------------------------------------------------------- analyze
#
# Meshy's library clips are full routines (ranger's Kung Fu Punch runs 7.3 s)
# while a fighting-game strike needs ~half a second. The strike is findable
# without eyes: forward-kinematics through the GLB at 60 Hz, track the
# striking end-effector (fist for punches, foot for kicks), and the segment
# around its peak speed IS the strike. Windows land in fight_trims.txt, which
# BrawlMoveForge reads to cut the adopted clip.

EFFECTOR = {
    "punch": "RightHand", "highkick": "RightFoot", "kick": "RightFoot",
    "flykick": "RightFoot", "blast": "RightHand", "hit": "Head",
}


def _mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def _trs(t, q, s):
    x, y, z, w = q
    # Row-major rotation from a glTF [x,y,z,w] quaternion, scaled and placed.
    r = [
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ]
    return [
        [r[0][0] * s[0], r[0][1] * s[1], r[0][2] * s[2], t[0]],
        [r[1][0] * s[0], r[1][1] * s[1], r[1][2] * s[2], t[1]],
        [r[2][0] * s[0], r[2][1] * s[1], r[2][2] * s[2], t[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]


def _floats(gltf, buffer, accessor_index):
    import struct as _struct
    from meshyretexture import accessor_bytes
    data, acc = accessor_bytes(gltf, buffer, accessor_index)
    count = {"VEC4": 4, "VEC3": 3, "SCALAR": 1}[acc["type"]]
    return [list(_struct.unpack_from(f"<{count}f", data, i * count * 4))
            if count > 1 else _struct.unpack_from("<f", data, i * 4)[0]
            for i in range(acc["count"])]


def _interp(times, values, t):
    if t <= times[0]:
        return values[0]
    if t >= times[-1]:
        return values[-1]
    import bisect
    hi = bisect.bisect_right(times, t)
    lo = hi - 1
    span = times[hi] - times[lo]
    f = 0.0 if span <= 0 else (t - times[lo]) / span
    a, b = values[lo], values[hi]
    out = [ai + (bi - ai) * f for ai, bi in zip(a, b)]
    if len(out) == 4:   # rotation: normalized lerp is fine at key density
        # Antipodal pairs would wobble; flip to the near side first.
        if sum(x * y for x, y in zip(a, b)) < 0:
            out = [ai + (-bi - ai) * f for ai, bi in zip(a, b)]
        norm = max(1e-8, sum(x * x for x in out) ** 0.5)
        out = [x / norm for x in out]
    return out


def effector_track(path, effector_name, hz=60):
    """(times, world positions) of one named joint through the whole clip."""
    from meshyretexture import parse
    gltf, buffer = parse(path)
    nodes = gltf["nodes"]

    parent = {}
    for index, node in enumerate(nodes):
        for child in node.get("children", ()):
            parent[child] = index

    channels = {}
    anim = gltf["animations"][0]
    duration = 0.0
    for channel in anim["channels"]:
        sampler = anim["samplers"][channel["sampler"]]
        times = _floats(gltf, buffer, sampler["input"])
        times = [t if isinstance(t, float) else t[0] for t in times]
        values = _floats(gltf, buffer, sampler["output"])
        channels.setdefault(channel["target"]["node"], {})[channel["target"]["path"]] = (times, values)
        duration = max(duration, times[-1])

    target = next(i for i, n in enumerate(nodes) if n.get("name") == effector_name)
    chain = [target]
    while chain[-1] in parent:
        chain.append(parent[chain[-1]])
    chain.reverse()

    times_out, positions = [], []
    steps = int(duration * hz)
    for step in range(steps + 1):
        t = step / hz
        world = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
        for index in chain:
            node = nodes[index]
            tr = node.get("translation", [0, 0, 0])
            ro = node.get("rotation", [0, 0, 0, 1])
            sc = node.get("scale", [1, 1, 1])
            animated = channels.get(index, {})
            if "translation" in animated:
                tr = _interp(*animated["translation"], t)
            if "rotation" in animated:
                ro = _interp(*animated["rotation"], t)
            if "scale" in animated:
                sc = _interp(*animated["scale"], t)
            world = _mat_mul(world, _trs(tr, ro, sc))
        times_out.append(t)
        positions.append((world[0][3], world[1][3], world[2][3]))
    return times_out, positions


def strike_window(times, positions):
    """[start, end] around the effector's peak speed — the strike itself."""
    speeds = [0.0]
    for i in range(1, len(positions)):
        a, b = positions[i - 1], positions[i]
        dt = times[i] - times[i - 1]
        speeds.append(sum((bi - ai) ** 2 for ai, bi in zip(a, b)) ** 0.5 / max(dt, 1e-6))
    # Light smoothing so a single noisy sample can't claim the peak.
    smooth = [sum(speeds[max(0, i - 2):i + 3]) / len(speeds[max(0, i - 2):i + 3])
              for i in range(len(speeds))]

    peak = max(range(len(smooth)), key=lambda i: smooth[i])
    threshold = smooth[peak] * 0.30
    lo = peak
    while lo > 0 and smooth[lo] > threshold:
        lo -= 1
    hi = peak
    while hi < len(smooth) - 1 and smooth[hi] > threshold:
        hi += 1

    start, end = times[lo] - 0.10, times[hi] + 0.12
    # A window that swallowed the whole routine is a combo, not a strike:
    # keep the tight band around the peak instead.
    if end - start > 0.90:
        start, end = times[peak] - 0.28, times[peak] + 0.24
    if end - start < 0.35:
        pad = (0.35 - (end - start)) / 2
        start, end = start - pad, end + pad
    return max(0.0, start), min(times[-1], end), times[peak], smooth[peak]


def analyze(names):
    require_names(names, "analyze")   # free, but the same explicitness
    trim_path = f"{ROOT}/Tools/fight_trims.txt"
    existing = {}
    if os.path.exists(trim_path):
        with open(trim_path) as handle:
            for line in handle:
                parts = line.split()
                if len(parts) == 4 and not line.lstrip().startswith("#"):
                    existing[(parts[0], parts[1])] = line.rstrip()

    for name in names:
        for move, effector in EFFECTOR.items():
            path = f"{OUT_DIR}/{name}-{move}.glb"
            if not os.path.exists(path):
                continue
            times, positions = effector_track(path, effector)
            start, end, peak, speed = strike_window(times, positions)
            existing[(name, move)] = f"{name} {move} {start:.2f} {end:.2f}"
            print(f"{name:9s} {move:9s} clip {times[-1]:5.2f}s  "
                  f"peak {peak:5.2f}s ({speed:4.1f} m/s {effector})  "
                  f"-> trim [{start:.2f}, {end:.2f}]")

    with open(trim_path, "w") as handle:
        handle.write("# robot move start end — strike windows cut from Meshy\n"
                     "# library routines; written by meshyfight.py analyze,\n"
                     "# hand-tweak freely (the forge re-reads on next Play).\n")
        for key in sorted(existing):
            handle.write(existing[key] + "\n")
    print(f"wrote {trim_path}")


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
    elif command == "analyze":
        analyze(rest)
    else:
        raise SystemExit(__doc__)

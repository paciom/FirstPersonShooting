"""Generates the Commander mode's structure models.

Same two-stage discipline as meshyvehicles.py: `preview` buys geometry only
(~20 credits each) and the shape is FINAL at preview — so look at the
thumbnails before paying ~25 each to refine the survivors. Colours go in the
refine texture_prompt, which overrides whatever the base prompt asked for.

Each prompt is written against the block placeholder it replaces (see
Assets/Scripts/Commander/BuildingCatalog.cs): the silhouette must read as
that structure from a 55-degree RTS camera 45 m up, and the footprint
proportions should roughly match (command 6x6, power 4x4, refinery 6x4,
factory 6x6, turret 2x2, tech 4x4) so the runtime normalizer doesn't have
to stretch anything far.

  python meshybuildings.py preview [building ...]
  python meshybuildings.py status
  python meshybuildings.py thumbs <dir>
  python meshybuildings.py refine <building ...>
  python meshybuildings.py download <building ...>
"""
import json
import os
import sys
import urllib.error
import urllib.request

ROOT = "D:/Claude/FirstPersongShooting"
KEY_PATH = f"{ROOT}/.secrets/meshy_key.txt"
STATE_PATH = f"{ROOT}/Tools/building_tasks.json"
OUT_DIR = f"{ROOT}/Assets/Models/Meshy"
API = "https://api.meshy.ai/openapi"

STYLE = ("chunky stylised low-poly toy look, clean flat panels, glowing light strips, "
         "friendly sci-fi, floating in empty space, no base, no pedestal, no stand, no platform")
NEGATIVE = ("base, pedestal, stand, platform, ground plane, shadow plane, text, logo, "
            "human, people, vehicles, gore, realistic military, photorealistic")

# (shape prompt, refine colour prompt) per building key — keys match
# BuildingCatalog so the importer can pair model to definition by name.
BUILDINGS = {
    "command":  ("large sci-fi command center headquarters building, wide hexagonal core "
                 "with a raised central control tower, antenna mast and front hangar doors",
                 "dark blue-grey armour panels with bright cyan glowing window strips and trim"),
    "power":    ("compact sci-fi power plant building, squat round fusion core flanked by "
                 "two short cooling towers with visible energy coils",
                 "dark grey industrial panels with bright green glowing energy coils"),
    "refinery": ("sci-fi ore refinery building, rectangular processing hall with a cluster "
                 "of raised storage silos at one end and an open unloading dock at the other",
                 "dark grey-brown industrial panels with amber glowing vats and windows"),
    "factory":  ("sci-fi robot factory building, big rectangular assembly hall with a wide "
                 "front roller door, roof vents and a small overhead crane gantry",
                 "dark gunmetal panels with violet glowing door seams and windows"),
    "turret":   ("small sci-fi defence turret, low armoured round base supporting a "
                 "rotating twin-barrel laser cannon head",
                 "dark grey armour with red glowing cannon tips and hazard stripes"),
    "tech":     ("sci-fi research laboratory building, cubic core with a large observation "
                 "dome on the roof and two antenna dishes",
                 "white and light grey clean panels with pale blue glowing dome and windows"),
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


def preview(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        shape, _ = BUILDINGS[name]
        prompt = f"a single futuristic strategy game structure, {shape}, {STYLE}"
        result = call("POST", "/v2/text-to-3d", {
            "mode": "preview",
            "prompt": prompt[:800],
            "negative_prompt": NEGATIVE,
            # v2 accepts only "realistic"; the toy look comes from the prompt.
            "art_style": "realistic",
            "should_remesh": True,
            "symmetry_mode": "on",
            # Structures are set dressing seen from 45 m — vehicles' 6000 was
            # already generous, buildings need less.
            "target_polycount": 4000,
        })
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["preview"] = task
        print(f"{name:9s} preview {task}")
    save(data)
    print(f"balance after:  {balance()}")


def status():
    data = state()
    for name, tasks in sorted(data.items()):
        line = [f"{name:9s}"]
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


def refine(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        preview_task = data.get(name, {}).get("preview")
        if not preview_task:
            print(f"{name:9s} no preview to refine")
            continue
        _, colours = BUILDINGS[name]
        result = call("POST", "/v2/text-to-3d", {
            "mode": "refine",
            "preview_task_id": preview_task,
            "texture_prompt": f"{colours}, clean flat panels, glowing light strips, "
                              f"stylised toy finish",
        })
        task = result.get("result") or result.get("id")
        data[name]["refine"] = task
        print(f"{name:9s} refine {task}")
    save(data)
    print(f"balance after:  {balance()}")


def download(names):
    data = state()
    os.makedirs(OUT_DIR, exist_ok=True)
    for name in names:
        tasks = data.get(name, {})
        task = tasks.get("refine") or tasks.get("preview")
        info = call("GET", f"/v2/text-to-3d/{task}")
        if info["status"] != "SUCCEEDED":
            print(f"{name:9s} {info['status']} — not ready")
            continue
        url = info["model_urls"]["glb"]
        path = f"{OUT_DIR}/{name}-building.glb"
        urllib.request.urlretrieve(url, path)
        print(f"{name:9s} {os.path.getsize(path) / 1024:7.0f} KB  {path}")


def thumbs(out_dir):
    """Saves each building's newest thumbnail, for eyeballing before refine."""
    data = state()
    os.makedirs(out_dir, exist_ok=True)
    for name, tasks in sorted(data.items()):
        url = tasks.get("refine_thumb") or tasks.get("preview_thumb")
        if not url:
            print(f"{name:9s} no thumbnail yet — run status")
            continue
        path = os.path.join(out_dir, f"{name}.png")
        urllib.request.urlretrieve(url, path)
        print(f"{name:9s} {path}")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    names = sys.argv[2:] or list(BUILDINGS)
    if command == "preview":
        preview(names)
    elif command == "status":
        status()
    elif command == "refine":
        refine(names)
    elif command == "download":
        download(names)
    elif command == "thumbs":
        thumbs(sys.argv[2] if len(sys.argv) > 2 else f"{ROOT}/Tools/building_thumbs")
    else:
        raise SystemExit(f"unknown command {command}")

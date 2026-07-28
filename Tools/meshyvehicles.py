"""Generates the vehicle-form models for the transformation feature.

Two-stage on purpose. `preview` buys geometry only (~5 credits each) and refine
(~25) only textures the mesh it is given — so the shape is already final when
the preview thumbnail appears. Paying to texture geometry nobody has looked at
is how a previous run lost 35 credits, hence the split: preview everything,
look, then refine the survivors by name.

Colours live in the refine texture_prompt, not the base prompt. Refine's
texture_prompt overrides whatever the base prompt asked for, which is why
several of the robots ended up orange and teal regardless of what they were
asked to be.

  python meshyvehicles.py preview [robot ...]
  python meshyvehicles.py status
  python meshyvehicles.py refine <robot> [robot ...]
  python meshyvehicles.py download <robot> [robot ...]
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = "D:/Claude/FirstPersongShooting"
KEY_PATH = f"{ROOT}/.secrets/meshy_key.txt"
# Task ids are not secrets, and refine needs them days later — so they live in
# the repo rather than next to the key, where a cleanup would take them out.
STATE_PATH = f"{ROOT}/Tools/vehicle_tasks.json"
OUT_DIR = f"{ROOT}/Assets/Models/Meshy"
API = "https://api.meshy.ai/openapi"

STYLE = ("chunky stylised low-poly toy look, clean flat panels, glowing light strips, "
         "friendly sci-fi, floating in empty space, no base, no pedestal, no stand, no platform")
NEGATIVE = ("base, pedestal, stand, platform, ground plane, shadow plane, text, logo, "
            "human, driver, rider, gore, realistic military")

# Shapes chosen to echo each robot's build and palette — see Tools/rigtexture.py
# for where the palettes came from (the original prompts were never recorded).
VEHICLES = {
    "ranger":  ("sleek angular scout hovercraft with a pointed nose, twin side pods and "
                "a low canopy", "white and light grey armour with bright cyan light strips"),
    "titan":   ("heavy six-wheeled assault tank with thick layered armour plates and a "
                "short forward cannon", "deep blue armour with orange warning stripes"),
    "scout":   ("small fast open-frame recon buggy with exposed suspension and four "
                "chunky knobbly tyres", "sage grey-green panels with pale grey trim"),
    "hawk":    ("wedge-shaped low speeder with swept-back wing fins and twin rear "
                "thrusters", "orange shell with dark teal underside"),
    "bolt":    ("compact two-wheeled rail bike with a forward-leaning fairing and a "
                "single wide rear tyre", "steel blue bodywork with bright yellow flashes"),
    "samurai": ("armoured tracked crawler with an angular blade-like prow and shoulder "
                "plating", "turquoise lacquered plates with orange edging"),
    "panther": ("low sleek four-wheeled pursuit car with a long bonnet and haunched rear "
                "arches", "bright red bodywork with cyan light accents"),
    "knight":  ("heavy shielded battle wagon with a broad front ram plate and side "
                "skirts", "matte black armour with orange glowing seams"),
    "racer":   ("open-wheel formula racer with a long nose cone, side pods and a rear "
                "wing", "white bodywork with red racing stripes"),
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


# --------------------------------------------------------------------- preview

def preview(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        shape, _ = VEHICLES[name]
        prompt = f"a futuristic ground battle vehicle, {shape}, {STYLE}"
        result = call("POST", "/v2/text-to-3d", {
            "mode": "preview",
            "prompt": prompt[:800],
            "negative_prompt": NEGATIVE,
            # v2 accepts only "realistic"; the toy look comes from the prompt.
            "art_style": "realistic",
            "should_remesh": True,
            "symmetry_mode": "on",
            "target_polycount": 6000,
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


# ---------------------------------------------------------------------- refine

def refine(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        preview_task = data.get(name, {}).get("preview")
        if not preview_task:
            print(f"{name:9s} no preview to refine")
            continue
        _, colours = VEHICLES[name]
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


# -------------------------------------------------------------------- download

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
        path = f"{OUT_DIR}/{name}-vehicle.glb"
        urllib.request.urlretrieve(url, path)
        print(f"{name:9s} {os.path.getsize(path) / 1024:7.0f} KB  {path}")


def thumbs(out_dir):
    """Saves the newest thumbnail for each robot, for eyeballing before refine."""
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
    rest = sys.argv[2:]
    if command == "preview":
        preview(rest or list(VEHICLES))
    elif command == "status":
        status()
    elif command == "refine":
        refine(rest)
    elif command == "download":
        download(rest or list(VEHICLES))
    elif command == "thumbs":
        thumbs(rest[0])
    else:
        raise SystemExit(__doc__)

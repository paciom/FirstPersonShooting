"""Turns transformation-video frames into 3D models via Meshy Image to 3D.

The experiment: the in-game robot->vehicle fold reads as a magic trick because
the folded robot is nowhere near the vehicle silhouette. So instead of folding a
rig, generate an actual Transformers-style transformation as VIDEO, sample it,
and rebuild each sampled pose as its own 3D model. Played back in sequence those
models are a stop-motion transformation -- crude, but it answers whether the
intermediate silhouettes are believable before anyone rigs anything.

Frames come from Tools/... no, from ffmpeg (see TransformerTest/02_frames). This
module only handles the Meshy side: submit, poll, download.

30 credits per image. Frames are uploaded as base64 data URIs, so nothing needs
to be publicly hosted.

  python meshyimageto3d.py submit  TransformerTest/02_frames/spread/*.png
  python meshyimageto3d.py status
  python meshyimageto3d.py download
"""
import base64
import glob
import json
import os
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meshyvehicles import balance, call  # noqa: E402

ROOT = "D:/Claude/FirstPersongShooting"
STATE_PATH = f"{ROOT}/TransformerTest/tasks.json"
OUT_DIR = f"{ROOT}/TransformerTest/03_models"

# Kept deliberately plain. The frame already shows the pose, the silhouette and
# the palette; over-describing it invites the generator to reinterpret rather
# than reproduce, and reproduction is the whole point of the test.
TEXTURE_PROMPT = ("white and light grey armour panels with bright cyan light "
                  "strips, clean flat panels, stylised toy finish")


def state():
    if not os.path.exists(STATE_PATH):
        return {}
    with open(STATE_PATH) as handle:
        return json.load(handle)


def save(data):
    os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
    with open(STATE_PATH, "w") as handle:
        json.dump(data, handle, indent=2)


def submit(paths):
    data = state()
    print(f"balance before: {balance()}")
    for path in paths:
        name = os.path.splitext(os.path.basename(path))[0]
        with open(path, "rb") as handle:
            encoded = base64.b64encode(handle.read()).decode()
        result = call("POST", "/v1/image-to-3d", {
            "image_url": f"data:image/png;base64,{encoded}",
            "ai_model": "meshy-6",
            "should_texture": True,
            "texture_prompt": TEXTURE_PROMPT[:600],
            "should_remesh": True,
            "topology": "triangle",
            "target_polycount": 30000,
        })
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["task"] = task
        print(f"{name:16s} {os.path.getsize(path)/1024:6.0f} KB -> {task}")
    save(data)
    print(f"balance after:  {balance()}")


def status():
    data = state()
    for name, entry in sorted(data.items()):
        info = call("GET", f"/v1/image-to-3d/{entry['task']}")
        line = f"{name:16s} {info['status']:10s} {info.get('progress', 0):3d}%"
        if info.get("model_urls"):
            entry["models"] = info["model_urls"]
            line += "  glb ready"
        error = (info.get("task_error") or {}).get("message")
        if error:
            line += "  ERROR " + error
        print(line)
    save(data)


def download():
    data = state()
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, entry in sorted(data.items()):
        url = (entry.get("models") or {}).get("glb")
        if not url:
            print(f"{name:16s} not ready -- run status")
            continue
        path = f"{OUT_DIR}/{name}.glb"
        urllib.request.urlretrieve(url, path)
        print(f"{name:16s} {os.path.getsize(path)/1024:8.0f} KB  {path}")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    rest = [p for arg in sys.argv[2:] for p in (glob.glob(arg) or [arg])]
    if command == "submit":
        submit(rest)
    elif command == "status":
        status()
    elif command == "download":
        download()
    else:
        raise SystemExit(__doc__)

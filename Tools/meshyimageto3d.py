"""Turns transformation-video frames into 3D models via Meshy Image to 3D.

The in-game fold could only ever crouch a humanoid, because a humanoid mesh
contains no tracks, no hull and no turret to unfold. So the transformation is
generated as VIDEO instead, sampled at chosen frames, and each frame rebuilt as
its own 3D model. Played in sequence those models are a stop-motion
transformation — robot at one end, the real vehicle at the other.

Frames are cropped free of the generator's UI overlay before submission; a
watermark or a "DRAG TO ROTATE" caption is geometry as far as image-to-3D is
concerned. See the frame-extraction commands in the commit that added each robot.

30 credits per image. Frames upload as base64 data URIs, so nothing needs
public hosting.

`download` writes straight to Assets/Models/Stages/<robot>/stageN.glb in frame
order, which is where ArenaBuilder.LoadTransformStages expects them — so
zero-pad frame numbers in the filenames and a plain sort is the right order.

  python meshyimageto3d.py bolt submit TransformerTest/02_frames/bolt/*.png
  python meshyimageto3d.py bolt status
  python meshyimageto3d.py bolt download

Ranger predates this being robot-aware; its run state is the legacy
TransformerTest/tasks.json rather than a per-robot file.
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
STAGES = f"{ROOT}/Assets/Models/Stages"

# Kept deliberately plain, and per robot. The frame already shows the pose, the
# silhouette and the palette; over-describing it invites the generator to
# reinterpret rather than reproduce, and reproduction is the whole point.
TEXTURE_PROMPTS = {
    "ranger": ("white and light grey armour panels with bright cyan light "
               "strips, clean flat panels, stylised toy finish"),
    "bolt": ("navy blue armour panels with bright yellow accents and white "
             "trim, cyan light strips, clean flat panels, stylised toy finish"),
}

LEGACY_STATE = {"ranger": f"{ROOT}/TransformerTest/tasks.json"}


def state_path(robot):
    return LEGACY_STATE.get(robot, f"{ROOT}/TransformerTest/{robot}/tasks.json")


def state(robot):
    path = state_path(robot)
    if not os.path.exists(path):
        return {}
    with open(path) as handle:
        return json.load(handle)


def save(robot, data):
    path = state_path(robot)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as handle:
        json.dump(data, handle, indent=2)


def submit(robot, paths):
    if robot not in TEXTURE_PROMPTS:
        raise SystemExit(f"No texture prompt for '{robot}' — add one to TEXTURE_PROMPTS "
                         "so the stages come back in that robot's own colours.")
    data = state(robot)
    print(f"balance before: {balance()}")
    for path in paths:
        name = os.path.splitext(os.path.basename(path))[0]
        with open(path, "rb") as handle:
            encoded = base64.b64encode(handle.read()).decode()
        result = call("POST", "/v1/image-to-3d", {
            "image_url": f"data:image/png;base64,{encoded}",
            "ai_model": "meshy-6",
            "should_texture": True,
            "texture_prompt": TEXTURE_PROMPTS[robot][:600],
            "should_remesh": True,
            "topology": "triangle",
            "target_polycount": 30000,
        })
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["task"] = task
        print(f"{name:16s} {os.path.getsize(path)/1024:6.0f} KB -> {task}")
    save(robot, data)
    print(f"balance after:  {balance()}")


def status(robot):
    data = state(robot)
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
    save(robot, data)


def download(robot):
    """Writes stage1..stageN in frame order, ready for the roster scan."""
    data = state(robot)
    out_dir = f"{STAGES}/{robot}"
    os.makedirs(out_dir, exist_ok=True)

    ready = [(name, entry) for name, entry in sorted(data.items())
             if (entry.get("models") or {}).get("glb")]
    missing = len(data) - len(ready)
    if missing:
        print(f"{missing} stage(s) not ready — run status first; "
              "numbering now would leave gaps in the sequence.")
        return

    for index, (name, entry) in enumerate(ready, start=1):
        path = f"{out_dir}/stage{index}.glb"
        urllib.request.urlretrieve(entry["models"]["glb"], path)
        print(f"stage{index:<2d} <- {name:16s} {os.path.getsize(path)/1024:8.0f} KB  {path}")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    robot, command = sys.argv[1], sys.argv[2]
    rest = [p for arg in sys.argv[3:] for p in (glob.glob(arg) or [arg])]
    if command == "submit":
        submit(robot, rest)
    elif command == "status":
        status(robot)
    elif command == "download":
        download(robot)
    else:
        raise SystemExit(__doc__)

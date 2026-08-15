#!/usr/bin/env python3
"""Build a beat's 30-second video from its storyboard.

    python Tools/adventure_video.py n01                # stills + clips + cut
    python Tools/adventure_video.py n01 n02 n03
    python Tools/adventure_video.py n01 --stills-only  # cheap: just the frames
    python Tools/adventure_video.py n01 --cut-only     # re-cut existing clips
    python Tools/adventure_video.py n01 --dry          # print the plan, spend nothing

A beat is already a 4-6 shot storyboard whose durations sum to exactly 30
seconds, so the video is not one generation — it is one clip per shot, cut
together. Each shot goes:

  1. a still, from gpt-image-1 with the beat's cast as reference images, framed
     from that shot's own camera and action (the beat's key shot reuses the
     still already in Resources rather than paying for it twice);
  2. that still pinned as the FIRST FRAME of a Seedance image-to-video job, so
     the clip starts exactly on the frame we approved and the robots keep their
     real designs — the same trick the ExternalData transformation clips use;
  3. ffmpeg trims it to the shot's exact seconds and concatenates.

Seedance 2.5 is the activated video model on this account (its image sibling
is not), and it is what made ExternalData/*_Transformation.mp4, so matching
that look is a matter of pinning the first frame and asking for the same clean
low-poly cel-shaded rendering.
"""

import argparse
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from adventure_onepass import (ADVENTURES, CAST_REFS, STORY, cast_in,   # noqa: E402
                               clean_setting, generate_openai, key_shot,
                               secret, story_nodes, crop_16x9)

SHOTS_DIR = os.path.join(ROOT, "Tools", "adventure_shotframes")
CLIPS_DIR = os.path.join(ROOT, "Tools", "adventure_clips")
OUT_DIR = os.path.join(ROOT, "Renders", "adventure")
HOST = "https://ark.ap-southeast.bytepluses.com"
CREATE_URL = HOST + "/api/v3/contents/generations/tasks"
QUERY_URL = HOST + "/api/v3/contents/generations/tasks/{}"
MODEL = "dreamina-seedance-2-5-260628"
POLL_SECONDS = 15
POLL_LIMIT = 80

# Seedance takes whole seconds; these are the lengths it is known to accept.
ALLOWED = (4, 5, 6, 8, 10, 12)

# The camera word in a shot is a framing, a move, or both. Only the moves need
# telling to the video model — everything else it should hold still, because
# an unasked-for drift is the fastest way to lose the pinned first frame.
MOVES = {
    "TRACKING": "[Tracking shot] the camera tracks smoothly with the movement",
    "VERTICAL TRACKING": "[Camera falls with him] the camera descends alongside the fall",
    "TRACKING DOWN": "[Camera lowers] the camera sinks slowly toward the ground",
    "CRANE UP": "[Crane up] the camera rises steadily",
    "CRANE DOWN": "[Crane down] the camera descends steadily",
    "WHIP-PAN": "[Whip pan] the camera whips quickly to the side",
    "TOP-DOWN": "[Static overhead shot] the camera looks straight down and does not move",
    "HOLD": "[Static shot] the camera is locked off and does not move, pan or zoom",
}
STILL_STYLE = ("Stylized low-poly cel-shaded 3D animation, flat saturated "
               "colours, clean chunky shapes, glowing cyan energy panels, "
               "dark rainy night palette")

# A shot's line often refers to somebody it never names — "the empty hand" is
# Titan's — so the frame comes back with the wrong robot in it, or the same
# robot twice. These say who is actually in the frame.
SHOT_CAST = {
    ("n01", 2): "Titan and Panther",
    ("n01", 3): "Titan",
    ("n02", 1): "Panther",
    ("n03", 1): "Panther",
}


def camera_note(cam):
    for word, note in MOVES.items():
        if word in cam:
            return note
    return "[Static shot] the camera is locked off and does not move, pan or zoom"


def nearest_allowed(seconds):
    return min(ALLOWED, key=lambda a: (abs(a - seconds), a))


def still_path(node_id, index):
    return os.path.join(SHOTS_DIR, f"{node_id}_s{index}.png")


def clip_path(node_id, index):
    return os.path.join(CLIPS_DIR, f"{node_id}_s{index}.mp4")


def shot_prompt(node, shot):
    """What the clip should DO. The look is carried by the pinned first frame,
    so this describes motion and nothing else."""
    return (f"{camera_note(shot['cam'])}. {shot['action']} "
            "Keep the exact character designs, colours, materials and lighting "
            "of the first frame — same robots, same plating, same glowing "
            f"panels. {STILL_STYLE}. Continuous natural motion, no cuts, no "
            "text, no captions, no interface, no watermark.")


def ensure_still(node, index, shot, key_index, force=False):
    """The frame the clip opens on. The beat's key shot already has an approved
    still in Resources; the rest are generated the same way it was."""
    path = still_path(node["id"], index)
    if os.path.exists(path) and not force:
        return path
    os.makedirs(SHOTS_DIR, exist_ok=True)
    if index == key_index:
        approved = os.path.join(ADVENTURES, STORY, node["id"] + ".png")
        if os.path.exists(approved):
            with open(approved, "rb") as src, open(path, "wb") as dst:
                dst.write(src.read())
            return path

    # Same recipe as the beat's own still, re-framed for this shot.
    key = secret("openai_key.txt")
    if not key:
        sys.exit("no OpenAI key: put one in .secrets/openai_key.txt")
    named = SHOT_CAST.get((node["id"], index))
    framed = dict(shot, key=True)
    if named:
        framed["action"] = f"In frame: {named}. " + shot["action"]
    stand_in = dict(node, shots=[framed])
    stand_in["id"] = node["id"]
    raw, _ = generate_openai(stand_in, key)
    with open(path, "wb") as handle:
        handle.write(crop_16x9(raw))
    return path


def data_uri(path):
    with open(path, "rb") as handle:
        return "data:image/png;base64," + base64.b64encode(handle.read()).decode()


def ark(url, key, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    request = urllib.request.Request(url, data=data, headers={
        "Authorization": "Bearer " + key, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"HTTP {error.code}: {error.read().decode(errors='replace')[:400]}")


def generate_clip(node, index, shot, first_frame, key, force=False):
    path = clip_path(node["id"], index)
    if os.path.exists(path) and not force:
        return path, "skip"
    os.makedirs(CLIPS_DIR, exist_ok=True)
    content = [{"type": "text", "text": shot_prompt(node, shot)},
               {"type": "image_url", "role": "first_frame",
                "image_url": {"url": data_uri(first_frame)}}]
    # No "ratio" here on purpose: with a pinned first frame the API rejects it
    # outright ("the output ratio follows the first-frame image"), and the
    # frames are already 16:9.
    payload = {"model": MODEL, "content": content,
               "duration": nearest_allowed(shot["t"]), "resolution": "720p",
               "generate_audio": False, "watermark": False}
    task = ark(CREATE_URL, key, payload).get("id")
    if not task:
        raise RuntimeError("no task id")
    for attempt in range(POLL_LIMIT):
        reply = ark(QUERY_URL.format(task), key)
        status = (reply.get("status") or "").lower()
        if status == "succeeded":
            url = (reply.get("content") or {}).get("video_url")
            with urllib.request.urlopen(url, timeout=600) as src, open(path, "wb") as dst:
                dst.write(src.read())
            with open(path + ".json", "w", encoding="utf-8") as handle:
                json.dump({"model": MODEL, "task_id": task, "shot": shot,
                           "first_frame": first_frame}, handle, indent=2)
            return path, "ok"
        if status in ("failed", "cancelled"):
            raise RuntimeError("generation " + status + ": " + json.dumps(reply)[:300])
        time.sleep(POLL_SECONDS)
    raise RuntimeError("timed out")


def cut(node):
    """Trim each clip to its shot's exact seconds and join them. The beat is
    thirty seconds because the storyboard says thirty seconds; the model's
    nearest-allowed duration is not allowed to change that."""
    os.makedirs(OUT_DIR, exist_ok=True)
    parts, listing = [], os.path.join(CLIPS_DIR, node["id"] + "_list.txt")
    for index, shot in enumerate(node["shots"], start=1):
        source = clip_path(node["id"], index)
        if not os.path.exists(source):
            return None, f"missing clip {index}"
        trimmed = os.path.join(CLIPS_DIR, f"{node['id']}_s{index}_cut.mp4")
        # Seedance only makes certain lengths, so a 7-second shot comes back as
        # a 6-second clip and the beat quietly finishes a second early. Stretch
        # the clip to the length the storyboard asked for rather than letting
        # thirty seconds become twenty-eight; a hold gets a freeze instead,
        # because slowing a locked-off shot does nothing anyway.
        have = float(subprocess.run(
            ["ffprobe", "-v", "error", "-show_entries", "format=duration",
             "-of", "csv=p=0", source],
            capture_output=True, text=True, check=True).stdout.strip())
        want = float(shot["t"])
        scale = ("scale=1280:720:force_original_aspect_ratio=decrease,"
                 "pad=1280:720:(ow-iw)/2:(oh-ih)/2")
        if have < want - 0.05:
            if (want / have) <= 1.3:
                video = f"setpts={want / have:.4f}*PTS,{scale},fps=24"
            else:
                video = f"{scale},tpad=stop_mode=clone:stop_duration={want - have:.2f},fps=24"
        else:
            video = f"{scale},fps=24"
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                        "-i", source, "-vf", video, "-t", str(shot["t"]),
                        "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                        trimmed], check=True)
        parts.append(trimmed)
    with open(listing, "w", encoding="utf-8") as handle:
        for part in parts:
            handle.write("file '%s'\n" % part.replace("\\", "/"))
    out = os.path.join(OUT_DIR, node["id"] + ".mp4")
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "concat", "-safe", "0", "-i", listing,
                    "-c", "copy", out], check=True)
    return out, "ok"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("nodes", nargs="+")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--stills-only", action="store_true")
    parser.add_argument("--cut-only", action="store_true")
    parser.add_argument("--dry", action="store_true")
    args = parser.parse_args()

    story = story_nodes()
    for nid in args.nodes:
        if nid not in story:
            sys.exit("unknown beat: " + nid)

    for nid in args.nodes:
        node = story[nid]
        shots = node["shots"]
        key_index = shots.index(key_shot(node)) + 1
        total = sum(s["t"] for s in shots)
        print(f"\n{nid}: {len(shots)} shots, {total}s, cast "
              f"{', '.join(cast_in(node)) or 'none'}")
        if args.dry:
            for i, shot in enumerate(shots, start=1):
                print(f"  s{i} {shot['t']}s -> {nearest_allowed(shot['t'])}s "
                      f"{'(key)' if i == key_index else '     '} {shot['cam']}")
                print(f"     {shot_prompt(node, shot)[:150]}...")
            continue

        if args.cut_only:
            print("  cut:", cut(node))
            continue

        frames = []
        for i, shot in enumerate(shots, start=1):
            frames.append(ensure_still(node, i, shot, key_index, force=args.force))
            print(f"  still s{i}  {os.path.basename(frames[-1])}")
        if args.stills_only:
            continue

        key = None
        for line in open(os.path.join(ROOT, ".secrets", "byteplus.env"), encoding="utf-8"):
            if line.startswith("ARK_API_KEY="):
                key = line.strip().split("=", 1)[1]
        results = [None] * len(shots)

        def run(i):
            shot = shots[i - 1]
            try:
                results[i - 1] = generate_clip(node, i, shot, frames[i - 1], key,
                                               force=args.force)
                print(f"  clip  s{i}  {results[i-1][1]}", flush=True)
            except Exception as error:
                print(f"  clip  s{i}  FAIL {error}", flush=True)

        with ThreadPoolExecutor(max_workers=3) as pool:
            list(pool.map(run, range(1, len(shots) + 1)))
        if all(results):
            print("  cut:", cut(node))


if __name__ == "__main__":
    main()

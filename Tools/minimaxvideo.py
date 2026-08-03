"""Drives a MiniMax image-to-video job from a still and a prompt.

Exists because the transformation clips are seeded by a render we control
(PreviewCaptureTool's *_hero.png) rather than by a painted robot, and the round
trip — submit, poll, fetch the file, download — is long enough that doing it by
hand loses track of which prompt produced which mp4. Every run writes its
request next to the video so a clip can be traced back to what asked for it.

  python minimaxvideo.py seed.png out.mp4 --prompt-file p.txt --seconds 4

MODEL AND DURATION. MiniMax-H3 is the only model that accepts an arbitrary
4-15s duration, but it is not enabled on this account ("TokenPlan or Credit
does not currently support MiniMax-H3 series models"). Hailuo-02 is, and it
generates 6s or 10s only. So a 4-second deliverable is generated at 6s and
retimed on the way out rather than trimmed: trimming would cut the
transformation off part-finished, retiming keeps the whole beat and just plays
it faster. --seconds is the length actually wanted; GEN_SECONDS is what is
asked of the API.

RESOLUTION. 512P is the smallest Hailuo-02 offers and it is only accepted when
first_frame_image is present. A 480px deliverable is that 512P output scaled
down, which is free quality.
"""
import argparse
import base64
import json
import mimetypes
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

CREATE_URL = "https://api.minimax.io/v1/video_generation"
QUERY_URL = "https://api.minimax.io/v1/query/video_generation?task_id={}"
FILE_URL = "https://api.minimax.io/v1/files/retrieve?file_id={}"
# Hailuo-02 takes duration and resolution but ignores "the camera does not
# move" however firmly the prompt puts it — it re-composes on the second frame
# and zooms. I2V-01-Director exists for that: it reads bracketed camera
# commands like [Static shot] and holds them. It has no size knobs (fixed 6s
# 720p), which is why the two models carry different parameter sets here.
MODELS = {
    "hailuo": {"name": "MiniMax-Hailuo-02", "duration": 6, "resolution": "512P"},
    "director": {"name": "I2V-01-Director"},
}
POLL_SECONDS = 10
POLL_LIMIT = 180


def call(url, key, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, headers={
        "Authorization": "Bearer " + key,
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=180) as response:
            reply = json.loads(response.read())
    except urllib.error.HTTPError as error:
        # MiniMax puts the real reason in the body, not the status line, so a
        # bare "HTTP 400" would throw away the only useful part.
        sys.exit(f"HTTP {error.code}: {error.read().decode(errors='replace')}")
    # Request-level failures come back inside a 200, so they have to be checked
    # separately or a broken run looks like a successful one with no video.
    base = reply.get("base_resp") or {}
    if base.get("status_code") not in (0, None):
        sys.exit(f"MiniMax {base['status_code']}: {base.get('status_msg')}")
    return reply


def data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "image/png"
    with open(path, "rb") as handle:
        return f"data:{mime};base64," + base64.b64encode(handle.read()).decode()


def create(seed, prompt, spec, key):
    payload = {
        "model": spec["name"],
        "prompt": prompt,
        "first_frame_image": data_uri(seed),
        # The optimizer rewrites the prompt, and this one is mostly a list of
        # things the camera must NOT do — exactly the wording it tends to drop,
        # along with the bracketed camera commands Director depends on.
        "prompt_optimizer": False,
    }
    if "duration" in spec:
        payload["duration"] = spec["duration"]
    if "resolution" in spec:
        payload["resolution"] = spec["resolution"]
    reply = call(CREATE_URL, key, payload)
    task = reply.get("task_id")
    if not task:
        sys.exit("no task id: " + json.dumps(reply)[:800])
    return task


def wait(task, key):
    for attempt in range(POLL_LIMIT):
        reply = call(QUERY_URL.format(task), key)
        status = (reply.get("status") or "").lower()
        print(f"  [{attempt * POLL_SECONDS:4d}s] {status or '?'}", flush=True)
        if status in ("success", "succeeded"):
            file_id = reply.get("file_id")
            if not file_id:
                sys.exit("succeeded with no file_id: " + json.dumps(reply)[:800])
            return file_id
        if status in ("fail", "failed"):
            sys.exit("generation failed: " + json.dumps(reply)[:800])
        time.sleep(POLL_SECONDS)
    sys.exit("timed out waiting for the task")


def download(file_id, key, path):
    reply = call(FILE_URL.format(file_id), key)
    url = (reply.get("file") or {}).get("download_url")
    if not url:
        sys.exit("no download url: " + json.dumps(reply)[:800])
    with urllib.request.urlopen(url, timeout=600) as response, \
            open(path, "wb") as handle:
        handle.write(response.read())
    return url


def retime(src, dst, seconds, height):
    """Rescales the clip to `seconds` and `height`, re-encoding once."""
    probe = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
         "stream=duration", "-of", "csv=p=0", src],
        capture_output=True, text=True, check=True)
    actual = float(probe.stdout.strip().split(",")[0])
    factor = seconds / actual
    filters = [f"setpts={factor:.6f}*PTS"]
    if height:
        # -2 keeps the width even, which H.264 requires; scaling by height
        # alone would give an odd width on a non-square frame and fail.
        filters.append(f"scale=-2:{height}:flags=lanczos")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-an", "-i", src,
                    "-vf", ",".join(filters), "-r", "25",
                    "-c:v", "libx264", "-crf", "18", "-pix_fmt", "yuv420p",
                    dst], check=True)
    print(f"retimed {actual:.2f}s -> {seconds}s ({factor:.3f}x)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("seed")
    parser.add_argument("out")
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--model", choices=sorted(MODELS), default="hailuo")
    parser.add_argument("--seconds", type=float, default=0,
                        help="retime the result to this length (0 = keep)")
    parser.add_argument("--height", type=int, default=0,
                        help="scale the result to this height (0 = keep)")
    args = parser.parse_args()

    key = os.environ.get("MINIMAX_API_KEY")
    if not key:
        sys.exit("MINIMAX_API_KEY is not set")
    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()

    spec = MODELS[args.model]
    print(f"seed   {args.seed}\nmodel  {spec['name']} "
          f"{spec.get('duration', 6)}s {spec.get('resolution', '720P')}")
    task = create(args.seed, prompt, spec, key)
    print("task  ", task, flush=True)
    file_id = wait(task, key)

    raw = args.out.replace(".mp4", "_raw.mp4") \
        if (args.seconds or args.height) else args.out
    url = download(file_id, key, raw)
    print("wrote ", raw)
    if args.seconds or args.height:
        retime(raw, args.out, args.seconds or 0, args.height)
        print("wrote ", args.out)

    with open(args.out + ".json", "w", encoding="utf-8") as handle:
        json.dump({"model": spec["name"], "model_spec": spec,
                   "final_seconds": args.seconds, "final_height": args.height,
                   "seed": os.path.abspath(args.seed), "task_id": task,
                   "file_id": file_id, "source_url": url,
                   "prompt": prompt}, handle, indent=2)


if __name__ == "__main__":
    main()

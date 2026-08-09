"""Drives a MiniMax-H3 reference-to-video job (v2 multimodal API).

The v1 script (minimaxvideo.py) speaks Hailuo-02's first-frame API; H3 lives
on a different endpoint with a content-array request: text plus up to 9
reference images that the prompt cites as "Image 1", "Image 2", ... in order.
Images are inlined as base64 data URIs (request body limit is 64 MB).

  python minimaxh3.py out.mp4 --prompt-file p.txt --ref a.png --ref b.png \
      --seconds 15 --resolution 2K --ratio 9:16

Duration is 4-15s integer, resolution 768P or 2K, ratio 9:16/16:9/1:1/4:3/
3:4/adaptive. Every run writes its request (minus the base64 blobs) next to
the video so a clip traces back to what asked for it.
"""
import argparse
import base64
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request

CREATE_URL = "https://api.minimax.io/v2/video_generation"
QUERY_URL = "https://api.minimax.io/v2/query/video_generation/{}"
POLL_SECONDS = 15
POLL_LIMIT = 160  # 40 min; 15s 2K jobs are slow


def call(url, key, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, headers={
        "Authorization": "Bearer " + key,
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=300) as response:
            reply = json.loads(response.read())
    except urllib.error.HTTPError as error:
        # MiniMax puts the real reason in the body, not the status line.
        sys.exit(f"HTTP {error.code}: {error.read().decode(errors='replace')}")
    base = reply.get("base_resp") or {}
    if base.get("status_code") not in (0, None):
        sys.exit(f"MiniMax {base['status_code']}: {base.get('status_msg')}")
    return reply


def data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "image/png"
    with open(path, "rb") as handle:
        return f"data:{mime};base64," + base64.b64encode(handle.read()).decode()


def find(reply, *keys):
    """Digs `keys` out of the reply whether or not it is nested under task."""
    for scope in (reply, reply.get("task") or {}):
        node = scope
        for key in keys:
            node = (node or {}).get(key) if isinstance(node, dict) else None
        if node:
            return node
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--ref", action="append", default=[],
                        help="reference image, cited as Image N in prompt order")
    parser.add_argument("--seconds", type=int, default=15)
    parser.add_argument("--resolution", choices=["768P", "2K"], default="2K")
    parser.add_argument("--ratio", default="9:16")
    args = parser.parse_args()

    key = os.environ.get("MINIMAX_API_KEY")
    if not key:
        sys.exit("MINIMAX_API_KEY is not set")
    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()

    content = [{"type": "text", "text": prompt}]
    for ref in args.ref:
        content.append({"type": "image_url", "role": "reference_image",
                        "image_url": {"url": data_uri(ref)}})

    payload = {"model": "MiniMax-H3", "content": content,
               "duration": args.seconds, "resolution": args.resolution,
               "ratio": args.ratio}
    print(f"model  MiniMax-H3 {args.seconds}s {args.resolution} {args.ratio}\n"
          f"refs   {', '.join(args.ref)}", flush=True)
    reply = call(CREATE_URL, key, payload)
    task = reply.get("task_id") or find(reply, "task_id")
    if not task:
        sys.exit("no task id: " + json.dumps(reply)[:800])
    print("task  ", task, flush=True)

    url = None
    for attempt in range(POLL_LIMIT):
        reply = call(QUERY_URL.format(task), key)
        status = (find(reply, "status") or "").lower()
        print(f"  [{attempt * POLL_SECONDS:4d}s] {status or '?'}", flush=True)
        if status in ("success", "succeeded"):
            url = (find(reply, "content", "url")
                   or find(reply, "video_url")
                   or (find(reply, "file", "download_url")))
            if not url:
                sys.exit("succeeded with no url: " + json.dumps(reply)[:1500])
            break
        if status in ("fail", "failed", "cancelled"):
            sys.exit("generation failed: " + json.dumps(reply)[:1500])
        time.sleep(POLL_SECONDS)
    if not url:
        sys.exit("timed out waiting for the task")

    with urllib.request.urlopen(url, timeout=600) as response, \
            open(args.out, "wb") as handle:
        handle.write(response.read())
    print("wrote ", args.out)

    with open(args.out + ".json", "w", encoding="utf-8") as handle:
        json.dump({"model": "MiniMax-H3", "duration": args.seconds,
                   "resolution": args.resolution, "ratio": args.ratio,
                   "refs": [os.path.abspath(r) for r in args.ref],
                   "task_id": task, "source_url": url, "prompt": prompt},
                  handle, indent=2)


if __name__ == "__main__":
    main()

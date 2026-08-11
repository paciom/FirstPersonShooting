"""Drives a BytePlus ModelArk (Seedance) reference-to-video job.

Same shape as minimaxh3.py — content array of text plus reference images,
submit, poll, download, request JSON written next to the video. ModelArk's
REST surface:

  POST {host}/api/v3/contents/generations/tasks   -> {"id": ...}
  GET  {host}/api/v3/contents/generations/tasks/{id}
        -> status queued|running|succeeded|failed, content.video_url

  python seedance.py out.mp4 --prompt-file p.txt --ref a.png --ref b.png \
      --seconds 30 --resolution 720p --ratio 9:16

Auth is ARK_API_KEY. The model id is a dated string that only the ModelArk
console knows for sure; MODELS is a ladder tried in order (2.5 launched
2026-08-07 and its public id is unconfirmed, 2.0's is documented), so an
unknown-model rejection falls through to the next id instead of dying.
Pass --model to pin the id shown in your console.
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

HOST = "https://ark.ap-southeast.bytepluses.com"
CREATE_URL = HOST + "/api/v3/contents/generations/tasks"
QUERY_URL = HOST + "/api/v3/contents/generations/tasks/{}"
MODELS = ["dreamina-seedance-2-5-260628", "dreamina-seedance-2-0-260128"]
POLL_SECONDS = 15
POLL_LIMIT = 160


def call(url, key, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, headers={
        "Authorization": "Bearer " + key,
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=300) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise RuntimeError(
            f"HTTP {error.code}: {error.read().decode(errors='replace')}")


def data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "image/png"
    with open(path, "rb") as handle:
        return f"data:{mime};base64," + base64.b64encode(handle.read()).decode()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--ref", action="append", default=[],
                        help="reference image, cited in prompt order")
    parser.add_argument("--audio-ref", default="",
                        help="reference audio clip to choreograph/score against")
    parser.add_argument("--model", default="",
                        help="pin one ModelArk model id (default: try the "
                             "ladder " + " then ".join(MODELS) + ")")
    parser.add_argument("--seconds", type=int, default=30)
    parser.add_argument("--resolution", default="720p")
    parser.add_argument("--ratio", default="9:16")
    args = parser.parse_args()

    key = os.environ.get("ARK_API_KEY")
    if not key:
        sys.exit("ARK_API_KEY is not set")
    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()

    content = [{"type": "text", "text": prompt}]
    for ref in args.ref:
        content.append({"type": "image_url", "role": "reference_image",
                        "image_url": {"url": data_uri(ref)}})
    if args.audio_ref:
        # The validator rejects the stock audio/mpeg mime; it wants audio/mp3.
        with open(args.audio_ref, "rb") as handle:
            blob = base64.b64encode(handle.read()).decode()
        ext = os.path.splitext(args.audio_ref)[1].lstrip(".").lower() or "mp3"
        content.append({"type": "audio_url", "role": "reference_audio",
                        "audio_url": {"url": f"data:audio/{ext};base64," + blob}})

    payload = {"content": content,
               "duration": args.seconds, "resolution": args.resolution,
               "ratio": args.ratio, "generate_audio": True,
               "watermark": False}
    task = None
    used = None
    for model in ([args.model] if args.model else MODELS):
        print(f"model  {model} {args.seconds}s {args.resolution} "
              f"{args.ratio}\nrefs   {', '.join(args.ref)}", flush=True)
        try:
            reply = call(CREATE_URL, key, dict(payload, model=model))
        except RuntimeError as error:
            # An unknown/unauthorized model id falls down the ladder; any
            # other rejection (bad params, no quota) is real and final.
            text = str(error).lower()
            if any(word in text for word in ("model", "not found", "invalid endpoint")):
                print(f"  refused: {error}", flush=True)
                continue
            sys.exit(str(error))
        task = reply.get("id") or reply.get("task_id")
        if not task:
            sys.exit("no task id: " + json.dumps(reply)[:800])
        used = model
        break
    if not task:
        sys.exit("every model id refused; pass --model with the id from "
                 "the ModelArk console")
    print("task  ", task, flush=True)

    url = None
    for attempt in range(POLL_LIMIT):
        try:
            reply = call(QUERY_URL.format(task), key)
        except RuntimeError as error:
            sys.exit(str(error))
        status = (reply.get("status") or "").lower()
        print(f"  [{attempt * POLL_SECONDS:4d}s] {status or '?'}", flush=True)
        if status == "succeeded":
            url = (reply.get("content") or {}).get("video_url")
            if not url:
                sys.exit("succeeded with no url: " + json.dumps(reply)[:1500])
            break
        if status in ("failed", "cancelled"):
            sys.exit("generation failed: " + json.dumps(reply)[:1500])
        time.sleep(POLL_SECONDS)
    if not url:
        sys.exit("timed out waiting for the task")

    with urllib.request.urlopen(url, timeout=600) as response, \
            open(args.out, "wb") as handle:
        handle.write(response.read())
    print("wrote ", args.out)

    with open(args.out + ".json", "w", encoding="utf-8") as handle:
        json.dump({"model": used, "duration": args.seconds,
                   "resolution": args.resolution, "ratio": args.ratio,
                   "refs": [os.path.abspath(r) for r in args.ref],
                   "task_id": task, "source_url": url, "prompt": prompt},
                  handle, indent=2)


if __name__ == "__main__":
    main()

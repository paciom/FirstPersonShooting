#!/usr/bin/env python3
"""One-pass adventure stills: character AND room in a single generation,
conditioned on the robots' own reference renders.

    python Tools/adventure_onepass.py --openai            # gpt-image-1
    python Tools/adventure_onepass.py --gemini            # gemini-2.5-flash-image
    python Tools/adventure_onepass.py --ark               # BytePlus Seedream 4.0
    python Tools/adventure_onepass.py --openai n19 --force
    python Tools/adventure_onepass.py --openai --dry      # show a prompt, spend nothing

This is the pipeline the art SHOULD use: no compositing, and the characters can
be in poses the reference renders cannot strike (kneeling, hanging off a
ladder, reaching). Each beat's cast is read off its own shot list, and those
robots' reference renders are sent with the prompt.

Provider status on this machine, all verified rather than assumed:

  MiniMax image-01   CANNOT do it. subject_reference tested four ways (keyed
                     cutout on white, front preview render, width/height
                     instead of aspect_ratio, optimizer on and off) and it
                     returned a robot that is not Panther every time, in
                     photoreal gloom instead of the toon look.
  BytePlus Seedream  Model recognised, answers ModelNotOpen — needs activating
                     once in the Ark console.
  OpenAI gpt-image-1 No key. Drop one in .secrets/openai_key.txt.
  Gemini 2.5 Flash   No key. Drop one in .secrets/gemini_key.txt.

The OpenAI and Gemini paths are written from their documented request shapes
but have NEVER BEEN RUN, because there is no key here to run them with. Treat
the first invocation as a test: it prints the response shape it got on failure
rather than guessing.

Output goes straight to Assets/Resources/Adventures/<story>/<node>.png, the
same place the composited stills live, so the reader picks it up with no code
change and you can compare the two approaches beat by beat.
"""

import argparse
import base64
import json
import mimetypes
import os
import sys
import urllib.error
import urllib.request
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADVENTURES = os.path.join(ROOT, "Assets", "Resources", "Adventures")
SECRETS = os.path.join(ROOT, ".secrets")
STORY = "quiet-confirmation"

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from adventure_art import PLATES, STYLE, EMPTY, NO_TEXT          # noqa: E402

# Which real render stands in for whom. These are the keyed cutouts of the
# actual game models, so the reference is the character, not a description.
CAST_REFS = {
    "panther": "Tools/adventure_cutouts/panther_hero.png",
    "titan": "Tools/adventure_cutouts/titan_hero.png",
    "samurai": "Tools/adventure_cutouts/samurai_hero.png",
    "bolt": "Tools/adventure_cutouts/bolt_front.png",
    "warden": "Tools/adventure_cutouts/ranger_front.png",
}
KEEP = ("Use the robots in the reference images as the characters, exactly: "
        "the same colours, the same proportions, the same head design, the same "
        "plating and the same glowing panels. Do not redesign them. ")


def story_nodes():
    with open(os.path.join(ADVENTURES, STORY + ".json"), "r", encoding="utf-8") as f:
        return {n["id"]: n for n in json.load(f)["nodes"]}


def key_shot(node):
    return next((s for s in node.get("shots", []) if s.get("key")), node["shots"][0])


# The shot lists name characters the way prose does, so a plain name
# match misses "the stalker", "the chief" and "the kid".
ALIASES = {
    "panther": ("panther", "the stalker", "stalker,", "three orange claws",
                "orange plating", "amber visor"),
    "titan": ("titan", "enormous waiting palm", "yellow pauldrons"),
    "samurai": ("samurai",),
    "bolt": ("bolt", "the kid", "small navy hand", "young warden"),
    "warden": ("warden", "the chief", "old dented", "shoulder to shoulder"),
}
# Framings that are genuinely a prop or a place and have nobody in them.
INSERT_CAMS = ("MACRO INSERT", "EXTREME CLOSE")

# The handful the keyword rule reads wrong. Sending a reference for somebody
# who is not in the frame is an invitation to paint them into it.
OVERRIDES = {
    "n23": ["samurai"],        # Panther watches from off-frame; Titan is miles away
    "e_watch": ["warden"],     # a hand and a name tag; Panther is not in this one
}


def cast_in(node):
    """Who is in the FRAME — read off the key shot only. Scanning the whole
    beat pulls in characters who are somewhere else in the thirty seconds
    (Samurai is on a ridge in n19, nowhere near its key frame) and sending
    their reference render invites the model to paint them in."""
    if node["id"] in OVERRIDES:
        return list(OVERRIDES[node["id"]])
    shot = key_shot(node)
    text = (shot.get("action", "") + " " + shot.get("vo", "")).lower()
    who = [name for name, words in ALIASES.items()
           if any(word in text for word in words)]
    # Panther is the POV of the whole file, so he is in frame unless this is a
    # prop insert — the lines often only say "he", and the earlier version of
    # this rule dropped him from half his own beats.
    insert = any(cam in shot.get("cam", "") for cam in INSERT_CAMS)
    if "panther" not in who and not insert:
        who.insert(0, "panther")
    return who


def clean_setting(node_id):
    """The plate prompt with its compositing instructions removed. A plate is
    deliberately empty and reserves bare ground for a cutout; one-pass wants
    the opposite, so those clauses have to come out or the model leaves a
    character-shaped hole in the frame."""
    text = PLATES[node_id]
    for marker in (". " + EMPTY, ". COMPLETELY EMPTY", ". IMPORTANT:",
                   ". The near", ". The centre", ". The middle", ". The gap",
                   ". The foreground", ". Open wet ground", ". The ring itself",
                   ". The shelf and", ". no robots", ". No shop", ". The lower"):
        text = text.split(marker)[0]
    return text.rstrip(" .")


def build_prompt(node):
    """The set comes from the plate prompt (minus its compositing clauses), the
    action and framing come from the beat's key shot."""
    shot = key_shot(node)
    who = cast_in(node)
    return (f"{shot['cam']}. {shot['action']} "
            + (KEEP if who else "")
            + f"Setting: {clean_setting(node['id'])}. {STYLE}. {NO_TEXT}"), who


def secret(name):
    path = os.path.join(SECRETS, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read().strip()


def target_path(node_id):
    return os.path.join(ADVENTURES, STORY, node_id + ".png")


def crop_16x9(raw):
    """Providers hand back 3:2 or square; the reader draws a 16:9 rect."""
    try:
        from PIL import Image
    except ImportError:
        return raw
    import io as _io
    image = Image.open(_io.BytesIO(raw)).convert("RGB")
    want = image.width * 9 / 16
    if abs(image.height - want) > 2:
        top = int((image.height - want) / 2)
        image = image.crop((0, max(0, top), image.width, min(image.height, top + int(want))))
    buffer = _io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


# ------------------------------------------------------------------ OpenAI

def multipart(fields, files):
    """gpt-image-1's edits endpoint is multipart only, and urllib has no
    encoder for it."""
    boundary = "----adventure" + uuid.uuid4().hex
    body = b""
    for name, value in fields:
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n"
                 f"{value}\r\n").encode()
    for name, path in files:
        filename = os.path.basename(path)
        mime = mimetypes.guess_type(filename)[0] or "image/png"
        with open(path, "rb") as handle:
            payload = handle.read()
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; "
                 f"filename=\"{filename}\"\r\nContent-Type: {mime}\r\n\r\n").encode()
        body += payload + b"\r\n"
    body += f"--{boundary}--\r\n".encode()
    return body, "multipart/form-data; boundary=" + boundary


def generate_openai(node, key):
    prompt, who = build_prompt(node)
    refs = [os.path.join(ROOT, CAST_REFS[w]) for w in who]
    if refs:
        fields = [("model", "gpt-image-1"), ("prompt", prompt),
                  ("size", "1536x1024"), ("n", "1")]
        body, content_type = multipart(fields, [("image[]", r) for r in refs])
        url = "https://api.openai.com/v1/images/edits"
        headers = {"Authorization": "Bearer " + key, "Content-Type": content_type}
    else:
        body = json.dumps({"model": "gpt-image-1", "prompt": prompt,
                           "size": "1536x1024", "n": 1}).encode()
        url = "https://api.openai.com/v1/images/generations"
        headers = {"Authorization": "Bearer " + key, "Content-Type": "application/json"}
    request = urllib.request.Request(url, data=body, headers=headers)
    payload = json.loads(urllib.request.urlopen(request, timeout=600).read())
    return base64.b64decode(payload["data"][0]["b64_json"]), who


# ------------------------------------------------------------------ Gemini

def generate_gemini(node, key):
    prompt, who = build_prompt(node)
    parts = [{"text": prompt}]
    for name in who:
        with open(os.path.join(ROOT, CAST_REFS[name]), "rb") as handle:
            parts.append({"inline_data": {"mime_type": "image/png",
                                          "data": base64.b64encode(handle.read()).decode()}})
    body = json.dumps({
        "contents": [{"parts": parts}],
        "generationConfig": {"responseModalities": ["IMAGE"],
                             "imageConfig": {"aspectRatio": "16:9"}},
    }).encode()
    url = ("https://generativelanguage.googleapis.com/v1beta/models/"
           "gemini-2.5-flash-image:generateContent")
    request = urllib.request.Request(url, data=body, headers={
        "x-goog-api-key": key, "Content-Type": "application/json"})
    payload = json.loads(urllib.request.urlopen(request, timeout=600).read())
    for part in payload["candidates"][0]["content"]["parts"]:
        blob = part.get("inline_data") or part.get("inlineData")
        if blob:
            return base64.b64decode(blob["data"]), who
    raise RuntimeError("no image part in reply: " + json.dumps(payload)[:400])


# ---------------------------------------------------------------- BytePlus

def generate_ark(node, key_and_host):
    host, key = key_and_host
    prompt, who = build_prompt(node)
    body = {"model": "seedream-4-0-250828", "prompt": prompt, "size": "1280x720",
            "response_format": "url", "watermark": False}
    refs = ["data:image/png;base64," + base64.b64encode(
        open(os.path.join(ROOT, CAST_REFS[w]), "rb").read()).decode() for w in who]
    if refs:
        body["image"] = refs if len(refs) > 1 else refs[0]
    request = urllib.request.Request(host + "/images/generations",
        data=json.dumps(body).encode(),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    payload = json.loads(urllib.request.urlopen(request, timeout=600).read())
    with urllib.request.urlopen(payload["data"][0]["url"], timeout=300) as src:
        return src.read(), who


BACKENDS = {
    "openai": ("OpenAI gpt-image-1", "openai_key.txt",
               "https://platform.openai.com/api-keys  (gpt-image-1 needs a "
               "verified organisation)"),
    "gemini": ("Gemini 2.5 Flash Image", "gemini_key.txt",
               "https://aistudio.google.com/apikey"),
    "ark": ("BytePlus Seedream 4.0", "byteplus.env",
            "activate Seedream 4.0 in the Ark console"),
}


def main():
    parser = argparse.ArgumentParser()
    for flag in BACKENDS:
        parser.add_argument("--" + flag, action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry", action="store_true",
                        help="print the prompt and the cast, call nothing")
    parser.add_argument("nodes", nargs="*")
    args = parser.parse_args()

    picked = [f for f in BACKENDS if getattr(args, f)]
    if len(picked) != 1:
        sys.exit("pick exactly one backend: --openai, --gemini or --ark")
    backend = picked[0]
    label, keyfile, where = BACKENDS[backend]

    nodes = story_nodes()
    wanted = args.nodes or list(nodes)
    for nid in wanted:
        if nid not in nodes:
            sys.exit("unknown beat: " + nid)

    if args.dry:
        for nid in wanted:
            prompt, who = build_prompt(nodes[nid])
            print(f"\n=== {nid}  cast: {', '.join(who) or 'none'}\n{prompt}")
        return

    if backend == "ark":
        env = {}
        for line in open(os.path.join(SECRETS, "byteplus.env"), encoding="utf-8"):
            if "=" in line:
                k, v = line.strip().split("=", 1)
                env[k] = v
        credentials = (env["BYTEPLUS_ARK_BASE_URL"].rstrip("/"), env["ARK_API_KEY"])
    else:
        credentials = secret(keyfile)
        if not credentials:
            sys.exit(f"\nNo key for {label}.\n"
                     f"Put it in .secrets/{keyfile} (one line, no quotes).\n"
                     f"Get one: {where}\n"
                     f"Then: python Tools/adventure_onepass.py --{backend}\n")

    generate = {"openai": generate_openai, "gemini": generate_gemini,
                "ark": generate_ark}[backend]
    print(f"{label}: {len(wanted)} beats")
    failures = []
    for nid in wanted:
        path = target_path(nid)
        if os.path.exists(path) and not args.force:
            print(f"  skip  {nid}")
            continue
        try:
            raw, who = generate(nodes[nid], credentials)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as handle:
                handle.write(crop_16x9(raw))
            print(f"  ok    {nid}  ({', '.join(who) or 'no cast'})")
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")[:300]
            print(f"  FAIL  {nid}  HTTP {error.code}: {detail}")
            failures.append(nid)
        except Exception as error:
            print(f"  FAIL  {nid}  {type(error).__name__}: {str(error)[:300]}")
            failures.append(nid)
    if failures:
        print("\nretry: python Tools/adventure_onepass.py --%s %s"
              % (backend, " ".join(failures)))
        sys.exit(1)


if __name__ == "__main__":
    main()

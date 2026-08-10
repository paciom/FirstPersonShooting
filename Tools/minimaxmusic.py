"""Generates a song through MiniMax music_generation.

Same shape as minimaxvideo.py — one call per deliverable, request JSON written
next to the audio so every track traces back to what asked for it. The music
endpoint is synchronous (no task/poll round trip): one POST returns either a
download URL or hex-encoded audio.

  python minimaxmusic.py out.mp3 --prompt-file style.txt [--lyrics-file l.txt]
  python minimaxmusic.py out.mp3 --prompt-file style.txt --instrumental

The prompt file is the style/mood/scenario description (2000 chars max);
lyrics take [Intro]/[Verse]/[Chorus]/[Bridge]/[Outro] structure tags. With
--instrumental no lyrics are sent and is_instrumental asks for a vocal-free
arrangement.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

URL = "https://api.minimax.io/v1/music_generation"
# music-3.0 is the current recommended model; the -free variant is the
# fallback when the account's plan doesn't cover the paid tier.
MODELS = ["music-3.0", "music-3.0-free", "music-2.6", "music-2.6-free"]


def call(key, payload):
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(),
                                 headers={"Authorization": "Bearer " + key,
                                          "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=600) as response:
            reply = json.loads(response.read())
    except urllib.error.HTTPError as error:
        sys.exit(f"HTTP {error.code}: {error.read().decode(errors='replace')}")
    return reply


def generate(key, model, prompt, lyrics, instrumental):
    payload = {
        "model": model,
        "prompt": prompt,
        "audio_setting": {"sample_rate": 44100, "bitrate": 256000,
                          "format": "mp3"},
        "output_format": "url",
    }
    if instrumental:
        payload["is_instrumental"] = True
    else:
        payload["lyrics"] = lyrics
    # Rate limits are a wait, not a refusal — falling down the model ladder
    # on 1002 just burns the next model's quota too (that mistake cost half
    # a 60-song batch). Back off and retry the SAME model.
    for attempt in range(12):
        reply = call(key, payload)
        base = reply.get("base_resp") or {}
        code = base.get("status_code")
        if code in (0, None):
            return reply, None
        message = f"{code}: {base.get('status_msg')}"
        if "rate limit" not in (base.get("status_msg") or "").lower():
            return None, message
        print(f"  rate limited, waiting 60s ({attempt + 1}/12)", flush=True)
        time.sleep(60)
    return None, message


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--lyrics-file")
    parser.add_argument("--instrumental", action="store_true")
    args = parser.parse_args()
    if not args.instrumental and not args.lyrics_file:
        sys.exit("need --lyrics-file or --instrumental")

    key = os.environ.get("MINIMAX_API_KEY")
    if not key:
        sys.exit("MINIMAX_API_KEY is not set")
    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    lyrics = None
    if args.lyrics_file:
        with open(args.lyrics_file, encoding="utf-8") as handle:
            lyrics = handle.read().strip()

    reply = None
    used = None
    for model in MODELS:
        print(f"model  {model}", flush=True)
        reply, err = generate(key, model, prompt, lyrics, args.instrumental)
        if err is None:
            used = model
            break
        # Plan/permission errors move down the ladder; anything else is real.
        print(f"  refused: {err}", flush=True)

    if reply is None:
        sys.exit("every model refused")

    data = reply.get("data") or {}
    audio = data.get("audio")
    if not audio:
        sys.exit("no audio in reply: " + json.dumps(reply)[:800])

    if audio.startswith("http"):
        with urllib.request.urlopen(audio, timeout=600) as response, \
                open(args.out, "wb") as handle:
            handle.write(response.read())
    else:
        # hex fallback — some responses inline the bytes regardless.
        with open(args.out, "wb") as handle:
            handle.write(bytes.fromhex(audio))
    print("wrote ", args.out)

    with open(args.out + ".json", "w", encoding="utf-8") as handle:
        json.dump({"model": used, "prompt": prompt, "lyrics": lyrics,
                   "instrumental": args.instrumental,
                   "extra": {k: v for k, v in data.items() if k != "audio"}},
                  handle, indent=2)


if __name__ == "__main__":
    main()

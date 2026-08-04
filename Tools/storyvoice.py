#!/usr/bin/env python3
"""Bake an episode's dialogue to WAV with Azure neural TTS.

Reads Assets/Resources/Episodes/<episode>.json, finds every beat carrying a
`voice` id and a `text` line, and synthesizes the missing clips into
Assets/Resources/Episodes/<episode>/voice/<id>.wav — the folder StoryVoice
loads from. Per-character voice, rate and pitch come from the episode's own
cast entries (voiceName/rate/pitch), so the episode file stays the single
source of truth.

    python Tools/storyvoice.py [episode] [--force]

Key/region live in .secrets/azure_speech_key.txt / azure_speech_region.txt
(never printed). The F0 free tier allows 500K chars/month — an episode is
~1K, so quota is never the constraint. Neural output is licensed for
commercial use on paid tiers; F0 is fine for the pilot while we evaluate.
"""
import json
import pathlib
import sys
import time
import urllib.request
import xml.sax.saxutils

ROOT = pathlib.Path(__file__).resolve().parent.parent


def read_secret(name):
    path = ROOT / ".secrets" / name
    if not path.exists():
        sys.exit(f"missing {path} — create the speech resource first")
    return path.read_text().strip()


def ssml(voice, rate, pitch, text):
    body = xml.sax.saxutils.escape(text)
    prosody = ""
    if rate or pitch:
        attrs = ""
        if rate:
            attrs += f" rate='{rate}'"
        if pitch:
            attrs += f" pitch='{pitch}'"
        body = f"<prosody{attrs}>{body}</prosody>"
    return (
        "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' "
        "xml:lang='en-US'>"
        f"<voice name='{voice}'>{body}</voice></speak>"
    )


def bake(region, key, out_path, markup):
    request = urllib.request.Request(
        f"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1",
        data=markup.encode("utf-8"),
        headers={
            "Ocp-Apim-Subscription-Key": key,
            "Content-Type": "application/ssml+xml",
            "X-Microsoft-OutputFormat": "riff-24khz-16bit-mono-pcm",
            "User-Agent": "photon-arena-storyvoice",
        },
    )
    with urllib.request.urlopen(request, timeout=60) as reply:
        out_path.write_bytes(reply.read())


def main():
    episode = next((a for a in sys.argv[1:] if not a.startswith("-")), "pilot")
    force = "--force" in sys.argv

    script_path = ROOT / "Assets/Resources/Episodes" / f"{episode}.json"
    if not script_path.exists():
        sys.exit(f"no episode at {script_path}")
    data = json.loads(script_path.read_text(encoding="utf-8"))

    voices = {
        member["name"].lower(): (
            member.get("voiceName", "en-US-DavisNeural"),
            member.get("rate", ""),
            member.get("pitch", ""),
        )
        for member in data.get("cast", [])
    }

    out_dir = ROOT / "Assets/Resources/Episodes" / episode / "voice"
    out_dir.mkdir(parents=True, exist_ok=True)

    region = read_secret("azure_speech_region.txt")
    key = read_secret("azure_speech_key.txt")

    baked = skipped = 0
    for scene in data.get("scenes", []):
        for beat in scene.get("beats", []):
            clip_id, text = beat.get("voice"), beat.get("text")
            if not clip_id or not text:
                continue
            out_path = out_dir / f"{clip_id}.wav"
            if out_path.exists() and not force:
                skipped += 1
                continue
            who = (beat.get("who") or "").lower()
            voice, rate, pitch = voices.get(who, ("en-US-DavisNeural", "", ""))
            bake(region, key, out_path, ssml(voice, rate, pitch, text))
            print(f"  {clip_id}.wav  [{who} / {voice}]  \"{text[:50]}\"")
            baked += 1
            time.sleep(0.3)  # stay far inside the free tier's rate limit

    print(f"{episode}: baked {baked}, kept {skipped} -> {out_dir}")


if __name__ == "__main__":
    main()

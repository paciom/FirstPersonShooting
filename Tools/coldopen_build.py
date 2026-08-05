#!/usr/bin/env python3
"""Assemble the S01E01 cold open as a stills-cinematic.

    python Tools/coldopen_build.py            # voices + ambience + video
    python Tools/coldopen_build.py --voices   # just rebake dialogue

Takes the 13 stills in Stories/S01E01/shots, bakes the scene's seven voice
lines with Azure neural TTS, synthesizes an ambience bed (wind, launch
rumble, rain) with numpy, and cuts the whole thing with ffmpeg: Ken Burns
drift on every still, 0.5s crossfades, warm grade + film grain + vignette
(it plays as archive footage — the grain is diegetic), title cards, and a
static degrade at the end. Output: Renders/S01E01_coldopen.mp4, ~95 s.

Shot durations stretch to fit their dialogue automatically: the timeline is
derived from the baked WAV lengths, never hand-synced.
"""
import json
import math
import os
import struct
import subprocess
import sys
import urllib.request
import wave
import xml.sax.saxutils

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EP = os.path.join(ROOT, "Stories", "S01E01")
SHOTS = os.path.join(EP, "shots")
AUDIO = os.path.join(EP, "audio")
WORK = os.path.join(EP, "build")
OUT = os.path.join(ROOT, "Renders", "S01E01_coldopen.mp4")
SR = 44100
FPS = 30

# ------------------------------------------------------------------ voices

MAYA = ("en-US-AnaNeural", "+4%", "+0%")
TENDER = ("en-US-GuyNeural", "-18%", "-16%")
SPEAKER = ("en-US-SaraNeural", "-2%", "-4%")

LINES = {
    "m1": (MAYA, "Dad says where we're going, the sky is fake. A big glass one."),
    "t1": (TENDER, "A ceiling... is not a sky."),
    "m2": (MAYA, "That's what I SAID!"),
    "ls": (SPEAKER, "Final boarding. Sector nine. Final boarding."),
    "m3": (MAYA, "Keep my garden safe. Plant them when the rain stops burning. "
                 "That's your job now. Promise."),
    "t2": (TENDER, "Promise."),
    "m4": (MAYA, "When the sky's blue, we come home! That's YOUR promise and "
                 "MY promise! Blue sky, Tender!"),
}


def read_secret(name):
    with open(os.path.join(ROOT, ".secrets", name)) as f:
        return f.read().strip()


def bake_voices(force=False):
    os.makedirs(AUDIO, exist_ok=True)
    region, key = read_secret("azure_speech_region.txt"), read_secret("azure_speech_key.txt")
    for clip_id, ((voice, rate, pitch), text) in LINES.items():
        path = os.path.join(AUDIO, clip_id + ".wav")
        if os.path.exists(path) and not force:
            continue
        body = xml.sax.saxutils.escape(text)
        markup = ("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' "
                  f"xml:lang='en-US'><voice name='{voice}'>"
                  f"<prosody rate='{rate}' pitch='{pitch}'>{body}</prosody>"
                  "</voice></speak>")
        req = urllib.request.Request(
            f"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1",
            data=markup.encode(),
            headers={"Ocp-Apim-Subscription-Key": key,
                     "Content-Type": "application/ssml+xml",
                     "X-Microsoft-OutputFormat": "riff-44100hz-16bit-mono-pcm",
                     "User-Agent": "jah-coldopen"})
        with urllib.request.urlopen(req, timeout=60) as reply:
            open(path, "wb").write(reply.read())
        print(f"  voice {clip_id}")


def load_wav(path):
    with wave.open(path) as w:
        data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
        return data.astype(np.float32) / 32768.0


def wav_seconds(path):
    with wave.open(path) as w:
        return w.getnframes() / w.getframerate()


def loudspeaker_fx(x):
    """Tannoy: band-limit hard, clip a little, slap one echo on it."""
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    spectrum[(freqs < 500) | (freqs > 2600)] = 0
    x = np.fft.irfft(spectrum, len(x)).astype(np.float32)
    x = np.clip(x * 2.2, -0.55, 0.55)
    echo = np.zeros_like(x)
    d = int(0.11 * SR)
    echo[d:] = x[:-d] * 0.35
    return x + echo

# ----------------------------------------------------------------- timeline


def build_timeline():
    """Shot order, base duration, and which line plays over each."""
    def vlen(cid):
        return wav_seconds(os.path.join(AUDIO, cid + ".wav"))

    rows = [
        ("s01_establish", 8.0, None),
        ("s02_repotting", 6.5, None),
        ("s03_maya_watches", max(6.0, vlen("m1") + 2.0), "m1"),
        ("s04_tender_close", max(4.5, vlen("t1") + 1.6), "t1"),
        ("s05_maya_reply", max(3.5, vlen("m2") + 1.5), "m2"),
        ("s06_final_boarding", max(5.5, vlen("ls") + 2.2), "ls"),
        ("s07_the_tin", max(8.0, vlen("m3") + 2.2), "m3"),
        ("s08_promise", max(4.5, vlen("t2") + 2.6), "t2"),
        ("s09_maya_runs", max(7.0, vlen("m4") + 1.6), "m4"),
        ("s10_rockets", 9.5, None),
        ("s11_alone", 8.0, None),
        ("s12_burning_rain", 6.0, None),
        ("s13_tender_rain", 5.0, None),
        ("card_title", 4.0, None),
        ("card_years", 3.5, None),
    ]
    return rows

# ---------------------------------------------------------------- ambience


def synth_ambience(rows):
    total = sum(d for _, d, _ in rows)
    n = int(total * SR)
    t = np.arange(n) / SR
    rng = np.random.default_rng(7)

    def start_of(name):
        at = 0.0
        for shot, d, _ in rows:
            if shot == name:
                return at
            at += d
        return total

    # Wind: heavy-lowpassed noise with a slow breath.
    wind = rng.standard_normal(n).astype(np.float32)
    kernel = np.ones(900, dtype=np.float32) / 900
    wind = np.convolve(wind, kernel, mode="same")
    wind *= 0.5 + 0.22 * np.sin(2 * np.pi * 0.07 * t + 1.2)
    wind *= 0.30

    # A sparse minor drone under everything - hope with a bruise on it.
    drone = np.zeros(n, dtype=np.float32)
    for freq, amp in ((110.0, 0.05), (130.8, 0.035), (164.8, 0.04)):
        drone += amp * np.sin(2 * np.pi * freq * t + freq)
    drone *= 0.5 + 0.5 * np.sin(2 * np.pi * 0.03 * t - 1.5)

    # Launch rumble: swells through Maya's run, peaks on the rocket sky,
    # dies during the alone shot.
    r0, r1 = start_of("s09_maya_runs"), start_of("s11_alone") + 2.0
    rumble = rng.standard_normal(n).astype(np.float32)
    rumble = np.convolve(rumble, np.ones(2400, dtype=np.float32) / 2400, mode="same")
    env = np.clip((t - r0) / 6.0, 0, 1) * np.clip((r1 - t) / 4.0, 0, 1)
    rumble *= env * 1.5

    # Rain: fades in on the alone shot and stays to the end.
    rain_at = start_of("s11_alone")
    rain = rng.standard_normal(n).astype(np.float32)
    rain -= np.convolve(rain, np.ones(24, dtype=np.float32) / 24, mode="same")
    rain *= np.clip((t - rain_at) / 5.0, 0, 1) * 0.16

    # Static burst under the archive degrade on the last held shot.
    st = start_of("card_title") - 1.2
    static = rng.standard_normal(n).astype(np.float32) * 0.28
    static *= np.clip((t - st) / 0.9, 0, 1) * (t < start_of("card_title") + 0.4)

    mix = wind + drone + rumble + rain + static
    # Duck the bed a little wherever dialogue plays.
    at = 0.0
    for shot, d, line in rows:
        if line:
            a, b = int((at + 0.6) * SR), int((at + 0.6 + wav_seconds(
                os.path.join(AUDIO, line + ".wav"))) * SR)
            mix[a:b] *= 0.55
        at += d
    return mix, total


def mix_audio(rows):
    bed, total = synth_ambience(rows)
    n = len(bed)
    at = 0.0
    for shot, d, line in rows:
        if line:
            x = load_wav(os.path.join(AUDIO, line + ".wav"))
            if line == "ls":
                x = loudspeaker_fx(x)
            i = int((at + 0.6) * SR)
            bed[i:i + len(x)] += x[:max(0, n - i)] * 0.95
        at += d
    peak = np.max(np.abs(bed))
    if peak > 0.94:
        bed *= 0.94 / peak
    path = os.path.join(WORK, "mix.wav")
    data = (bed * 32767).astype(np.int16)
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    return path, total

# ------------------------------------------------------------------- video


def make_card(name, big, small=None):
    img = Image.new("RGB", (1920, 1080), (2, 6, 12))
    draw = ImageDraw.Draw(img)
    try:
        font_big = ImageFont.truetype("C:/Windows/Fonts/bahnschrift.ttf", 110)
        font_small = ImageFont.truetype("C:/Windows/Fonts/bahnschrift.ttf", 46)
    except OSError:
        font_big = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 110)
        font_small = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 46)
    w = draw.textlength(big, font=font_big)
    draw.text(((1920 - w) / 2, 460), big, fill=(60, 225, 255), font=font_big)
    if small:
        w = draw.textlength(small, font=font_small)
        draw.text(((1920 - w) / 2, 610), small, fill=(220, 228, 235), font=font_small)
    path = os.path.join(WORK, name + ".png")
    img.save(path)
    return path


def run(cmd):
    subprocess.run(cmd, check=True, capture_output=True)


def build_video(rows, mix_path):
    os.makedirs(WORK, exist_ok=True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    cards = {
        "card_title": make_card("card_title", "JET  ARMOR  HEROES"),
        "card_years": make_card("card_years", "200  YEARS  LATER"),
    }

    # One Ken Burns clip per shot. Zoom alternates in/out; stills are
    # upscaled 2x first so the drift never samples below output size.
    clips = []
    for index, (shot, dur, _) in enumerate(rows):
        src = cards.get(shot) or os.path.join(SHOTS, shot + ".png")
        clip = os.path.join(WORK, f"clip{index:02d}.mp4")
        frames = int(dur * FPS)
        if shot.startswith("card"):
            filt = (f"scale=1920:1080,fade=t=in:d=0.5,"
                    f"fade=t=out:st={dur - 0.5:.2f}:d=0.5")
        else:
            zin = index % 2 == 0
            z = (f"zoom+{0.10 / frames:.6f}" if zin
                 else f"1.10-{0.10 / frames:.6f}*on")
            filt = (
                "scale=3840:2160,"
                f"zoompan=z='{z}':x='iw/2-(iw/zoom/2)':"
                f"y='ih/2-(ih/zoom/2)+({index % 3 - 1})*8':d={frames}:s=1920x1080:fps={FPS}")
        run(["ffmpeg", "-y", "-loop", "1", "-t", f"{dur:.3f}", "-i", src,
             "-vf", filt, "-t", f"{dur:.3f}", "-r", str(FPS),
             "-c:v", "libx264", "-preset", "fast", "-crf", "17",
             "-pix_fmt", "yuv420p", clip])
        clips.append((clip, dur))
        print(f"  clip {shot} {dur:.1f}s")

    # Crossfade chain: 0.5s between stills, hard cut into the title cards.
    inputs = []
    for clip, _ in clips:
        inputs += ["-i", clip]
    fade = 0.5
    graph, label, offset = [], "0:v", 0.0
    for i in range(1, len(clips)):
        prev_dur = clips[i - 1][1]
        this_cut = clips[i][0].endswith("13.mp4") or clips[i][0].endswith("14.mp4")
        f = 0.05 if this_cut else fade
        offset += prev_dur - f
        out = f"v{i}"
        graph.append(f"[{label}][{i}:v]xfade=transition=fade:"
                     f"duration={f}:offset={offset:.3f}[{out}]")
        label = out
    # The grade: warm curve, vignette, living grain - the archive look.
    graph.append(f"[{label}]curves=r='0/0 0.5/0.55 1/1':b='0/0 0.5/0.45 1/0.95',"
                 "vignette=PI/5,noise=alls=9:allf=t,"
                 f"fade=t=in:d=1.0[vout]")
    run(["ffmpeg", "-y", *inputs, "-i", mix_path,
         "-filter_complex", ";".join(graph),
         "-map", "[vout]", "-map", f"{len(clips)}:a",
         "-c:v", "libx264", "-preset", "medium", "-crf", "18",
         "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
         "-shortest", OUT])
    print(f"  wrote {OUT}")


def main(argv):
    os.makedirs(WORK, exist_ok=True)
    bake_voices(force="--force" in argv)
    if "--voices" in argv:
        return
    rows = build_timeline()
    total = sum(d for _, d, _ in rows)
    print(f"timeline: {len(rows)} shots, {total:.1f}s")
    mix_path, _ = mix_audio(rows)
    build_video(rows, mix_path)


if __name__ == "__main__":
    main(sys.argv[1:])

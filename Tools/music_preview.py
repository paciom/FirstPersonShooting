"""Offline render of GameMusic's synthesized tracks, for auditioning
without opening Unity. This is a line-for-line port of the pattern
synthesizer in Assets/Scripts/GameMusic.cs — if a track sounds wrong
here, fix the Spec THERE and re-render here to check.

    python Tools/music_preview.py [outdir]

Writes <track>.wav (16-bit stereo, one full loop) per track.
"""
import math
import os
import random
import struct
import sys
import wave

RATE = 32000

MINOR = [0, 2, 3, 5, 7, 8, 10]
MAJOR = [0, 2, 4, 5, 7, 9, 11]
PENTA = [0, 2, 4, 7, 9]

SPECS = {
    "menu": dict(bpm=96, root=50, scale=MAJOR, chords=[0, 4, 5, 3, 0, 4, 3, 4],
                 kick="x.......x.......", snare="................",
                 hat="..x...x...x...x.", bass="r.......r.......",
                 arp_every=2, arp_oct=0, drums=0.5, bass_lvl=0.35, pad=0.55, arp=0.16),
    "arena": dict(bpm=128, root=45, scale=MINOR, chords=[0, 0, 5, 5, 2, 2, 6, 6],
                  kick="x...x...x...x...", snare="....x.......x...",
                  hat="..x...x...x...x.", bass="r.r.r.r.r.r.r.r.",
                  arp_every=1, arp_oct=12, drums=1.0, bass_lvl=0.5, pad=0.35, arp=0.22),
    "brawl": dict(bpm=134, root=43, scale=MINOR, chords=[0, 0, 5, 6, 0, 0, 3, 6],
                  kick="x..x....x..x....", snare="....x.......x..x",
                  hat="x.x.x.x.x.x.x.x.", bass="r..r..r.r..r..o.",
                  arp_every=1, arp_oct=12, drums=1.0, bass_lvl=0.55, pad=0.3, arp=0.18),
    "commander": dict(bpm=92, root=48, scale=MINOR, chords=[0, 0, 3, 3, 5, 5, 4, 4],
                      kick="x.......x.......", snare="................",
                      hat="....x.......x...", bass="r.....r.........",
                      arp_every=2, arp_oct=0, drums=0.6, bass_lvl=0.4, pad=0.6, arp=0.12),
    "towerdefense": dict(bpm=112, root=43, scale=MINOR, chords=[0, 6, 5, 6, 0, 6, 3, 4],
                         kick="x...x...x...x...", snare="....x.......x...",
                         hat="..x...x...x...x.", bass="r.r.o.r.r.r.o.r.",
                         arp_every=2, arp_oct=12, drums=0.85, bass_lvl=0.5, pad=0.4, arp=0.2),
    "tankraid": dict(bpm=140, root=47, scale=MINOR, chords=[0, 0, 5, 6, 0, 0, 5, 6],
                     kick="x...x...x...x...", snare="....x.......x...",
                     hat="x.x.x.x.x.x.x.x.", bass="r.r.r.r.r.r.r.r.",
                     arp_every=1, arp_oct=12, drums=1.0, bass_lvl=0.5, pad=0.25, arp=0.3),
    "dogfight": dict(bpm=138, root=50, scale=MINOR, chords=[0, 6, 5, 6, 0, 6, 2, 6],
                     kick="x...x...x...x...", snare="....x.......x...",
                     hat="..x...x...x...x.", bass="r.r.r.r.r.r.r.o.",
                     arp_every=1, arp_oct=12, drums=0.95, bass_lvl=0.5, pad=0.35, arp=0.26),
    "chinese": dict(bpm=88, root=48, scale=PENTA, chords=[0, 3, 1, 4, 0, 3, 4, 0],
                    kick="x.......x.......", snare="................",
                    hat="..x.....x....x..", bass="r.......r.......",
                    arp_every=2, arp_oct=12, drums=0.4, bass_lvl=0.3, pad=0.45, arp=0.3),
    "adventure": dict(bpm=76, root=45, scale=MINOR, chords=[0, 5, 3, 6, 0, 5, 4, 6],
                      kick="................", snare="................",
                      hat="................", bass="r...............",
                      arp_every=4, arp_oct=12, drums=0.0, bass_lvl=0.35, pad=0.7, arp=0.15),
}


def freq(midi):
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def chord_semis(scale, degree):
    out = []
    for k in range(3):
        idx = degree + 2 * k
        out.append(scale[idx % len(scale)] + 12 * (idx // len(scale)))
    return out


def bass_len(pattern, step):
    for n in range(1, 4):
        if pattern[(step + n) % 16] != ".":
            return n
    return 4


def render(name, s):
    bars = len(s["chords"])
    sec_per_step = 60.0 / s["bpm"] / 4.0
    frames = math.ceil(bars * 16 * sec_per_step * RATE)
    left = [0.0] * frames
    right = [0.0] * frames
    rng = random.Random(name)

    def add(buf, at, i, v):
        buf[(at + i) % frames] += v

    def kick(at, level):
        phase = 0.0
        for i in range(int(0.3 * RATE)):
            t = i / RATE
            phase += 2 * math.pi * (44 + 85 * math.exp(-30 * t)) / RATE
            v = math.sin(phase) * math.exp(-11 * t) * 0.85 * level
            add(left, at, i, v)
            add(right, at, i, v)

    def snare(at, level):
        for i in range(int(0.22 * RATE)):
            t = i / RATE
            noise = rng.uniform(-1, 1)
            v = (noise * 0.6 * math.exp(-25 * t)
                 + math.sin(2 * math.pi * 190 * t) * 0.35 * math.exp(-40 * t)) * level
            add(left, at, i, v)
            add(right, at, i, v)

    def hat(at, level):
        prev = 0.0
        for i in range(int(0.07 * RATE)):
            t = i / RATE
            noise = rng.uniform(-1, 1)
            v = (noise - prev) * math.exp(-60 * t) * 0.35 * level
            prev = noise
            add(left, at, i, v * 0.8)
            add(right, at, i, v)

    def bass(at, seconds, hz, level):
        p1 = p2 = lp = 0.0
        n = int(seconds * RATE)
        for i in range(n):
            t = i / RATE
            p1 = (p1 + hz * 1.004 / RATE) % 1.0
            p2 = (p2 + hz * 0.996 / RATE) % 1.0
            saw = (p1 * 2 - 1) + (p2 * 2 - 1)
            a = 0.35 + (0.08 - 0.35) * min(1.0, t * 6)
            lp += a * (saw - lp)
            env = (min(1.0, t * 250) * min(1.0, (seconds - t) * 40)
                   * (0.35 + 0.65 * math.exp(-4 * t)))
            v = lp * env * 0.5 * level
            add(left, at, i, v)
            add(right, at, i, v)

    def pad(at, seconds, root, chord, level):
        n = int(seconds * RATE)
        for semi in [chord[0], chord[1], chord[2], chord[0] + 12]:
            hz = freq(root + semi)
            p1 = p2 = lp_l = lp_r = 0.0
            for i in range(n):
                t = i / RATE
                p1 = (p1 + hz * 1.003 / RATE) % 1.0
                p2 = (p2 + hz * 0.997 / RATE) % 1.0
                env = min(1.0, t / 0.35) * min(1.0, (seconds - t) / 0.3)
                lp_l += 0.10 * ((p1 * 2 - 1) - lp_l)
                lp_r += 0.10 * ((p2 * 2 - 1) - lp_r)
                add(left, at, i, lp_l * env * 0.11 * level)
                add(right, at, i, lp_r * env * 0.11 * level)

    def pluck(at, seconds, hz, level, pan):
        phase = lp = 0.0
        l_gain = max(0.0, min(1.0, 1 - pan))
        r_gain = max(0.0, min(1.0, 1 + pan))
        for i in range(int(seconds * RATE)):
            t = i / RATE
            phase = (phase + hz / RATE) % 1.0
            square = 1.0 if phase < 0.5 else -1.0
            lp += 0.25 * (square - lp)
            v = lp * math.exp(-9 * t) * min(1.0, t * 400) * 0.3 * level
            add(left, at, i, v * l_gain)
            add(right, at, i, v * r_gain)

    arp_index = 0
    for bar in range(bars):
        chord = chord_semis(s["scale"], s["chords"][bar])
        bar_start = bar * 16 * sec_per_step
        if s["pad"] > 0:
            pad(int(bar_start * RATE), 16 * sec_per_step, s["root"], chord, s["pad"])
        for step in range(16):
            at = int((bar_start + step * sec_per_step) * RATE)
            if s["drums"] > 0:
                if s["kick"][step] == "x":
                    kick(at, s["drums"])
                if s["snare"][step] == "x":
                    snare(at, s["drums"])
                if s["hat"][step] == "x":
                    hat(at, s["drums"])
            if s["bass_lvl"] > 0 and s["bass"][step] != ".":
                semi = (chord[0] + 12 if s["bass"][step] == "o"
                        else chord[0] + 7 if s["bass"][step] == "f" else chord[0])
                bass(at, bass_len(s["bass"], step) * sec_per_step * 0.95,
                     freq(s["root"] - 12 + semi), s["bass_lvl"])
            if s["arp_every"] > 0 and s["arp"] > 0 and step % s["arp_every"] == 0:
                tone = chord[arp_index % 3] + (12 if arp_index % 4 == 3 else 0)
                pluck(at, s["arp_every"] * sec_per_step * 0.9,
                      freq(s["root"] + 12 + s["arp_oct"] + tone), s["arp"],
                      -0.25 if arp_index % 2 == 0 else 0.25)
                arp_index += 1

    peak = max(0.001, max(max(abs(v) for v in left), max(abs(v) for v in right)))
    gain = min(1.5, 0.8 / peak)
    data = bytearray()
    for i in range(frames):
        for buf in (left, right):
            v = math.tanh(buf[i] * gain * 1.3) * 0.8
            data += struct.pack("<h", int(v * 32767))
    return frames, bytes(data), peak


def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else "Renders/music_preview"
    os.makedirs(outdir, exist_ok=True)
    for name, spec in SPECS.items():
        frames, data, peak = render(name, spec)
        path = os.path.join(outdir, name + ".wav")
        with wave.open(path, "wb") as w:
            w.setnchannels(2)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes(data)
        print(f"{name:14s} {frames / RATE:5.1f}s  pre-gain peak {peak:5.2f}  -> {path}")


if __name__ == "__main__":
    main()

"""Cut a generated song into a seamless game-music loop.

AI music services return SONGS - intro, arc, outro, fade. A game loop wants
none of that: a steady slab from the middle that joins back onto itself
without a click or a stumble. This tool:

  1. decodes via ffmpeg,
  2. estimates the beat period (autocorrelation of the onset envelope -
     no librosa needed),
  3. picks a loop window from the steady middle of the track, snapped to a
     whole multiple of 4 beats so the rhythm doesn't hiccup at the seam,
  4. slides the loop END point +/- one beat to the offset whose waveform
     best matches the loop START (max cross-correlation), so the splice
     lands where the audio actually repeats,
  5. equal-power crossfades the tail into the head, and
  6. writes <name>.ogg (Vorbis q5) ready for Assets/Resources/Music/.

  python Tools/music_loopify.py in.mp3 out.ogg [--seconds 60] [--start 0.2]

--seconds is the TARGET loop length (snapped to bars); --start is where the
window may begin, as a fraction of the track (default 0.2 skips the intro).
Verify by ear: ffplay -loop 0 out.ogg
"""
import argparse
import os
import subprocess
import sys
import tempfile

import numpy as np

RATE = 44100


def decode(path):
    """mp3/whatever -> float32 stereo [n, 2] via ffmpeg."""
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2",
         "-ar", str(RATE), "-"],
        capture_output=True, check=True).stdout
    data = np.frombuffer(raw, dtype=np.float32).reshape(-1, 2)
    return data


def onset_envelope(mono, hop=512):
    """Half-wave-rectified spectral flux - louder where new notes hit."""
    window = np.hanning(2048)
    frames = []
    for i in range(0, len(mono) - 2048, hop):
        frames.append(np.abs(np.fft.rfft(mono[i:i + 2048] * window)))
    spec = np.array(frames)
    flux = np.diff(spec, axis=0)
    flux[flux < 0] = 0
    return flux.sum(axis=1), hop


def beat_period(mono):
    """Beat length in samples, from the onset envelope's autocorrelation.
    Searches 60-180 BPM; returns RATE (one second) if nothing convincing."""
    env, hop = onset_envelope(mono)
    env = env - env.mean()
    corr = np.correlate(env, env, mode="full")[len(env) - 1:]
    lo = int(60.0 / 180.0 * RATE / hop)          # 180 BPM in envelope frames
    hi = int(60.0 / 60.0 * RATE / hop)           # 60 BPM
    if hi >= len(corr):
        return RATE
    lag = lo + int(np.argmax(corr[lo:hi]))
    return lag * hop


def loopify(audio, target_seconds, start_fraction):
    n = len(audio)
    mono = audio.mean(axis=1)
    beat = beat_period(mono)
    bar = beat * 4

    # The window: begin past the intro, end well before the outro/fade.
    start = int(n * start_fraction)
    limit = int(n * 0.92)
    bars = max(4, int(round(target_seconds * RATE / bar)))
    length = bars * bar
    while start + length > limit and bars > 4:
        bars -= 1
        length = bars * bar
    if start + length > limit:
        length = limit - start          # short track: take what exists

    # Slide the end +/- one beat for the best waveform match to the start.
    probe = int(0.05 * RATE)            # 50 ms of correlation probe
    head = mono[start:start + probe]
    best, best_score = 0, -1e18
    for off in range(-beat, beat, max(1, beat // 64)):
        j = start + length + off
        if j + probe >= n:
            continue
        score = float(np.dot(head, mono[j:j + probe]))
        if score > best_score:
            best_score, best = score, off
    end = start + length + best

    loop = audio[start:end].copy()
    # Equal-power crossfade: the last `fade` samples merge with the audio
    # that preceded the loop start, which is what the end will "become".
    fade = min(int(0.25 * RATE), start, len(loop) // 4)
    t = np.linspace(0, np.pi / 2, fade)[:, None]
    loop[-fade:] = (loop[-fade:] * np.cos(t) ** 2
                    + audio[start - fade:start] * np.sin(t) ** 2)
    return loop, beat


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("src")
    parser.add_argument("out")
    parser.add_argument("--seconds", type=float, default=60.0)
    parser.add_argument("--start", type=float, default=0.2)
    args = parser.parse_args()

    audio = decode(args.src)
    loop, beat = loopify(audio, args.seconds, args.start)
    bpm = 60.0 * RATE / beat

    with tempfile.NamedTemporaryFile(suffix=".f32", delete=False) as handle:
        handle.write(loop.astype(np.float32).tobytes())
        tmp = handle.name
    try:
        subprocess.run(
            ["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ac", "2",
             "-ar", str(RATE), "-i", tmp, "-c:a", "libvorbis", "-q:a", "5",
             args.out],
            check=True)
    finally:
        os.unlink(tmp)
    print(f"{os.path.basename(args.out)}: {len(loop) / RATE:.1f}s loop, "
          f"~{bpm:.0f} BPM detected")


if __name__ == "__main__":
    main()

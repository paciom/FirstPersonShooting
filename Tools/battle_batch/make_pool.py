"""Generates the 60-song pool: mp3 + offset-zero lyric timing per song.

Idempotent — a song with an mp3 is not regenerated, a song with a _sub.json
is not re-aligned — so the script can be re-run after any interruption.
Alignment (Whisper, CPU-heavy) is skipped with --no-align so generation can
run alongside the Unity recording batch; run again without the flag later.

  python make_pool.py [--no-align]
"""
import argparse
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from songs_a import SONGS_A
from songs_b import SONGS_B
from songs_c import SONGS_C

HERE = os.path.dirname(os.path.abspath(__file__))
POOL = os.path.join(HERE, "pool")
TOOLS = os.path.dirname(HERE)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--no-align", action="store_true")
    args = parser.parse_args()

    os.makedirs(POOL, exist_ok=True)
    songs = SONGS_A + SONGS_B + SONGS_C
    print(f"{len(songs)} songs in pool")

    failures = []
    for song in songs:
        base = os.path.join(POOL, song["id"])
        with open(base + "_style.txt", "w", encoding="utf-8") as handle:
            handle.write(song["style"])
        with open(base + "_lyrics.txt", "w", encoding="utf-8") as handle:
            handle.write(song["lyrics"])

        if not os.path.exists(base + ".mp3"):
            print(f"generate {song['id']} \"{song['title']}\"", flush=True)
            time.sleep(20)  # stay under the API's requests-per-minute cap
            result = subprocess.run(
                [sys.executable, os.path.join(TOOLS, "minimaxmusic.py"),
                 base + ".mp3", "--prompt-file", base + "_style.txt",
                 "--lyrics-file", base + "_lyrics.txt"])
            if result.returncode != 0:
                failures.append(song["id"])
                continue

        if not args.no_align and not os.path.exists(base + "_sub.json"):
            print(f"align    {song['id']}", flush=True)
            result = subprocess.run(
                [sys.executable, os.path.join(TOOLS, "lyricsub.py"),
                 base + ".mp3", base + "_lyrics.txt",
                 "--offset", "0", "--json", base + "_sub.json"])
            if result.returncode != 0:
                failures.append(song["id"] + " (align)")

    done = sum(1 for song in songs
               if os.path.exists(os.path.join(POOL, song["id"] + ".mp3")))
    print(f"POOL: {done}/{len(songs)} generated"
          + (f", FAILURES: {failures}" if failures else ""))


if __name__ == "__main__":
    main()

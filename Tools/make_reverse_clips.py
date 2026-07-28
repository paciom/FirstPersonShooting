"""Bakes the reversed transformation clips the in-game replay plays when a
robot unfolds back out of vehicle form.

Reversing at runtime is not an option: Unity's VideoPlayer rejects a negative
playbackSpeed, and on WebGL the browser's <video> element ignores a negative
playbackRate outright. So every <robot>-transform.mp4 gets a pre-reversed
<robot>-transform-back.mp4 sitting next to it.

Both copies matter. Assets/Video is the source of truth (and what the roster
references); StreamingAssets is what actually ships to the browser, since
TransformCast streams every clip by URL.

  python make_reverse_clips.py [robot ...]      # default: all of them
"""
import os
import shutil
import subprocess
import sys

ROOT = "D:/Claude/FirstPersongShooting"
VIDEO = os.path.join(ROOT, "Assets/Video")
STREAMING = os.path.join(ROOT, "Assets/StreamingAssets")
FFMPEG = os.path.expanduser(
    "~/AppData/Local/Microsoft/WinGet/Packages/"
    "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/"
    "ffmpeg-8.1.1-full_build/bin/ffmpeg.exe"
)
SUFFIX = "-transform.mp4"


def sources(names):
    for f in sorted(os.listdir(VIDEO)):
        if not f.endswith(SUFFIX):
            continue
        robot = f[: -len(SUFFIX)]
        if not names or robot in names:
            yield robot, os.path.join(VIDEO, f)


def reverse(src, dst):
    # -an: the clips carry an audio track nothing plays, and reversed audio is
    # only ever a liability. The whole stream is buffered by the filter, which
    # is fine at ~120 frames.
    subprocess.run(
        [FFMPEG, "-y", "-v", "error", "-i", src, "-vf", "reverse", "-an",
         "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20",
         "-movflags", "+faststart", dst],
        check=True,
    )


def main(names):
    if not os.path.exists(FFMPEG):
        sys.exit(f"ffmpeg not found at {FFMPEG}")
    os.makedirs(STREAMING, exist_ok=True)

    found = 0
    for robot, src in sources(set(names)):
        found += 1
        dst = os.path.join(VIDEO, f"{robot}-transform-back.mp4")
        if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
            print(f"{robot}: up to date")
        else:
            print(f"{robot}: reversing {os.path.basename(src)}")
            reverse(src, dst)
        shutil.copyfile(dst, os.path.join(STREAMING, os.path.basename(dst)))

    if not found:
        print(f"No *{SUFFIX} clips in {VIDEO}")


if __name__ == "__main__":
    main(sys.argv[1:])

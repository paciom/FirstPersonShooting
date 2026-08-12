#!/usr/bin/env bash
# Generates FRAGMENTS: 512 episodes, VERSIONS takes each.
#
#   Tools/fragments_batch.sh 005 006 007
#
# Resumable on purpose: an episode whose output already exists is skipped, so
# a lane that dies partway through can simply be re-run, and a single bad take
# can be re-rolled by deleting just that file. Lanes are meant to run several
# at once — each one is sequential so a lane never floods the API on its own.
set -u
cd "$(dirname "$0")/.."

# Not `. .secrets/byteplus.env`: the file has CRLF line endings, which would
# leave a trailing \r inside the model id and the key.
export $(grep -v '^#' .secrets/byteplus.env | tr -d '\r' | xargs)

VERSIONS=${VERSIONS:-2}

for ep in "$@"; do
    d="Fragments512/ep$ep"
    if [ ! -f "$d/prompt.txt" ]; then
        echo "!! ep$ep has no prompt.txt, skipping"
        continue
    fi
    for v in $(seq 1 "$VERSIONS"); do
        out="$d/output/ep${ep}_v${v}.mp4"
        if [ -f "$out" ]; then
            echo "== ep$ep v$v already generated, skipping"
            continue
        fi
        echo "== ep$ep v$v starting"
        python Tools/seedance.py "$out" \
            --prompt-file "$d/prompt.txt" \
            --ref "$d/refs/robot.png" \
            --ref "$d/refs/angle_2.png" \
            --ref "$d/refs/angle_4.png" \
            --ref "$d/refs/angle_6.png" \
            --ref "$d/refs/cube_ref.png" \
            --seconds 30 --resolution 720p --ratio 9:16 \
            --model "$BYTEPLUS_VIDEO_MODEL" \
            || echo "!! FAILED ep$ep v$v"
    done
done
echo "LANE COMPLETE: $*"

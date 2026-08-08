#!/bin/bash
# Records the 20-battle series, one windowed Unity editor at a time.
# Idempotent: a battle whose mp4 already exists (and runs longer than five
# minutes) is skipped, so the batch can be re-run after any interruption.
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Unity.exe"
ROOT="D:/Claude/FirstPersongShooting"
LOGS="$ROOT/Tools/battle_batch/logs"
mkdir -p "$LOGS"

# name cyan magenta map — every robot appears, maps rotate.
BATTLES="
battle01 bolt panther airfield
battle02 hawk samurai planet
battle03 knight racer donut
battle04 scout titan airfield
battle05 ranger bolt planet
battle06 panther samurai donut
battle07 hawk knight airfield
battle08 racer scout planet
battle09 titan ranger donut
battle10 bolt hawk airfield
battle11 samurai knight planet
battle12 panther racer donut
battle13 scout ranger airfield
battle14 titan bolt planet
battle15 hawk panther donut
battle16 knight scout airfield
battle17 samurai racer planet
battle18 ranger hawk donut
battle19 bolt knight airfield
battle20 titan samurai planet
"

echo "$BATTLES" | while read -r name cyan magenta map; do
  [ -z "$name" ] && continue
  out="$ROOT/Renders/$name.mp4"
  if [ -f "$out" ]; then
    secs=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$out" 2>/dev/null | cut -d. -f1)
    if [ "${secs:-0}" -gt 300 ]; then
      echo "SKIP $name (already ${secs}s)"
      continue
    fi
    rm -f "$out"
  fi
  echo "RECORD $name: $cyan vs $magenta on $map  $(date +%H:%M:%S)"
  "$UNITY" -projectPath "$ROOT" -executeMethod DogfightRender.Render \
    -dogfightWar -dogfightSize 4 -dogfightRobots "$cyan,$magenta" \
    -dogfightMap "$map" -dogfightOut "$name" -logFile "$LOGS/$name.log"
  code=$?
  secs=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$out" 2>/dev/null | cut -d. -f1)
  echo "DONE $name exit=$code duration=${secs:-MISSING}s  $(date +%H:%M:%S)"
  if [ ! -f "$out" ] || [ "${secs:-0}" -le 300 ]; then
    echo "RETRY $name"
    rm -f "$out"
    "$UNITY" -projectPath "$ROOT" -executeMethod DogfightRender.Render \
      -dogfightWar -dogfightSize 4 -dogfightRobots "$cyan,$magenta" \
      -dogfightMap "$map" -dogfightOut "$name" -logFile "$LOGS/$name.retry.log"
    secs=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$out" 2>/dev/null | cut -d. -f1)
    echo "RETRY-DONE $name duration=${secs:-MISSING}s"
  fi
done
echo "BATCH COMPLETE $(date +%H:%M:%S)"

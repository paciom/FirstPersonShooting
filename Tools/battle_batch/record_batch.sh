#!/bin/bash
# Records the 20-battle series, one windowed Unity editor at a time.
# Idempotent: a battle whose mp4 already exists (longer than five minutes)
# is skipped, so the batch can be re-run after any interruption.
#
# Serialization is by CONDITIONS, not by process exit: a stale Unity worker
# holding the project lock makes the next editor show a modal "another
# instance is running" dialog and sit there forever (that burned the first
# run of this batch) — so before every launch the field is swept clean of
# Unity processes, each run gets a hard wall-clock watchdog, and success is
# judged by the mp4 existing, never by an exit code.
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Unity.exe"
ROOT="D:/Claude/FirstPersongShooting"
LOGS="$ROOT/Tools/battle_batch/logs"
WALL_LIMIT=5100   # seconds a single recording may hold the editor
mkdir -p "$LOGS"

unity_count() {
  tasklist //FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -c "^Unity.exe"
}

sweep_unity() {
  if [ "$(unity_count)" -gt 0 ]; then
    taskkill //IM Unity.exe //F >/dev/null 2>&1
  fi
  # Workers respawn briefly during shutdown; insist on a quiet field.
  for _ in 1 2 3 4 5 6; do
    sleep 10
    [ "$(unity_count)" -eq 0 ] && return 0
    taskkill //IM Unity.exe //F >/dev/null 2>&1
  done
}

mp4_seconds() {
  ffprobe -v error -show_entries format=duration -of csv=p=0 "$1" 2>/dev/null | cut -d. -f1
}

record_one() {  # name cyan magenta map logfile
  sweep_unity
  rm -f "$ROOT/Temp/UnityLockfile"
  echo "LAUNCH $1 $(date +%H:%M:%S)"
  "$UNITY" -projectPath "$ROOT" -executeMethod DogfightRender.Render \
    -dogfightWar -dogfightSize 4 -dogfightRobots "$2,$3" \
    -dogfightMap "$4" -dogfightOut "$1" -silent-crashes \
    -logFile "$5" < /dev/null &
  local pid=$! start=$(date +%s)
  while kill -0 "$pid" 2>/dev/null; do
    sleep 30
    if [ $(( $(date +%s) - start )) -gt "$WALL_LIMIT" ]; then
      echo "WALL LIMIT $1 — killing the editor"
      taskkill //IM Unity.exe //F >/dev/null 2>&1
    fi
  done
  sweep_unity
}

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
  secs=$(mp4_seconds "$out")
  if [ "${secs:-0}" -gt 300 ]; then
    echo "SKIP $name (already ${secs}s)"
    continue
  fi
  rm -f "$out"
  for attempt in 1 2; do
    record_one "$name" "$cyan" "$magenta" "$map" "$LOGS/$name.try$attempt.log"
    secs=$(mp4_seconds "$out")
    if [ "${secs:-0}" -gt 300 ]; then
      echo "OK $name duration=${secs}s attempt=$attempt $(date +%H:%M:%S)"
      break
    fi
    echo "FAIL $name attempt=$attempt duration=${secs:-MISSING}s"
    rm -f "$out"
  done
done
echo "BATCH COMPLETE $(date +%H:%M:%S)"

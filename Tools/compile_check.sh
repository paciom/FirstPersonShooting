#!/bin/bash
# Type-checks Assets/Scripts (+ Assets/Editor with -e) with the Roslyn inside
# the Unity install, in seconds, without opening the editor.
#
# The two traps this script exists to remember (see memory
# unity-compile-check-without-editor): csc flags must be -flag not /flag
# (MSYS rewrites /t into a path), and every path must pass through
# cygpath -m or csc reports the whole reference set as CS0006 missing.
set -e

UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor"
ROOT="D:/Claude/FirstPersongShooting"
CSC="$UNITY/Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
RSP="$(cygpath -m "$(mktemp)")"

{
  echo "-target:library"
  echo "-nologo"
  echo "-noconfig"
  echo "-nostdlib+"
  echo "-langversion:9.0"
  echo "-out:\"$(cygpath -m "$(mktemp -u)").dll\""

  for dll in "$UNITY"/Data/Managed/UnityEngine/*.dll \
             "$UNITY"/Data/NetStandard/ref/2.1.0/*.dll \
             "$UNITY"/Data/NetStandard/compat/2.1.0/shims/netstandard/*.dll; do
    echo "-reference:\"$(cygpath -m "$dll")\""
  done
  for dll in "$ROOT"/Library/ScriptAssemblies/*.dll; do
    case "$dll" in
      *Assembly-CSharp.dll|*Assembly-CSharp-Editor.dll) continue ;;
    esac
    echo "-reference:\"$(cygpath -m "$dll")\""
  done
  for dll in "$ROOT"/Library/Bee/artifacts/*.dag/post-processed/UnityEngine.UI.dll; do
    [ -f "$dll" ] && echo "-reference:\"$(cygpath -m "$dll")\""
  done

  find "$ROOT/Assets/Scripts" -name '*.cs' | while read -r src; do
    echo "\"$(cygpath -m "$src")\""
  done
  if [ "$1" = "-e" ]; then
    # Editor scripts additionally need the UnityEditor assemblies — the
    # MODULAR set only. Adding the monolithic UnityEditor.dll alongside it
    # duplicates every type (CS0433 on MenuItem, AnimatorController, ...).
    for dll in "$UNITY"/Data/Managed/UnityEditor/*.dll; do
      [ -f "$dll" ] && echo "-reference:\"$(cygpath -m "$dll")\""
    done
    find "$ROOT/Assets/Editor" -name '*.cs' | while read -r src; do
      echo "\"$(cygpath -m "$src")\""
    done
  fi
} > "$RSP"

dotnet "$CSC" "@$RSP" && echo "COMPILE OK"

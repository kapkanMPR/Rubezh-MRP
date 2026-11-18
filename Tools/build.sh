#!/usr/bin/env bash
set -euo pipefail
project="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-fast}"
if [[ "$mode" == check ]]; then
 exec python3 "$project/Tools/ProjectCheck/run.py"
fi
case "$mode" in fast) method=AndroidFast; flavor=Dev;; developer) method=AndroidDeveloper; flavor=Developer;; release) method=AndroidRelease; flavor=Release;; *) echo 'Use fast, release, developer or check' >&2; exit 2;; esac
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the Unity editor executable}"
if [[ -e "$project/Temp/UnityLockfile" ]]; then echo 'Project is open: use its Rubezh build menu or close the Editor.' >&2; exit 2; fi
mkdir -p "$project/Logs"
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$project" -buildTarget Android -executeMethod "Rubezh.UnityPort.Editor.FastBuild.$method" -logFile "$project/Logs/build.log"
apk="$(cat "$project/Validation/last-apk-path.txt")"
test -f "$apk"
printf 'APK: %s\n' "$apk"

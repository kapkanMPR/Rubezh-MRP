#!/usr/bin/env bash
set -euo pipefail

project="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
case "${1:-fast}" in
    check) exec python3 "$project/Tools/ProjectCheck/run.py" ;;
    fast) method=AndroidFast ;;
    developer) method=AndroidDeveloper ;;
    release) method=AndroidRelease ;;
    *) echo 'Use fast, release, developer or check.' >&2; exit 2 ;;
esac

: "${UNITY_EDITOR:?Set UNITY_EDITOR to the Unity editor executable}"
if [[ -e "$project/Temp/UnityLockfile" ]]; then
    echo 'Project is open. Use its Rubezh build menu or close the Editor.' >&2
    exit 2
fi

mkdir -p "$project/Logs"
log="$project/Logs/build.log"
: > "$log"
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$project" \
    -buildTarget Android -executeMethod "Rubezh.UnityPort.Editor.FastBuild.$method" \
    -logFile "$log"

# FastBuild emits this marker only after Unity reports a successful build.
apk="$(sed -n 's/^RUBEZH APK: //p' "$log" | tail -n 1)"
apk="${apk%$'\r'}"
if [[ -z "$apk" || ! -f "$apk" ]]; then
    echo "Unity finished without reporting a valid APK. See $log." >&2
    exit 1
fi
printf 'APK: %s\n' "$apk"

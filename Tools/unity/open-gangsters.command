#!/bin/zsh
set -eu

launcher_dir="${0:A:h}"
gangsters_root="${launcher_dir:h:h}"
gangsters_version="$(sed -n 's/^m_EditorVersion: //p' "$gangsters_root/ProjectSettings/ProjectVersion.txt")"
unity_bin="/Applications/Unity/Hub/Editor/$gangsters_version/Unity.app/Contents/MacOS/Unity"

if [[ ! -x "$unity_bin" ]]; then
    print -u2 "Unity $gangsters_version is not installed at: $unity_bin"
    exit 1
fi

if [[ -f "$gangsters_root/Temp/UnityLockfile" ]] && \
   pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectPath $gangsters_root" >/dev/null 2>&1; then
    print -u2 "Gangsters is already open. Close that Editor before using this launcher."
    exit 1
fi

# Unity's default 16 MiB graphics command ring is too small for the CoreDemo
# first-traverse renderer upload burst. 64 MiB is a bounded Editor-side safety
# margin; the recycler still limits composition and renderer attachment per frame.
exec "$unity_bin" \
    -projectPath "$gangsters_root" \
    -gfx-ring-buffer-size 67108864 \
    "$@"

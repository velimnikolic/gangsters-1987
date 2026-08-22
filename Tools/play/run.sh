#!/bin/bash
# Plays a scene headless and leaves a run behind: the trace, the logs, the pictures.
#
#   Tools/play/run.sh --scene Assets/Scenes/BlockDemo.unity --seconds 90 --out runs/001
#
# run.ps1's opposite number, for the machines that are not Windows: the same harness
# (GangstersTools.PlayHarness.Run), the same flags, the same output. It exists because
# the editor is a Mac editor here and the soak is not a thing that should only be
# runnable on one of the two.
#
# Unity holds the project with a lock, so the editor must be CLOSED while this runs.
# Nothing here touches the working tree: everything is written under --out.

set -u

SCENE="Assets/Scenes/BlockDemo.unity"
SECONDS_=90
OUT=""
STEP=0.0333
SAMPLE=0.1
WARM=3
SHOT=0
WALL=1200
TIMEOUT_MIN=30
NOGRAPHICS=0
SETS=()

while [ $# -gt 0 ]; do
    case "$1" in
        --scene)   SCENE="$2"; shift 2 ;;
        --seconds) SECONDS_="$2"; shift 2 ;;
        --out)     OUT="$2"; shift 2 ;;
        --step)    STEP="$2"; shift 2 ;;
        --sample)  SAMPLE="$2"; shift 2 ;;
        --warm)    WARM="$2"; shift 2 ;;
        --shot)    SHOT="$2"; shift 2 ;;
        --wall)    WALL="$2"; shift 2 ;;
        --timeout) TIMEOUT_MIN="$2"; shift 2 ;;
        --nographics) NOGRAPHICS=1; shift ;;
        # "Type.field=value", several joined by ';' - the ps1's own shape, so a soak
        # can pass one string either way round
        --set)     IFS=';' read -r -a parts <<< "$2"
                   for one in "${parts[@]}"; do
                       [ -n "$one" ] && SETS+=("$one")
                   done
                   shift 2 ;;
        *) echo "[run] unknown argument: $1" >&2; exit 2 ;;
    esac
done

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "$HERE/../.." && pwd)"

# --- which Unity: the one the project is stamped with
VERSION=$(head -n 1 "$PROJECT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}')
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
if [ ! -x "$UNITY" ]; then
    echo "[run] no Unity $VERSION at $UNITY" >&2
    exit 2
fi

# --- the project may be open in one editor only
if [ -f "$PROJECT/Temp/UnityLockfile" ] && pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null 2>&1; then
    echo "[run] the project is open in the Unity editor - close it, then run this again" >&2
    exit 2
fi

# NOT under the project's Temp: that is Unity's own scratch directory and it is emptied
# when the editor shuts down - which is exactly when a run has just finished writing its
# trace there.
if [ -z "$OUT" ]; then
    OUT="$HOME/Library/Application Support/gangsters-play/$(date +%Y%m%d-%H%M%S)"
fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
find "$OUT" -maxdepth 1 -type f -delete 2>/dev/null

LOG="$OUT/unity.log"

ARGS=(
    -batchmode -accept-apiupdate -silent-crashes
    -projectPath "$PROJECT"
    -logFile "$LOG"
    -executeMethod GangstersTools.PlayHarness.Run
    -hScene "$SCENE"
    -hOut "$OUT"
    -hSeconds "$SECONDS_"
    -hStep "$STEP"
    -hSample "$SAMPLE"
    -hWarm "$WARM"
    -hShot "$SHOT"
    -hWall "$WALL"
)
[ "$NOGRAPHICS" = "1" ] && ARGS+=(-nographics)
for one in ${SETS+"${SETS[@]}"}; do ARGS+=(-hSet "$one"); done

echo "[run] $VERSION  $SCENE  ${SECONDS_}s  -> $OUT"
STARTED=$(date +%s)

"$UNITY" "${ARGS[@]}" &
PID=$!
# no `timeout` on a stock macOS, so the wait is counted here
DEADLINE=$(( STARTED + TIMEOUT_MIN * 60 ))
CODE=0
while kill -0 "$PID" 2>/dev/null; do
    if [ "$(date +%s)" -gt "$DEADLINE" ]; then
        echo "[run] no end after $TIMEOUT_MIN minutes - killing it"
        kill -9 "$PID" 2>/dev/null
        wait "$PID" 2>/dev/null
        CODE=124
        break
    fi
    sleep 2
done
if [ "$CODE" != "124" ]; then
    wait "$PID"
    CODE=$?
fi
TOOK=$(( $(date +%s) - STARTED ))

echo "[run] exit $CODE after ${TOOK}s"
if [ -f "$LOG" ]; then
    ERRORS=$(grep -E "error CS|Exception:|Fatal|Aborting" "$LOG" 2>/dev/null | head -20)
    if [ -n "$ERRORS" ]; then
        echo "[run] from the editor log:"
        echo "$ERRORS" | sed 's/^/   /'
    fi
fi
if [ -f "$OUT/summary.json" ]; then
    echo "[run] $(cat "$OUT/summary.json")"
else
    echo "[run] no summary was written"
fi
exit $CODE

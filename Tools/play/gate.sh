#!/bin/bash
# Has the work landed? One exit code for the whole verdict, so a loop can stop on it.
#
#   Tools/play/gate.sh                          the last fast-circle run
#   Tools/play/gate.sh --run DIR --soak DIR     a named run and a named soak
#
# 0 = the compile is clean, the run has no defect, and the soak (if one is named) had no
# failing run. Anything else = there is still work. Nothing here judges prose: every check
# is a program's own exit code or its own word for itself.

set -u

RUN="Temp/play/loop"
SOAK=""

while [ $# -gt 0 ]; do
    case "$1" in
        --run)  RUN="$2";  shift 2 ;;
        --soak) SOAK="$2"; shift 2 ;;
        *) echo "[gate] unknown argument: $1" >&2; exit 2 ;;
    esac
done

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

# The compile, from the editor that is open. With no editor up this cannot be answered, and
# an unanswerable gate is a closed gate.
STATUS="$(unity command recompile_status --json 2>&1)" || {
    echo "[gate] no compile verdict: $STATUS"; exit 1; }
# The verdict arrives as a JSON string inside the envelope's "result", so its quotes come
# back escaped. Drop the backslashes and the two shapes read the same.
case "${STATUS//\\/}" in
    *'"failed":false'*) echo "[gate] compile clean" ;;
    *) echo "[gate] compile FAILED"; echo "$STATUS" | grep -o '"errors".*' | head -3; exit 1 ;;
esac

# The fast circle: one run, judged the way the soak judges every run.
if [ ! -d "$ROOT/$RUN" ] && [ ! -d "$RUN" ]; then
    echo "[gate] no run at '$RUN'"; exit 1
fi
[ -d "$RUN" ] || RUN="$ROOT/$RUN"
python3 "$HERE/analyze.py" "$RUN" --verdict || { echo "[gate] the run has a defect"; exit 1; }

# Thirty in a row, if one has been run. soak.sh writes its tally here and exits non-zero
# itself, but the loop reads the file: the soak may have been run in another session.
if [ -n "$SOAK" ]; then
    LEDGER="$SOAK/soak.txt"
    [ -f "$LEDGER" ] || { echo "[gate] no soak tally at '$LEDGER'"; exit 1; }
    TALLY="$(grep '^== ' "$LEDGER" | tail -1)"
    echo "[gate] $TALLY"
    case "$TALLY" in
        *"the ones that did not"*) echo "[gate] the soak has failures"; exit 1 ;;
        "") echo "[gate] the soak never finished"; exit 1 ;;
    esac
fi

echo "[gate] PASSED"

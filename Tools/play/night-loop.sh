#!/bin/bash
# The Night Watch's outer loop (EPIC 31, GAN-280). One `claude -p` per iteration, each with a
# fresh context; the ledger on disk is the only memory. Runs until the ledger's last line
# says NIGHT DONE, or MAX iterations have gone by.
#
#   Tools/play/night-loop.sh                 start it and walk away
#   Tools/play/night-loop.sh --max 50        fewer iterations
#
# Before starting: the Unity editor OPEN on a saved scene (no dirty scene), no other Claude
# session on this checkout, `unity status` says ready.
set -u

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
NIGHT="$HOME/Library/Application Support/gangsters-play/night-2026-09-03"
LEDGER="$NIGHT/ledger.md"
MAX=400
while [ $# -gt 0 ]; do
    case "$1" in
        --max) MAX="$2"; shift 2 ;;
        *) echo "[night] unknown argument: $1" >&2; exit 2 ;;
    esac
done

mkdir -p "$NIGHT/loop"
cd "$ROOT" || exit 2

PROMPT="Work Gangsters 1987 EPIC 31 (the Night Watch, Linear GAN-280) top to bottom by the rules \
in Docs/design-briefs/night-watch-brief.md and the epic's own text. Read the ledger at \
'$LEDGER' FIRST; its STATE line overrides anything you remember. If the ledger does not exist, \
you are at NIGHT-000 pre-flight: create it. Do the next thing the ledger says is undone, write \
every step to the ledger as you go (a pass, a fix, a review, a commit, a restart, with the \
time), and stop when a ticket changes state or after one soak/review has finished. Never wait \
on a clock: long commands run in the background and you wait on their exit. Write the line \
NIGHT DONE as the ledger's last line only when NIGHT-012's REPORT.md is written and every \
ticket is Done or written up as open."

i=0
while [ "$i" -lt "$MAX" ]; do
    if [ -f "$LEDGER" ] && tail -n 3 "$LEDGER" | grep -q 'NIGHT DONE'; then
        echo "[night] NIGHT DONE after $i iterations"; exit 0
    fi
    i=$(( i + 1 ))
    LOG=$(printf "%s/loop/iter-%03d.log" "$NIGHT" "$i")
    echo "[night] iteration $i  $(date '+%H:%M:%S')  -> $LOG"
    claude -p "$PROMPT" --dangerously-skip-permissions > "$LOG" 2>&1
    CODE=$?
    tail -n 5 "$LOG" | sed 's/^/   /'
    if [ "$CODE" != "0" ]; then
        echo "[night] iteration $i exited $CODE; waiting 60 s"
        sleep 60
    else
        sleep 5
    fi
done
echo "[night] stopped at $MAX iterations without NIGHT DONE"
exit 1

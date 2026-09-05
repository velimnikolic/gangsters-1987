#!/usr/bin/env bash
# The fix cycle's second half: make the open editor compile what is on disk, and wait
# for a verdict that is actually about THIS edit.
#
#     Tools/play/recompile.sh
#
# Prints "COMPILED" or "FAILED" plus the error lines, and exits 0 or 1.
#
# Three things this has to get right, and each of them was got wrong first:
#
#   * A domain reload drops the pipeline server for a few seconds, so `unity status` is
#     polled back to ready before any answer is read - without that the poll reads a
#     dead port and calls a good compile a failure.
#   * The TRIGGER's own answer is read. recompile_status keeps only the LAST status the
#     editor recorded, with no request id on it, so a trigger that never landed would
#     let this accept a `completed` left over from an earlier compile.
#   * `up_to_date` is not taken on trust. The command refreshes the AssetDatabase
#     itself, and a refresh can consume the edit and compile it before the status is
#     ever asked for - which means `up_to_date` can sit on top of a compile that
#     FAILED. So it is only accepted after the editor's own console has been read from
#     the moment of the trigger and shown to hold no `error CS`.
set -uo pipefail

# This flag records task-specific user permission; agents may not grant it to themselves.
if [ "${1:-}" != "--allow-unity" ]; then
    echo "UNVERIFIED: requires explicit user permission for Unity in this task and --allow-unity" >&2
    exit 2
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

wait_ready() {
    for _ in $(seq 1 60); do
        if unity status 2>/dev/null | grep -q "ready"; then return 0; fi
        sleep 2
    done
    return 1
}

# Every compiler error the console has seen since sequence $1, or nothing. A console
# that will not answer, or an answer that will not parse, is NOT a clean console: it is
# an UNREAD one, and consoleread.py exits non-zero for it so that reads as a failure.
compile_errors_since() {
    local answer
    answer=$(unity command console --tail 400 --level error --json 2>/dev/null)
    if ! printf '%s' "$answer" | python3 "$HERE/consoleread.py" "$1"; then
        echo "the console could not be read, so no compile could be proved clean"
    fi
}

# THE MARK GOES DOWN FIRST, before anything is waited for. Unity watches the project
# folder and compiles an edit on its own the moment it regains focus, so a mark taken
# AFTER waiting for ready can sit on the far side of the very errors it is meant to
# catch - and the compile that failed would be invisible.
MARK=$(unity command console --tail 1 --json 2>/dev/null | python3 "$HERE/consoleread.py" --mark)
case "$MARK" in ''|*[!0-9]*) MARK=0 ;; esac

wait_ready || { echo "FAILED: the editor never came back to ready"; exit 1; }

# UNITY DOES NOT COMPILE WHILE IT IS PLAYING. The request is taken and deferred, and
# `recompile_status` then answers about the LAST compile - which is `completed` or
# `up_to_date` from before the edit. That is not a hypothetical: a killed soak left the
# editor in Play, three recompiles in a row said COMPILED, and the fix under test was
# still not in the assemblies (the exception it fixed came back with its OLD line
# numbers). So Play is stopped first, and if it will not stop this fails.
PLAY=$(unity command editor_status --json 2>/dev/null | python3 "$HERE/suiteread.py" --playmode)
if [ "$PLAY" != "stopped" ]; then
    unity command editor_stop >/dev/null 2>&1
    for _ in $(seq 1 20); do
        sleep 2
        PLAY=$(unity command editor_status --json 2>/dev/null | python3 "$HERE/suiteread.py" --playmode)
        [ "$PLAY" = "stopped" ] && break
    done
fi
if [ "$PLAY" != "stopped" ]; then
    echo "FAILED: the editor is in play mode ($PLAY) and will not compile there"
    exit 1
fi

TRIGGER=$(unity command recompile --focus "${RECOMPILE_FOCUS:-false}" --json 2>&1)
if ! printf '%s' "$TRIGGER" | grep -q '"success": true'; then
    echo "FAILED: the editor would not take a recompile"
    printf '%s\n' "$TRIGGER" | tail -5
    exit 1
fi

STEADY=0
for _ in $(seq 1 90); do
    sleep 4
    wait_ready || continue
    STATUS=$(unity command recompile_status --json 2>/dev/null | python3 "$HERE/statusread.py")
    case "$STATUS" in
        completed*)
            ERRORS=$(compile_errors_since "$MARK")
            if [ -n "$ERRORS" ]; then
                echo "FAILED"; printf '%s\n' "$ERRORS" | head -20; exit 1
            fi
            echo "COMPILED"; exit 0 ;;
        failed*)
            echo "FAILED"; printf '%s\n' "${STATUS#failed:}" | head -20; exit 1 ;;
        up_to_date*)
            STEADY=$(( STEADY + 1 ))
            if [ "$STEADY" -ge 3 ]; then
                ERRORS=$(compile_errors_since "$MARK")
                if [ -n "$ERRORS" ]; then
                    echo "FAILED"; printf '%s\n' "$ERRORS" | head -20; exit 1
                fi
                echo "COMPILED (up_to_date held after the trigger, console clean)"
                exit 0
            fi ;;
        *)  STEADY=0 ;;
    esac
done

echo "FAILED: the compile never answered inside the deadline"
exit 1

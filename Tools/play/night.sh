#!/usr/bin/env bash
# THE NIGHT WATCH's own driver (EPIC 31, Docs/design-briefs/night-watch-brief.md).
#
# soak.sh runs the batch harness with the editor CLOSED, one fresh Unity a run. This one
# runs everything through the OPEN editor over the pipeline port, which is the user's
# ruling for this epic: the editor stays up all night and answers the terminal.
#
#     Tools/play/night.sh --suites gangsters_wage_tests,gangsters_economy_tests --passes 5
#     Tools/play/night.sh --smoke                       120 s of MiniCoreDemo, seed 1
#     Tools/play/night.sh --brawl --runs 5 --seed 101   a soak mode on the mini core
#     Tools/play/night.sh --cover --runs 5              cover and the ambush on CoverDemo
#     Tools/play/night.sh --court --runs 5              ROAD-006: all 12 cases x 5 seeds
#     Tools/play/night.sh --court --scenario 8 --runs 1 one reproducible court case
#
# Exit codes are analyze.py's, so a caller reads them the same way soak.sh's caller does:
#   0 every pass green, 1 something failed, 3 nothing ran (the editor refused to play).
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "$HERE/../.." && pwd)"
PYTHON_BIN="${PYTHON_BIN:-python3}"

MODE=""
SUITES=""
PASSES=5
RUNS=5
FIRST_SEED=101
SECONDS_=""
OUT=""
SETS=""
SCENE=""
COURT_SCENARIO=0
# The brief's floor for cover first over a whole soak (EPIC 31 NIGHT-010; 85 % was the
# last reading over thirty runs).
COVER_FLOOR=80

while [ $# -gt 0 ]; do
    case "$1" in
        --suites)  MODE="suites"; SUITES="$2"; shift 2 ;;
        --passes)  PASSES="$2"; shift 2 ;;
        --runs)    RUNS="$2"; shift 2 ;;
        --seed)    FIRST_SEED="$2"; shift 2 ;;
        --seconds) SECONDS_="$2"; shift 2 ;;
        --out)     OUT="$2"; shift 2 ;;
        --sets)    SETS="$2"; shift 2 ;;
        --scene)   SCENE="$2"; shift 2 ;;
        --scenario) COURT_SCENARIO="$2"; shift 2 ;;
        --smoke)   MODE="smoke"; shift ;;
        --car)     MODE="car"; shift ;;
        --moto)    MODE="moto"; shift ;;
        --roadblock) MODE="roadblock"; shift ;;
        --walk)    MODE="walk"; shift ;;
        --brawl)   MODE="brawl"; shift ;;
        --cover)   MODE="cover"; shift ;;
        --ambush)  MODE="ambush"; shift ;;
        --court)   MODE="court"; shift ;;
        --core-s1) MODE="core-s1"; shift ;;
        --core-s2) MODE="core-s2"; shift ;;
        --core-s3) MODE="core-s3"; shift ;;
        --core-s4) MODE="core-s4"; shift ;;
        --core-s5) MODE="core-s5"; shift ;;
        *) echo "[night] unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ -z "$MODE" ]; then
    echo "[night] nothing asked for: give --suites, --smoke, or a soak mode" >&2
    exit 2
fi

PLAY_DATA_ROOT="${LOCALAPPDATA:-${TMPDIR:-/tmp}}"
NIGHT_ROOT="${NIGHT_ROOT:-$PLAY_DATA_ROOT/gangsters-play/night-2026-09-03}"
[ -z "$OUT" ] && OUT="$NIGHT_ROOT/$MODE-$(date +%H%M%S)"
mkdir -p "$OUT"
LEDGER="$OUT/soak.txt"

# --------------------------------------------------------------- the pure suites
#
# FIVE PASSES IN ONE PROCESS. The editor is one long-lived process, so calling a
# suite five times over the port is exactly the check the brief asks for: a suite
# that is green once and red on the third call is a static leaking between runs,
# and that is a real bug here rather than a flake to be re-rolled.
if [ "$MODE" = "suites" ]; then
    FAILED=""
    RAN=0
    IFS=',' read -r -a LIST <<< "$SUITES"
    # NOTHING RAN IS NOT EVERYTHING PASSED. An empty --suites, a --passes of 0 or a
    # typo for one runs no commands at all, leaves FAILED empty, and used to print
    # "5 of 5 passes green" over nothing whatever.
    WANTED=0
    for cmd in "${LIST[@]}"; do [ -n "$cmd" ] && WANTED=$(( WANTED + 1 )); done
    case "$PASSES" in ''|*[!0-9]*) echo "[night] --passes must be a positive whole number" >&2; exit 2 ;; esac
    if [ "$WANTED" -eq 0 ] || [ "$PASSES" -eq 0 ]; then
        echo "[night] nothing to run: $WANTED suite(s) x $PASSES pass(es)" >&2
        exit 2
    fi
    for pass in $(seq 1 "$PASSES"); do
        for cmd in "${LIST[@]}"; do
            [ -z "$cmd" ] && continue
            RAN=$(( RAN + 1 ))
            ANSWER=$(unity command "$cmd" --json 2>&1)
            VERDICT=$(printf '%s' "$ANSWER" | "$PYTHON_BIN" "$HERE/suiteread.py" 2>/dev/null)
            [ -z "$VERDICT" ] && VERDICT="UNREADABLE"
            LINE="pass $pass/$PASSES $cmd: $VERDICT"
            echo "$LINE"
            echo "$LINE" >> "$LEDGER"
            case "$VERDICT" in
                PASSED) ;;
                *) FAILED="$FAILED $cmd(pass $pass)"
                   printf '%s\n' "$ANSWER" > "$OUT/$cmd-pass$pass.json" ;;
            esac
        done
    done
    if [ -n "$FAILED" ]; then
        echo "== FAILED:$FAILED"
        echo "== FAILED:$FAILED" >> "$LEDGER"
        exit 1
    fi
    if [ "$RAN" -ne $(( WANTED * PASSES )) ]; then
        echo "== $RAN calls made, $(( WANTED * PASSES )) expected: NOT a green run"
        echo "== $RAN calls made, $(( WANTED * PASSES )) expected: NOT a green run" >> "$LEDGER"
        exit 1
    fi
    echo "== $PASSES of $PASSES passes green over $WANTED suite(s) ($RAN calls)"
    echo "== $PASSES of $PASSES passes green over $WANTED suite(s) ($RAN calls)" >> "$LEDGER"
    exit 0
fi

# ------------------------------------------------------------------- a played run
#
# gangsters_play returns at once and the run is over when summary.json appears. An
# eval would be the obvious way to watch it and is the one thing that cannot be used
# here (the port's main-thread call gives up at five seconds), so the wait is a file
# poll, the same pattern tally.sh uses.
stop_play() {
    unity command editor_stop >/dev/null 2>&1
    for _ in $(seq 1 30); do
        STATE=$(unity command editor_status --json 2>/dev/null | "$PYTHON_BIN" "$HERE/suiteread.py" --playmode 2>/dev/null)
        [ "$STATE" = "stopped" ] && return 0
        sleep 2
    done
    return 1
}

# gangsters_play opens its scene SINGLE, so an unattended run replaces whatever is in
# the editor - and unsaved work in it is gone. The night refuses to be the thing that
# threw a morning's editing away; NIGHT_ALLOW_DIRTY=1 says the user has given it leave.
dirty_scene() {
    unity command list_open_scenes --json 2>/dev/null | grep -q '"isDirty": true'
}

play_and_wait() {   # scene seconds sets dir wallcap
    local scene="$1" secs="$2" sets="$3" dir="$4" cap="$5"
    rm -rf "$dir"; mkdir -p "$dir"
    stop_play
    unity command gangsters_play --scene "$scene" --seconds "$secs" --step 0.05 \
        --out "$dir" --sets "$sets" >"$dir/launch.txt" 2>&1
    local waited=0
    while [ "$waited" -lt "$cap" ]; do
        [ -f "$dir/summary.json" ] && return 0
        sleep 5
        waited=$(( waited + 5 ))
    done
    stop_play
    return 3
}

case "$MODE" in
    smoke)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-120}"
        SETS="${SETS:-CoreDemoBuilder.seed=$FIRST_SEED;CoreDemoBuilder.newSeedEveryPlay=0;CoreDemoBuilder.realSecondsPerGameHour=5}"
        RUNS=1
        VERDICT_FLAG="--verdict"
        ;;
    cover)
        SCENE="${SCENE:-Assets/Scenes/CoverDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoverDemoBuilder.layoutSeed"
        SETS="${SETS:-CoverDemoBuilder.missionAfter=12;CoverDemoBuilder.rivalCrews=2;CoverDemoBuilder.rivalHoods=3;CoverDemoBuilder.outfitCrews=2}"
        VERDICT_FLAG="--crew"
        ;;
    ambush)
        SCENE="${SCENE:-Assets/Scenes/CoverDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoverDemoBuilder.layoutSeed"
        SETS="${SETS:-CoverDemoBuilder.missionAfter=12;CoverDemoBuilder.ambushRun=1;CoverDemoBuilder.rivalCrews=1;CoverDemoBuilder.rivalHoods=3;CoverDemoBuilder.outfitCrews=3}"
        VERDICT_FLAG="--crew"
        ;;
    court)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-600}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.courtTransferAfter=10;CoreDemoBuilder.courtTransferPatience=570;CoreDemoBuilder.rivalCrews=0;CoreDemoBuilder.outfitLieutenants=3;CoreDemoBuilder.outfitHoods=3;CoreDemoBuilder.mixedArms=1;CoreDemoBuilder.policeCars=8;CoreDemoBuilder.policeOfficers=16;CoreDemoBuilder.policeBeatPairs=2;CoreDemoBuilder.carCount=24}"
        VERDICT_FLAG="--court"
        ;;
    brawl)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.missionOnFoot=1;CoreDemoBuilder.rivalCrews=3;CoreDemoBuilder.rivalHoods=4;CoreDemoBuilder.carCount=20;CoreDemoBuilder.outfitLieutenants=2;CoreDemoBuilder.outfitHoods=4;CoreDemoBuilder.mixedArms=1;CoreDemoBuilder.panicChance=0.8}"
        VERDICT_FLAG="--crew"
        ;;
    walk)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-1500}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.missionWalk=1;CoreDemoBuilder.rivalCrews=0;CoreDemoBuilder.carCount=20;CoreDemoBuilder.outfitLieutenants=2;CoreDemoBuilder.outfitHoods=4}"
        VERDICT_FLAG="--crew"
        ;;
    car)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.rivalCrews=1;CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.carCount=20;CoreDemoBuilder.rivalHoods=1;CoreDemoBuilder.missionPasses=30}"
        VERDICT_FLAG="--verdict"
        ;;
    roadblock)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.rivalCrews=1;CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.carCount=20;CoreDemoBuilder.rivalHoods=3;CoreDemoBuilder.missionPasses=120;CoreDemoBuilder.missionRoadblock=1}"
        VERDICT_FLAG="--verdict"
        ;;
    # THE FORCED SCENARIOS (EPIC 31 NIGHT-013). The night does not wait for a scenario
    # to happen by chance; it sets the mini core up so it MUST happen, and analyze.py
    # --core is told in words what the scenario promised. The clock runs at 5 real
    # seconds to the game hour throughout, so a game day is 120 sim-seconds.
    core-s3)   # does the AI spread the way we do, with nobody at the mouse
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-840}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.realSecondsPerGameHour=5;CoreDemoBuilder.rivalCrews=20;CoreDemoBuilder.rivalHoods=3;CoreDemoBuilder.mindThinkEveryHours=1;CoreDemoBuilder.carCount=12;CoreDemoBuilder.pedestrianCount=20}"
        VERDICT_FLAG="--core expand,turf,allthink"
        ;;
    core-s4)   # no police at all: does the ladder reach a war
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-840}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.realSecondsPerGameHour=5;CoreDemoBuilder.police=0;CoreDemoBuilder.rivalCrews=20;CoreDemoBuilder.rivalHoods=4;CoreDemoBuilder.mindThinkEveryHours=1;CoreDemoBuilder.carCount=12;CoreDemoBuilder.pedestrianCount=20}"
        VERDICT_FLAG="--core war"
        ;;
    core-s5)   # the broke player: the envelope comes up short and men walk
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-360}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.realSecondsPerGameHour=5;CoreDemoBuilder.playerSafeAtStart=0;CoreDemoBuilder.outfitLieutenants=2;CoreDemoBuilder.outfitHoods=3;CoreDemoBuilder.rivalCrews=3;CoreDemoBuilder.carCount=8;CoreDemoBuilder.pedestrianCount=20}"
        VERDICT_FLAG="--core short"
        ;;
    core-s2)   # a ton of police, and a brawl for them to turn out to
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.realSecondsPerGameHour=5;CoreDemoBuilder.policeCars=12;CoreDemoBuilder.policeOfficers=12;CoreDemoBuilder.policeBeatPairs=12;CoreDemoBuilder.rivalCrews=20;CoreDemoBuilder.rivalHoods=4;CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.missionOnFoot=1;CoreDemoBuilder.outfitLieutenants=2;CoreDemoBuilder.outfitHoods=4;CoreDemoBuilder.mixedArms=1;CoreDemoBuilder.carCount=20}"
        VERDICT_FLAG="--core police"
        ;;
    core-s1)   # every owner rings: needs NIGHT-009's shakedown mission and its rows
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-480}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.realSecondsPerGameHour=5;CoreDemoBuilder.ownerTraitOverride=Connected;CoreDemoBuilder.policeCars=6;CoreDemoBuilder.policeOfficers=6;CoreDemoBuilder.rivalCrews=20;CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.missionOnFoot=1;CoreDemoBuilder.outfitLieutenants=2;CoreDemoBuilder.outfitHoods=4}"
        VERDICT_FLAG="--core law,police"
        ;;
    moto)
        SCENE="${SCENE:-Assets/Scenes/MiniCoreDemo.unity}"
        SECONDS_="${SECONDS_:-900}"
        SEED_FIELD="CoreDemoBuilder.seed"
        SETS="${SETS:-CoreDemoBuilder.rivalCrews=2;CoreDemoBuilder.rivalHoods=1;CoreDemoBuilder.carCount=20;CoreDemoBuilder.missionAfter=15;CoreDemoBuilder.missionMoto=1;CoreDemoBuilder.missionPassesRidden=3;CoreDemoBuilder.outfitMotorcycle=Motorbike;CoreDemoBuilder.outfitLieutenants=1;CoreDemoBuilder.outfitHoods=4;CoreDemoBuilder.mixedArms=1}"
        VERDICT_FLAG="--moto"
        ;;
esac

# MiniCoreDemo is deliberately serialized at NPC-001's visual stress density
# (400 pedestrians / 120 cars). Unattended regression modes keep the historical
# 100 / 40 baseline unless a caller names either setting explicitly.
append_core_default() { # field value
    case ";$SETS;" in
        *";$1="*) ;;
        *) SETS="${SETS:+$SETS;}$1=$2" ;;
    esac
}
case "$SCENE" in
    *MiniCoreDemo*)
        append_core_default CoreDemoBuilder.pedestrianCount 100
        append_core_default CoreDemoBuilder.carCount 40
        ;;
esac

# THE FIXED SEED, ALWAYS. The core deals a fresh city every Play unless it is told
# not to, and a soak whose city changes under it compares nothing with nothing.
CORE_FIX=""
case "$SCENE" in
    *MiniCoreDemo*|*CoreDemo*) CORE_FIX=";CoreDemoBuilder.newSeedEveryPlay=0" ;;
esac

# A run is given three times its own sim length in wall clock, and never less than
# fifteen minutes: the core is a big city and its first frame is not cheap.
WALL=$("$PYTHON_BIN" -c "import sys; print(max(900, int(float(sys.argv[1]) * 3)))" "$SECONDS_")

# ASKED ONCE, BEFORE THE FIRST RUN, AND NEVER AGAIN. The harness marks the builder
# dirty as it writes its own -hSet overrides, so every run after the first leaves the
# scene dirty by construction - checking per run would refuse the whole soak on the
# strength of its own first run. What this protects is the morning's unsaved editing,
# and that is a question about the state the night STARTED in.
if [ "${NIGHT_ALLOW_DIRTY:-0}" != "1" ] && dirty_scene; then
    echo "[night] a loaded scene has unsaved changes - refusing to play over it." >&2
    echo "[night] save it, or re-run with NIGHT_ALLOW_DIRTY=1 to discard it." >&2
    exit 3
fi

# A MODE THAT ORDERS A MISSION MUST SHOW ITS MISSION. An -hSet override that never
# reached the builder plays an ordinary session, and an ordinary session's trace is
# one every quality verdict is happy with - so the gate asks for the mission's own
# rows before any verdict is read (Tools/play/rungate.py).
#
# WHICH MODES THOSE ARE IS THE MODE'S BUSINESS, not the settings string's. A --sets of
# the caller's own REPLACES the mode's defaults, so reading the text for missionAfter
# let `--car --sets "CoreDemoBuilder.carCount=20"` build no mission and drop the gate
# in the same breath, and the ambient traffic then passed for a car mission.
GATE_ARGS=""
case "$MODE" in
    car|roadblock|moto|walk|brawl|cover|ambush|court|core-s1|core-s2) GATE_ARGS="--mission" ;;
esac

COURT_CASES="0"
TOTAL_RUNS="$RUNS"
if [ "$MODE" = "court" ]; then
    case "$RUNS" in
        ''|*[!0-9]*) echo "[night] --runs must be a positive whole number" >&2; exit 2 ;;
    esac
    [ "$RUNS" -gt 0 ] || { echo "[night] --runs must be greater than zero" >&2; exit 2; }
    case "$COURT_SCENARIO" in
        ''|*[!0-9]*) echo "[night] --scenario must be a whole number from 0 to 12" >&2; exit 2 ;;
    esac
    [ "$COURT_SCENARIO" -le 12 ] || {
        echo "[night] --scenario must be from 0 (all) to 12" >&2
        exit 2
    }
    if [ "$COURT_SCENARIO" -eq 0 ]; then
        COURT_CASES="$(seq 1 12)"
        TOTAL_RUNS=$(( RUNS * 12 ))
    else
        COURT_CASES="$COURT_SCENARIO"
    fi
fi

PASSED=0
FAILED=""
SKIPPED=""
JOB=0
for court_case in $COURT_CASES; do
for i in $(seq 1 "$RUNS"); do
    JOB=$(( JOB + 1 ))
    if [ "$MODE" = "smoke" ]; then
        SEED="$FIRST_SEED"
        RUN_SETS="$SETS"
    else
        # --seed S RUNS SEED S. It used to start at S+1 (soak.sh's own off-by-one),
        # so re-running a reported failure on its own seed quietly ran the next one.
        SEED=$(( FIRST_SEED + i - 1 ))
        RUN_SETS="$SETS;$SEED_FIELD=$SEED$CORE_FIX"
    fi
    RUN_VERDICT_FLAG="$VERDICT_FLAG"
    RUN_ID="$i"
    if [ "$MODE" = "court" ]; then
        RUN_SETS="$RUN_SETS;CoreDemoBuilder.courtTransferScenario=$court_case"
        RUN_VERDICT_FLAG="$VERDICT_FLAG $court_case"
        DIR=$(printf "%s/scenario-%02d/run-%02d" "$OUT" "$court_case" "$i")
        RUN_ID=$(printf "s%02dr%02d" "$court_case" "$i")
    else
        DIR=$(printf "%s/run-%02d" "$OUT" "$i")
    fi

    # A RUN THAT NEVER RAN IS RUN AGAIN, ONCE, ON THE SAME SEED. Unity refusing to
    # play says nothing about the city; a seed quietly dropped from a five says
    # nothing either, and that is worse. One retry, then it counts against the tally.
    WORD=""; VERDICT=""
    for attempt in 1 2; do
        play_and_wait "$SCENE" "$SECONDS_" "$RUN_SETS" "$DIR" "$WALL"
        RAN=$?
        if [ "$RAN" != "0" ]; then
            WORD="NO RUN"; VERDICT="summary.json never appeared within ${WALL}s"
            continue
        fi
        GATE=$("$PYTHON_BIN" "$HERE/rungate.py" "$DIR" $GATE_ARGS --sets "$RUN_SETS" 2>&1)
        GATECODE=$?
        if [ "$GATECODE" = "3" ]; then
            WORD="NO RUN"; VERDICT="$GATE"
            continue
        fi
        if [ "$GATECODE" != "0" ]; then
            WORD="FAILED"; VERDICT="$GATE"
            break
        fi
        VERDICT=$("$PYTHON_BIN" "$HERE/analyze.py" "$DIR" $RUN_VERDICT_FLAG 2>&1)
        CODE=$?
        if [ "$CODE" = "3" ]; then WORD="NO RUN"; continue; fi
        if [ "$CODE" = "0" ]; then WORD="PASSED"; else WORD="FAILED"; fi
        break
    done

    case "$WORD" in
        PASSED) PASSED=$(( PASSED + 1 )) ;;
        FAILED) FAILED="$FAILED $RUN_ID" ;;
        *)      SKIPPED="$SKIPPED $RUN_ID" ;;
    esac

    if [ "$MODE" = "court" ]; then
        LINE=$(printf "job %2d/%d scenario %02d run %d/%d seed %s: %s" \
            "$JOB" "$TOTAL_RUNS" "$court_case" "$i" "$RUNS" "$SEED" "$WORD")
    else
        LINE=$(printf "run %2d/%d seed %s: %s" "$i" "$RUNS" "$SEED" "$WORD")
    fi
    HEAD=$(printf '%s' "$VERDICT" | head -8)
    echo "$LINE"; echo "$HEAD"
    printf "%s\n%s\n" "$LINE" "$HEAD" > "$DIR/verdict.txt"
    printf "%s\n%s\n\n" "$LINE" "$HEAD" >> "$LEDGER"
done
done

TALLY="== $PASSED of $TOTAL_RUNS passed"
[ -n "$FAILED" ] && TALLY="$TALLY; the ones that did not:$FAILED"
[ -n "$SKIPPED" ] && TALLY="$TALLY; never ran:$SKIPPED"
echo "$TALLY"; echo "$TALLY" >> "$LEDGER"

# EPIC 28's number over the whole soak, and the brief's floor under it: the men whose
# FIRST round left from behind something. A cover soak that fires from the open is a
# cover soak that failed, however clean its faults are.
COVER_SHORT=""
if [ "$MODE" = "cover" ] || [ "$MODE" = "ambush" ]; then
    COVER_TALLY=$("$PYTHON_BIN" "$HERE/analyze.py" "$OUT" --cover-tally 2>&1)
    echo "$COVER_TALLY"; echo "$COVER_TALLY" >> "$LEDGER"
    # THE COUNTS, NOT THE PRINTED PERCENTAGE. analyze.py rounds - 39 of 49 prints as
    # 80% and would clear an 80% floor at 79.6% - and a tally that measured nobody
    # prints no percentage at all, which read as "no reason to fail". Both are read
    # off the ratio itself, and a soak that measured nobody is a soak that failed.
    COVER_SHORT=$(printf '%s' "$COVER_TALLY" | "$PYTHON_BIN" "$HERE/coverfloor.py" "$COVER_FLOOR")
    if [ -n "$COVER_SHORT" ]; then
        echo "== $COVER_SHORT"; echo "== $COVER_SHORT" >> "$LEDGER"
    fi
fi

echo "[night] $LEDGER"
# EXIT 0 MEANS EVERY PASS WAS GREEN, and nothing else. A run that never ran is not a
# run that passed: four of five plus a skipped seed is not a five.
[ -n "$COVER_SHORT" ] && exit 1
[ -n "$FAILED" ] && exit 1
[ -n "$SKIPPED" ] && exit 3
[ "$PASSED" = "$TOTAL_RUNS" ] && exit 0
exit 1

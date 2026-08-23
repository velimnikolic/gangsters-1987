#!/bin/bash
# Thirty runs in a row, each a different quarter, each judged the same way. Nothing is
# accepted on one good run - a fault that shows up one time in ten is still a fault,
# and this is what finds it.
#
#   Tools/play/soak.sh --runs 30                    the car mission (soak.ps1's own)
#   Tools/play/soak.sh --runs 30 --moto             the motorcycle: two men, one pass
#   Tools/play/soak.sh --runs 30 --freeway          the motorway: two quarters, one road
#
# soak.ps1's opposite number for a Mac editor. Same shape, same ledger, same tally.
# The editor must be CLOSED throughout. Around a minute a run.

set -u

RUNS=30
SECONDS_=480
MOTO_SECONDS=900   # three passes across a quarter, each door to door: see soak.sh --moto
SCENE="Assets/Scenes/BlockDemo.unity"
OUT=""
FIRST_SEED=101
MODE="car"
SETS=""

while [ $# -gt 0 ]; do
    case "$1" in
        --runs)    RUNS="$2"; shift 2 ;;
        --seconds) SECONDS_="$2"; shift 2 ;;
        --scene)   SCENE="$2"; shift 2 ;;
        --out)     OUT="$2"; shift 2 ;;
        --seed)    FIRST_SEED="$2"; shift 2 ;;
        --sets)    SETS="$2"; shift 2 ;;
        --moto)    MODE="moto"; shift ;;
        --roadblock) MODE="roadblock"; shift ;;
        --walk)    MODE="walk"; shift ;;
        --brawl)   MODE="brawl"; shift ;;
        --freeway) MODE="freeway"; shift ;;
        *) echo "[soak] unknown argument: $1" >&2; exit 2 ;;
    esac
done

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# THE MOTORWAY is a scene of its own: two quarters six hundred metres apart with an
# elevated road between them, and what is judged is the road - that cars get on it,
# pay at the plaza, cross, and get off at the other end (analyze.py --freeway).
SEED_FIELD="BlockDemoBuilder.spacingSeed"
if [ "$MODE" = "freeway" ]; then
    [ "$SCENE" = "Assets/Scenes/BlockDemo.unity" ] && SCENE="Assets/Scenes/FreewayDemo.unity"
    SEED_FIELD="FreewayDemoBuilder.spacingSeed"
fi

# The quarter each mode wants. The car soak's line is soak.ps1's, unchanged, so the two
# scripts really do run the same lab. The motorcycle's is its own: a machine bought off
# the counter, no car mission at all, and three passes a run - enough that a fault in
# the loop has to show up rather than hide behind the first one.
if [ -z "$SETS" ]; then
    if [ "$MODE" = "moto" ]; then
        # Two mobs to ride at, one hood behind each of their bosses. Not more: the
        # crews the machine rides past come looking for the crew it went out from, and
        # a standing crew of five with six armed men walking at it is wiped out before
        # the second pass - which ends the run early and proves nothing about the loop.
        #
        # A crew with men to spare and guns worth carrying: four hoods behind one
        # lieutenant, armed off the counter. Both halves matter. A pass costs a hood
        # now and then, and a crew of one cannot ride out three times; and a pillion
        # carrying the .38 every man has in his coat reaches ten metres, which at the
        # pass speed is one round fired before the machine is past - the first runs of
        # this lost the rider on every pass for exactly that reason.
        SETS="BlockDemoBuilder.rivalCrews=2;BlockDemoBuilder.rivalHoods=1;BlockDemoBuilder.carCount=20;BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.missionMoto=1;BlockDemoBuilder.missionPassesRidden=3;BlockDemoBuilder.outfitMotorcycle=Motorbike;BlockDemoBuilder.outfitLieutenants=1;BlockDemoBuilder.outfitHoods=4;BlockDemoBuilder.mixedArms=1"
    else
        SETS="BlockDemoBuilder.rivalCrews=1;BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.carCount=20;BlockDemoBuilder.rivalHoods=1;BlockDemoBuilder.missionPasses=30"
    fi
    # THE ROADBLOCK is the car mission with the hunted mob marched into the carriageway
    # in front of the outfit's car every few seconds. A quarter does not make that scene
    # by itself - rival crews stand at frontages and the car passes them on the road -
    # and without it the run-down and the bullet holes are code nobody ever reaches
    # (sixty runs of the other two soaks fired neither, once). It is a MODE and not the
    # car soak's default because men standing in a live lane jam the ordinary traffic
    # too, and that is a different thing being measured.
    if [ "$MODE" = "roadblock" ]; then
        SETS="BlockDemoBuilder.rivalCrews=1;BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.carCount=20;BlockDemoBuilder.rivalHoods=3;BlockDemoBuilder.missionPasses=120;BlockDemoBuilder.missionRoadblock=1"
    fi
    # THE WALKABOUT judges the walking, nothing else: no rivals at all (a fight
    # would excuse the tether), two crews of five so the pack and the lanes have
    # men to show on, and the ordinary traffic and crowd in their way.
    if [ "$MODE" = "walk" ]; then
        SETS="BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.missionWalk=1;BlockDemoBuilder.rivalCrews=0;BlockDemoBuilder.carCount=20;BlockDemoBuilder.outfitLieutenants=2;BlockDemoBuilder.outfitHoods=4"
    fi
    # THE BRAWL is the on-foot war with the nerve lever up: two crews of ours
    # marched the quarter at three mobs of five, mixed arms, and 80% of the men
    # shot to their last hit break and run - so every run has the runners the
    # runnerchase and aimlow rules exist to watch.
    if [ "$MODE" = "freeway" ]; then
        SETS="FreewayDemoBuilder.carCount=34"
    fi
    if [ "$MODE" = "brawl" ]; then
        SETS="BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.missionOnFoot=1;BlockDemoBuilder.rivalCrews=3;BlockDemoBuilder.rivalHoods=4;BlockDemoBuilder.carCount=20;BlockDemoBuilder.outfitLieutenants=2;BlockDemoBuilder.outfitHoods=4;BlockDemoBuilder.mixedArms=1;BlockDemoBuilder.panicChance=0.8"
    fi
fi

if [ -z "$OUT" ]; then
    OUT="$HOME/Library/Application Support/gangsters-play/soak-$MODE-$(date +%Y%m%d-%H%M%S)"
fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
LEDGER="$OUT/soak.txt"

VERDICT_FLAG="--verdict"
if [ "$MODE" = "moto" ]; then
    VERDICT_FLAG="--moto"
    # A pass is a walk to the machine, a ride across the quarter and a ride back, three
    # times over - and the quarter has traffic in it. The car soak's eight minutes is
    # not enough sim for that, and a run cut off mid-pass reads as a fault.
    [ "$SECONDS_" = "480" ] && SECONDS_=$MOTO_SECONDS
fi
if [ "$MODE" = "freeway" ]; then
    VERDICT_FLAG="--freeway"
fi
# the crews' own verdict (CrewAudit rows) judges the walking and the fighting
if [ "$MODE" = "walk" ] || [ "$MODE" = "brawl" ]; then
    VERDICT_FLAG="--crew"
    # six corner-to-corner legs at a walking pace - a corner is ~400 m and the
    # boss walks 1.75 m/s, so a leg is near four sim-minutes; lights included
    [ "$MODE" = "walk" ] && [ "$SECONDS_" = "480" ] && SECONDS_=1500
fi

PASSED=0
FAILED=""
SKIPPED=""
for i in $(seq 1 "$RUNS"); do
    SEED=$(( FIRST_SEED + i ))
    DIR=$(printf "%s/run-%02d" "$OUT" "$i")
    "$HERE/run.sh" --scene "$SCENE" --seconds "$SECONDS_" --step 0.05 --out "$DIR" \
        --set "$SETS;$SEED_FIELD=$SEED" --timeout 20 >/dev/null 2>&1

    VERDICT=$(python3 "$HERE/analyze.py" "$DIR" $VERDICT_FLAG 2>&1)
    CODE=$?

    # A RUN THAT NEVER RAN IS NOT A RUN THAT FAILED. Unity refusing to play - the
    # scripts caught half-written, the editor open, the machine short of memory - says
    # nothing about the driving, and counting it against the city would send somebody
    # hunting a fault that is not there. It is said out loud and skipped.
    if [ "$CODE" = "3" ]; then WORD="NO RUN"; SKIPPED="$SKIPPED $i"
    elif [ "$CODE" = "0" ]; then WORD="PASSED"; PASSED=$(( PASSED + 1 ))
    else WORD="FAILED"; FAILED="$FAILED $i"
    fi

    LINE=$(printf "run %2d/%d seed %d: %s" "$i" "$RUNS" "$SEED" "$WORD")
    HEAD=$(echo "$VERDICT" | head -6)
    echo "$LINE"
    echo "$HEAD"
    # the run's own verdict, beside its trace: read THIS while the soak is going
    printf "%s\n%s\n" "$LINE" "$HEAD" > "$DIR/verdict.txt"
    printf "%s\n%s\n\n" "$LINE" "$HEAD" >> "$LEDGER"
done

TALLY="== $PASSED of $RUNS passed"
[ -n "$FAILED" ] && TALLY="$TALLY; the ones that did not:$FAILED"
[ -n "$SKIPPED" ] && TALLY="$TALLY; never ran:$SKIPPED"
echo "$TALLY"
echo "$TALLY" >> "$LEDGER"
echo "[soak] $LEDGER"
[ -n "$FAILED" ] && exit 1
exit 0

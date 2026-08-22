#!/bin/bash
# THE MONKEY SOAK: fifty unattended runs of the CITY, with the mobs at each other.
#
#   Tools/play/monkey.sh --runs 50 --seconds 240
#
# Not the lab quarter (BlockDemo) and not one scripted mission: this is Game.unity,
# the city the player plays, with MonkeyRunner setting family against family every few
# seconds and sending the outfit out by car, on foot and on the motorcycle - while
# CityAudit sweeps the ground for holes and the buildings for anything standing in the
# air. What comes out is fifty directories of trace and log; monkey.py reads them.
#
# The first run is the city as it ships (its own seed); the rest walk the layout seed,
# so a fault that needs a particular street to show up has fifty streets to show up on.
#
# The editor must be CLOSED. Around two minutes a run.

set -u

RUNS=50
FROM=1
SECONDS_=240
STEP=0.05
SCENE="Assets/Scenes/Game.unity"
OUT=""
ORDER_EVERY=5

while [ $# -gt 0 ]; do
    case "$1" in
        --runs)    RUNS="$2"; shift 2 ;;
        --seconds) SECONDS_="$2"; shift 2 ;;
        --step)    STEP="$2"; shift 2 ;;
        --scene)   SCENE="$2"; shift 2 ;;
        --out)     OUT="$2"; shift 2 ;;
        --every)   ORDER_EVERY="$2"; shift 2 ;;
        --from)    FROM="$2"; shift 2 ;;
        *) echo "[monkey] unknown argument: $1" >&2; exit 2 ;;
    esac
done

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -z "$OUT" ]; then
    OUT="$HOME/Library/Application Support/gangsters-play/monkey-$(date +%Y%m%d-%H%M%S)"
fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
LEDGER="$OUT/monkey.txt"

echo "[monkey] $RUNS runs of $SCENE, ${SECONDS_}s each -> $OUT"

for i in $(seq "$FROM" "$RUNS"); do
    DIR=$(printf "%s/run-%02d" "$OUT" "$i")
    SETS="RoadDemoBuilder.monkey=1;RoadDemoBuilder.monkeySeed=$i;RoadDemoBuilder.monkeyOrderEvery=$ORDER_EVERY"
    # run 1 is the city as it ships; after that the layout seed walks
    if [ "$i" != "1" ]; then
        SETS="$SETS;RoadDemoBuilder.spacingSeed=$(( 100 + i ))"
    fi

    # --sample 1: the per-car second-by-second samples are a hundred and sixty
    # megabytes a run and this soak reads EVENTS (faults, shots, the monkey's orders),
    # which are not sampled. --shot 0: nobody is going to look at fifty pictures of one
    # corner of the city, and CityAudit is what looks at the ground.
    "$HERE/run.sh" --scene "$SCENE" --seconds "$SECONDS_" --step "$STEP" --out "$DIR" \
        --sample 1.0 --shot 0 --set "$SETS" --timeout 25 >/dev/null 2>&1

    # the run's own line, beside its trace - read THIS while the soak is going
    {
        printf "run %2d/%d\n" "$i" "$RUNS"
        [ -f "$DIR/summary.json" ] && cat "$DIR/summary.json" && echo
        grep -hE "^\[monkey\] |^\[audit\] " "$DIR/unity.log" 2>/dev/null | sed 's/^/   /'
    } > "$DIR/verdict.txt" 2>/dev/null

    LINE=$(printf "run %2d/%d: %s" "$i" "$RUNS" \
        "$(grep -c '\[monkey\] fault\|\[audit\] ' "$DIR/unity.log" 2>/dev/null || echo 0) findings")
    echo "$LINE"
    cat "$DIR/verdict.txt" >> "$LEDGER"
done

echo "[monkey] done: $OUT"
python3 "$HERE/monkey.py" "$OUT" | tee "$OUT/report.txt"

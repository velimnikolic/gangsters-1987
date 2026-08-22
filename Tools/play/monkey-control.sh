#!/bin/bash
# The control the monkey soak needs: is the walking fault the CITY's, or is it the
# twenty families' - a hundred men marching where there used to be four?
#
#   Tools/play/monkey-control.sh
#
# Two pairs of runs at the same seeds, one with the city as it ships (twenty families,
# twenty-six crews) and one with the old single rival crew, and nothing else changed.
# What is compared is stalls PER CREW MAN, which is the only fair way to read it.
#
# The editor must be CLOSED.

set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$HOME/Library/Application Support/gangsters-play/monkey-control-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"

for seed in 1 2; do
    for arm in many one; do
        SETS="RoadDemoBuilder.monkey=1;RoadDemoBuilder.monkeySeed=$seed;RoadDemoBuilder.spacingSeed=$(( 100 + seed ))"
        [ "$arm" = "one" ] && SETS="$SETS;RoadDemoBuilder.rivalCrewsInCity=1;RoadDemoBuilder.rivalCrewCap=1"
        "$HERE/run.sh" --scene Assets/Scenes/Game.unity --seconds 150 --step 0.05 \
            --sample 1.0 --shot 0 --out "$OUT/$arm-$seed" --set "$SETS" --timeout 25 \
            >/dev/null 2>&1
        echo "$arm seed $seed: $(grep -c '"walkstall"' "$OUT/$arm-$seed/trace.jsonl" 2>/dev/null) stalls, \
$(python3 -c "
import json,sys
men=set()
for line in open('$OUT/$arm-$seed/trace.jsonl',errors='replace'):
    if '\"crew\"' not in line: continue
    try: r=json.loads(line)
    except: continue
    if r.get('tag')=='crew': men.add(r.get('id'))
print(len(men))
" 2>/dev/null) crew men on the street"
    done
done
echo "[control] $OUT"

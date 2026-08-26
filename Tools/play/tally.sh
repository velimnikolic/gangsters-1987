#!/usr/bin/env bash
# A tally of runs through the OPEN editor - the harness the CLI drives (gangsters_play),
# one run after another, with the verdict of each written to a table.
#
#     bash Tools/play/tally.sh <scene> <runs> <seconds> <outdir>
#
# soak.sh does the same with the editor CLOSED (its own batch Unity, a lockfile, a cold
# domain load a run). This one costs nothing but the editor already being up, which is
# how the rest of this project's work is done now.
set -uo pipefail

scene=${1:-Assets/Scenes/ExpresswayDemo.unity}
runs=${2:-30}
secs=${3:-180}
# NOT under the project's Temp: Unity empties that when the editor shuts down, and a
# run has just finished writing its trace there (run.ps1 says the same thing).
out=${4:-$LOCALAPPDATA/gangsters-play/tally}

mkdir -p "$out"
table="$out/tally.tsv"
printf 'run\tbelt\tworst_stand\tjump\tsteer\tstall\tlanechange\tdeck\ttoll\n' > "$table"

for i in $(seq 1 "$runs"); do
  dir=$(printf '%s/%02d' "$out" "$i")
  rm -rf "$dir"
  unity command gangsters_play --scene "$scene" --seconds "$secs" --out "$dir" >/dev/null 2>&1
  for _ in $(seq 1 60); do
    [ -f "$dir/summary.json" ] && break
    sleep 10
  done
  if [ ! -f "$dir/summary.json" ]; then
    printf '%d\tNO RUN\n' "$i" >> "$table"
    continue
  fi
  python - "$dir" "$i" >> "$table" <<'PY'
import json, sys, collections
d, run = sys.argv[1], sys.argv[2]
k = collections.Counter()
belt = lc = deck = toll = 0
worst = 0.0
stand = {}
with open(d + '/trace.jsonl', encoding='utf-8', errors='replace') as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            r = json.loads(line)
        except Exception:
            continue
        t = r.get('k')
        if t == 'belt':
            belt += 1
        elif t == 'fault':
            k[r.get('fault')] += 1
            if r.get('fault') == 'stall':
                w = r.get('what') or ''
                try:
                    worst = max(worst, float(w.split()[2].rstrip('s')))
                except Exception:
                    pass
        elif t == 'lanechange':
            lc += 1
        elif t == 'deck':
            deck += 1
        elif t == 'toll':
            toll += 1
print('%s\t%d\t%.0f\t%d\t%d\t%d\t%d\t%d\t%d' % (
    run, belt, worst, k['jump'], k['steer'], k['stall'], lc, deck, toll))
PY
  tail -1 "$table"
done

echo "---- tally over $runs runs ----"
python - "$table" <<'PY'
import sys, statistics
rows = []
with open(sys.argv[1], encoding='utf-8') as f:
    head = f.readline().rstrip('\n').split('\t')
    for line in f:
        parts = line.rstrip('\n').split('\t')
        if len(parts) != len(head):
            continue
        rows.append([float(x) for x in parts])
if not rows:
    print('no runs')
    raise SystemExit
for c in range(1, len(head)):
    col = [r[c] for r in rows]
    print('%-12s min %6.0f  median %6.0f  max %6.0f' % (
        head[c], min(col), statistics.median(col), max(col)))
print('runs: %d' % len(rows))
PY

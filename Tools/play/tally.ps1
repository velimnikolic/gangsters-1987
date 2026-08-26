# A tally of runs, each on a DIFFERENT city, judged the same way.
#
#     Tools\play\tally.ps1 -Runs 30 -Seconds 150
#
# The batch harness a run at a time (run.ps1: its own Unity, the editor closed, about
# four times real time), a row a run, and a summary at the end. Each run is given its own
# city number, so thirty runs are thirty towns - which is the point: a road that works on
# one map is a road for one map.
#
# It STOPS EARLY when a run goes badly wrong (-StopOver). A tally is for confirming that
# a thing works, not for sitting through twenty more runs of a fault already seen.
[CmdletBinding()]
param(
    [int]    $Runs      = 30,
    [double] $Seconds   = 150,
    [string] $Scene     = "Assets/Scenes/ExpresswayDemo.unity",
    [string] $Out       = "",
    [int]    $FirstSeed = 4001,
    [int]    $StopOver  = 2500,      # belt refusals in one run that end the tally
    [string[]] $Set     = @()
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrEmpty($Out)) { $Out = Join-Path $env:LOCALAPPDATA "gangsters-play\tally" }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$table = Join-Path $Out "tally.tsv"
"run`tseed`tbelt`tbeltXW`tdeck`tlanechange`ttoll`tjump`tsteer`tstall`tworstCell" | Set-Content $table

$reader = @'
import json, sys, collections
d = sys.argv[1]
belt = deck = lc = toll = 0
k = collections.Counter()
cells = collections.Counter()
try:
    f = open(d + "/trace.jsonl", encoding="utf-8", errors="replace")
except OSError:
    print("0\t0\t0\t0\t0\t0\t0\tNO-TRACE")
    raise SystemExit
with f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            r = json.loads(line)
        except Exception:
            continue
        t = r.get("k")
        if t == "belt":
            belt += 1
            p = r.get("p") or [0, 0]
            cells[(int(round(p[0] / 200) * 200), int(round(p[-1] / 200) * 200))] += 1
        elif t == "deck":
            deck += 1
        elif t == "lanechange":
            lc += 1
        elif t == "toll":
            toll += 1
        elif t == "fault":
            k[r.get("fault")] += 1
# the city's own jam sits on its origin junction and is there with the road switched off;
# everything else is somewhere a motorway might be
away = sum(n for c, n in cells.items() if abs(c[0]) > 300 or abs(c[1]) > 300)
worst = cells.most_common(1)
worst = ("%d,%d:%d" % (worst[0][0][0], worst[0][0][1], worst[0][1])) if worst else "-"
print("%d\t%d\t%d\t%d\t%d\t%d\t%d\t%s" % (belt, away, deck, lc, toll, k["jump"], k["steer"], worst) + "\t%d" % k["stall"])
'@
$readerPath = Join-Path $Out "read.py"
$reader | Set-Content $readerPath -Encoding UTF8

for ($i = 1; $i -le $Runs; $i++) {
    $seed = $FirstSeed + $i
    $dir = Join-Path $Out ("{0:d2}" -f $i)
    $sets = @("ExpresswayDemoBuilder.citySeed=$seed") + $Set
    & (Join-Path $PSScriptRoot "run.ps1") -Scene $Scene -Seconds $Seconds -Out $dir -NoGraphics -Set $sets | Out-Null
    $line = & python $readerPath $dir
    $parts = $line -split "`t"
    if ($parts.Count -lt 9) { "$i`t$seed`tNO RUN" | Add-Content $table; Write-Host "$i seed $seed  NO RUN"; continue }
    $belt = [int]$parts[0]; $away = [int]$parts[1]
    "$i`t$seed`t$($parts[0])`t$($parts[1])`t$($parts[2])`t$($parts[3])`t$($parts[4])`t$($parts[5])`t$($parts[6])`t$($parts[8])`t$($parts[7])" | Add-Content $table
    Write-Host ("{0,2} seed {1}  belt {2,6}  away {3,5}  deck {4,4}  lane {5,3}  toll {6,3}  worst {7}" -f `
        $i, $seed, $parts[0], $parts[1], $parts[2], $parts[3], $parts[4], $parts[7])
    if ($away -gt $StopOver) {
        Write-Host "[tally] run $i went wrong ($away refusals away from the city's own jam) - stopping here, as intended"
        break
    }
}

Write-Host "---- tally ----"
$rows = Import-Csv $table -Delimiter "`t" | Where-Object { $_.belt -match '^\d+$' }
foreach ($col in "belt", "beltXW", "deck", "lanechange", "toll", "jump", "steer", "stall") {
    $v = $rows | ForEach-Object { [int]$_.$col } | Sort-Object
    if ($v.Count -eq 0) { continue }
    $med = $v[[int]($v.Count / 2)]
    Write-Host ("{0,-11} min {1,6}  median {2,6}  max {3,6}" -f $col, $v[0], $med, $v[-1])
}
Write-Host ("runs: {0}   table: {1}" -f $rows.Count, $table)

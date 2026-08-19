# Thirty runs in a row, each a different quarter, each judged the same way: the crew
# gets in the car, wipes the mob out, is never stuck with somewhere to be, and parks.
# Nothing is accepted on one good run - a fault that shows up one time in ten is still
# a fault, and this is what finds it.
#
#   pwsh Tools/play/soak.ps1 -Runs 30
#
# The editor must be CLOSED throughout. Around a minute a run.

[CmdletBinding()]
param(
    [int]    $Runs    = 30,
    [double] $Seconds = 480,
    [string] $Scene   = "Assets/Scenes/BlockDemo.unity",
    [string] $Out     = "",
    [int]    $FirstSeed = 101,
    [string] $Sets    = "BlockDemoBuilder.rivalCrews=1;BlockDemoBuilder.missionAfter=15;BlockDemoBuilder.carCount=20;BlockDemoBuilder.rivalHoods=1;BlockDemoBuilder.missionPasses=30"
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$project = (Resolve-Path (Join-Path $here "..\..")).Path
if ([string]::IsNullOrEmpty($Out)) {
    $Out = Join-Path $env:LOCALAPPDATA ("gangsters-play\soak-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$ledger = Join-Path $Out "soak.txt"

$passed = 0
$failed = @()
for ($i = 1; $i -le $Runs; $i++) {
    $seed = $FirstSeed + $i
    $dir = Join-Path $Out ("run-{0:D2}" -f $i)
    $sets = "$Sets;BlockDemoBuilder.spacingSeed=$seed"

    & powershell -ExecutionPolicy Bypass -File (Join-Path $here "run.ps1") `
        -Scene $Scene -Seconds $Seconds -Step 0.05 -Out $dir -Set $sets -TimeoutMinutes 20 | Out-Null

    $verdict = & python (Join-Path $here "analyze.py") $dir --verdict 2>&1
    $ok = $LASTEXITCODE -eq 0
    if ($ok) { $passed++ } else { $failed += $i }

    $line = ("run {0,2}/{1} seed {2}: {3}" -f $i, $Runs, $seed, ($(if ($ok) { "PASSED" } else { "FAILED" })))
    $head = ($verdict | Select-Object -First 6) -join "`n"
    Write-Host $line
    Write-Host $head
    Add-Content -Path $ledger -Value $line -Encoding utf8
    Add-Content -Path $ledger -Value $head -Encoding utf8
    Add-Content -Path $ledger -Value "" -Encoding utf8
}

$tally = "== $passed of $Runs passed"
if ($failed.Count -gt 0) { $tally += "; the ones that did not: " + ($failed -join ", ") }
Write-Host $tally
Add-Content -Path $ledger -Value $tally -Encoding utf8
Write-Host "[soak] $ledger"
if ($failed.Count -gt 0) { exit 1 } else { exit 0 }

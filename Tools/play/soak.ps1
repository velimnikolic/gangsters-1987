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

# The ledger is appended between runs; anything else holding the file open for that
# instant used to end the soak. Waited for, not died on.
function Append-Ledger([string] $path, [string] $text) {
    for ($try = 0; $try -lt 40; $try++) {
        try { Add-Content -Path $path -Value $text -Encoding utf8 -ErrorAction Stop; return }
        catch { Start-Sleep -Milliseconds 250 }
    }
    Write-Host "[soak] could not write the ledger; the run's own verdict.txt still has it"
}

$passed = 0
$failed = @()
$skipped = @()
for ($i = 1; $i -le $Runs; $i++) {
    $seed = $FirstSeed + $i
    $dir = Join-Path $Out ("run-{0:D2}" -f $i)
    $sets = "$Sets;BlockDemoBuilder.spacingSeed=$seed"

    & powershell -ExecutionPolicy Bypass -File (Join-Path $here "run.ps1") `
        -Scene $Scene -Seconds $Seconds -Step 0.05 -Out $dir -Set $sets -TimeoutMinutes 20 | Out-Null

    $verdict = & python (Join-Path $here "analyze.py") $dir --verdict 2>&1
    $code = $LASTEXITCODE
    # A RUN THAT NEVER RAN IS NOT A RUN THAT FAILED. Unity refusing to play - the
    # scripts caught half-written, the editor open, the machine short of memory -
    # says nothing about the driving, and counting it against the city would send
    # somebody hunting a fault that is not there. It is said out loud and skipped.
    $ok = $code -eq 0
    if ($code -eq 3) { $skipped += $i }
    elseif ($ok) { $passed++ }
    else { $failed += $i }

    $word = if ($code -eq 3) { "NO RUN" } elseif ($ok) { "PASSED" } else { "FAILED" }
    $line = ("run {0,2}/{1} seed {2}: {3}" -f $i, $Runs, $seed, $word)
    $head = ($verdict | Select-Object -First 6) -join "`n"
    Write-Host $line
    Write-Host $head
    # the run's own verdict, beside its trace: read THIS while the soak is going, never
    # the ledger - a reader holding the ledger open for a moment is enough to make the
    # append below throw, and with it the whole soak (three runs in, the once it happened)
    Set-Content -Path (Join-Path $dir "verdict.txt") -Value ($line + "`n" + $head) -Encoding utf8
    Append-Ledger $ledger ($line + "`n" + $head + "`n")
}

$tally = "== $passed of $Runs passed"
if ($failed.Count -gt 0) { $tally += "; the ones that did not: " + ($failed -join ", ") }
if ($skipped.Count -gt 0) { $tally += "; never ran: " + ($skipped -join ", ") }
Write-Host $tally
Append-Ledger $ledger $tally
Write-Host "[soak] $ledger"
if ($failed.Count -gt 0) { exit 1 } else { exit 0 }

# Plays a scene headless and leaves a run behind: the trace, the logs, the pictures.
#
#   pwsh Tools/play/run.ps1 -Scene Assets/Scenes/BlockDemo.unity -Seconds 90 -Out runs/001
#
# Unity holds the project with a lock, so the editor must be CLOSED while this runs.
# Nothing here touches the working tree: everything is written under -Out.

[CmdletBinding()]
param(
    [string]   $Scene   = "Assets/Scenes/BlockDemo.unity",
    [double]   $Seconds = 90,
    [string]   $Out     = "",
    [double]   $Step    = 0.0333,
    [double]   $Sample  = 0.1,
    [double]   $Warm    = 3,
    [double]   $Shot    = 0,
    [double]   $Wall    = 1200,
    [string[]] $Set     = @(),   # "Type.field=value", several joined by ';'
    [switch]   $NoGraphics,
    [int]      $TimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

# --- which Unity: the one the project is stamped with
$version = (Get-Content (Join-Path $project "ProjectSettings\ProjectVersion.txt") | Select-Object -First 1).Split(" ")[1]
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "no Unity $version at $unity" }

# --- the project may be open in one editor only
$lock = Join-Path $project "Temp\UnityLockfile"
if (Test-Path $lock) {
    $held = $false
    try { $s = [System.IO.File]::Open($lock, "Open", "ReadWrite", "None"); $s.Close() } catch { $held = $true }
    if ($held) { throw "the project is open in the Unity editor - close it, then run this again" }
}

# NOT under the project's Temp: that is Unity's own scratch directory and it is
# emptied when the editor shuts down - which is exactly when a run has just finished
# writing its trace there.
if ([string]::IsNullOrEmpty($Out)) {
    $Out = Join-Path $env:LOCALAPPDATA ("gangsters-play\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
Get-ChildItem -Path $Out -File -ErrorAction SilentlyContinue | Remove-Item -Force

$unityLog = Join-Path $Out "unity.log"

$argv = @(
    "-batchmode", "-accept-apiupdate", "-silent-crashes",
    "-projectPath", $project,
    "-logFile", $unityLog,
    "-executeMethod", "GangstersTools.PlayHarness.Run",
    "-hScene", $Scene,
    "-hOut", $Out,
    "-hSeconds", $Seconds,
    "-hStep", $Step,
    "-hSample", $Sample,
    "-hWarm", $Warm,
    "-hShot", $Shot,
    "-hWall", $Wall
)
if ($NoGraphics) { $argv += "-nographics" }
# -File passes every argument as one raw string, so several sets come in as one
# semicolon-separated piece: BlockDemoBuilder.rivalCrews=2;BlockDemoBuilder.missionAfter=15
foreach ($s in $Set) {
    foreach ($one in ($s -split ";")) {
        if ($one.Trim().Length -gt 0) { $argv += @("-hSet", $one.Trim()) }
    }
}

Write-Host "[run] $version  $Scene  ${Seconds}s  -> $Out"
$started = Get-Date
$proc = Start-Process -FilePath $unity -ArgumentList $argv -PassThru -NoNewWindow
if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Host "[run] no end after $TimeoutMinutes minutes - killing it"
    try { $proc.Kill($true) } catch {}
    Start-Sleep -Seconds 2
    $code = 124
} else {
    $code = $proc.ExitCode
}
$took = [int]((Get-Date) - $started).TotalSeconds

Write-Host "[run] exit $code after ${took}s"
if (Test-Path $unityLog) {
    $errors = Select-String -Path $unityLog -Pattern "error CS|Exception:|Fatal|Aborting" -ErrorAction SilentlyContinue |
              Select-Object -First 20
    if ($errors) { Write-Host "[run] from the editor log:"; $errors | ForEach-Object { Write-Host "   $_" } }
}
$summary = Join-Path $Out "summary.json"
if (Test-Path $summary) { Write-Host "[run] $(Get-Content $summary -Raw)" } else { Write-Host "[run] no summary was written" }
exit $code

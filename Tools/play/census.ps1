# Runs the canonical NPC-001 census as a retained Pipeline job.
#
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Rows
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -DetachOnly
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Resume <job-id>
#
# Submission is detached on purpose. The editor may already have main-thread work
# queued, and a synchronous CLI request would then lose its reply at the transport
# timeout even though the census itself is healthy. Pipeline retains this job and
# the ID below is also written under Temp/play, so a caller can always reattach.

[CmdletBinding()]
param(
    [switch] $Rows,
    [switch] $DetachOnly,
    [string] $Resume = "",
    [ValidateRange(0, 86400)]
    [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$jobFile = Join-Path $project "Temp\play\people-census.job"
Get-Command unity -ErrorAction Stop | Out-Null

function Invoke-UnityRaw([string[]] $Arguments) {
    # Windows PowerShell promotes redirected native stderr to ErrorRecord objects.
    # Keep those in the captured body so a non-zero CLI exit can report the retained
    # job cleanly instead of ErrorActionPreference aborting before our recovery hint.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $lines = @(& unity @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }

    return @{ Lines = $lines; ExitCode = $exitCode }
}

function Invoke-UnityJson([string[]] $Arguments, [string] $Operation) {
    $invocation = Invoke-UnityRaw $Arguments
    $body = $invocation.Lines -join [Environment]::NewLine

    if ($invocation.ExitCode -ne 0) {
        throw "$Operation failed with exit code $($invocation.ExitCode).`n$body"
    }

    try {
        $reply = $body | ConvertFrom-Json
    }
    catch {
        throw "$Operation returned invalid JSON.`n$body"
    }

    if (-not $reply.success) {
        throw "$Operation was rejected.`n$body"
    }

    return @{ Reply = $reply; Body = $body }
}

if ([string]::IsNullOrWhiteSpace($Resume)) {
    $arguments = @(
        "command", "--project-path", $project, "--detach", "gangsters_people_census",
        "--seed", "1987"
    )
    if ($Rows) { $arguments += "--rows" }
    $arguments += "--json"

    $submission = Invoke-UnityJson $arguments "census submission"
    $jobId = [string]$submission.Reply.data.jobId
    if ($jobId -notmatch '^[0-9a-fA-F]{32}$') {
        throw "census submission returned no valid job ID.`n$($submission.Body)"
    }
}
else {
    $jobId = $Resume.Trim()
    if ($jobId -notmatch '^[0-9a-fA-F]{32}$') {
        throw "-Resume must be the 32-character hexadecimal Pipeline job ID."
    }
}

$jobDirectory = Split-Path -Parent $jobFile
New-Item -ItemType Directory -Force -Path $jobDirectory | Out-Null
Set-Content -LiteralPath $jobFile -Value $jobId -Encoding ascii

Write-Host "[census] retained job $jobId"
Write-Host "[census] resume: powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Resume $jobId"

if ($DetachOnly) {
    Write-Output $jobId
    exit 0
}

$waitArguments = @(
    "job", "wait", $jobId,
    "--timeout", [string]$TimeoutSeconds,
    "--project-path", $project, "--json"
)

$wait = Invoke-UnityRaw $waitArguments
$waitBody = $wait.Lines -join [Environment]::NewLine
if ($wait.ExitCode -ne 0) {
    [Console]::Error.WriteLine(
        "Waiting stopped, but census job $jobId remains retained. Use -Resume $jobId.`n$waitBody")
    exit $wait.ExitCode
}

try {
    $waitReply = $waitBody | ConvertFrom-Json
}
catch {
    [Console]::Error.WriteLine(
        "Census wait returned invalid JSON; job $jobId remains queryable.`n$waitBody")
    exit 1
}

Write-Output $waitBody
if (-not $waitReply.success -or $waitReply.data.state -ne "completed" -or
    $null -eq $waitReply.data.result -or -not $waitReply.data.result.passed) {
    exit 1
}

exit 0

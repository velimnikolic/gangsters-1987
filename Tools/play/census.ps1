# Runs the canonical NPC-001 census as a retained Pipeline job.
#
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Rows
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -DetachOnly
#   powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Resume <job-id>
#
# Submission is detached on purpose. The editor may already have main-thread work
# queued, and a synchronous CLI request would then lose its reply at the transport
# timeout even though the census itself is healthy. Pipeline retains this job and
# writes an atomic, per-ID receipt under Temp/play, so a caller can reattach. All
# diagnostics go to stderr; stdout stays one JSON document, or one ID with -DetachOnly.

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
$censusCommand = "gangsters_people_census"
$checkpointDirectory = Join-Path $project "Temp\play\people-census"
Get-Command unity -ErrorAction Stop | Out-Null

function Write-CensusDiagnostic([string] $Message) {
    [Console]::Error.WriteLine("[census] $Message")
}

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

function Assert-CensusJobIdentity(
    [object] $Reply,
    [string] $ExpectedJobId,
    [switch] $RequireResult
) {
    if ($Reply.success -ne $true) {
        throw "Pipeline did not report success for job $ExpectedJobId."
    }

    if ($null -eq $Reply.data) {
        throw "Pipeline returned no job data for $ExpectedJobId."
    }

    $actualJobId = [string]$Reply.data.jobId
    if ($actualJobId -cne $ExpectedJobId) {
        throw "Pipeline returned job '$actualJobId' while '$ExpectedJobId' was requested."
    }

    $actualCommand = [string]$Reply.data.command
    if ($actualCommand -cne $censusCommand) {
        throw "Job $ExpectedJobId belongs to '$actualCommand', not '$censusCommand'."
    }

    if (-not $RequireResult) { return }

    $result = $Reply.data.result
    if ($null -eq $result) {
        throw "Completed census job $ExpectedJobId returned no result."
    }

    foreach ($property in @("passed", "failures", "seed", "doors", "kerb", "crowdTick", "businessRegistry")) {
        if ($null -eq $result.PSObject.Properties[$property]) {
            throw "Census job $ExpectedJobId returned no '$property' field."
        }
    }

    if ($result.seed -ne 1987) {
        throw "Census job $ExpectedJobId returned seed '$($result.seed)', not canonical seed 1987."
    }
}

function Save-CensusJobReceipt([string] $JobId) {
    $receipt = Join-Path $checkpointDirectory "$JobId.job"
    $temporary = Join-Path $checkpointDirectory (".{0}.{1}.{2}.tmp" -f $JobId, $PID, [Guid]::NewGuid().ToString("N"))

    try {
        [System.IO.Directory]::CreateDirectory($checkpointDirectory) | Out-Null

        if ([System.IO.File]::Exists($receipt)) {
            $existing = [System.IO.File]::ReadAllText($receipt).Trim()
            if ($existing -cne $JobId) {
                throw "existing receipt has unexpected contents"
            }
            return $true
        }

        [System.IO.File]::WriteAllText(
            $temporary,
            $JobId + [Environment]::NewLine,
            [System.Text.Encoding]::ASCII)

        try {
            # Same-directory rename: readers see either no receipt or the complete ID.
            [System.IO.File]::Move($temporary, $receipt)
        }
        catch [System.IO.IOException] {
            # A concurrent resume of the same ID may have won the rename race.
            if (-not [System.IO.File]::Exists($receipt) -or
                [System.IO.File]::ReadAllText($receipt).Trim() -cne $JobId) {
                throw
            }
        }

        return $true
    }
    catch {
        Write-CensusDiagnostic "could not checkpoint retained job $JobId at '$receipt': $($_.Exception.Message)"
        return $false
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            try { [System.IO.File]::Delete($temporary) }
            catch {
                Write-CensusDiagnostic "could not remove temporary receipt for job ${JobId}: $($_.Exception.Message)"
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Resume)) {
    $arguments = @(
        "command", "--project-path", $project, "--detach", $censusCommand,
        "--seed", "1987"
    )
    if ($Rows) { $arguments += "--rows" }
    $arguments += "--json"

    $submission = Invoke-UnityJson $arguments "census submission"
    $jobId = [string]$submission.Reply.data.jobId
    if ($jobId -notmatch '^[0-9a-fA-F]{32}$') {
        throw "census submission returned no valid job ID.`n$($submission.Body)"
    }
    Write-CensusDiagnostic "retained job $jobId"
    Write-CensusDiagnostic "resume: powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Resume $jobId"
    Assert-CensusJobIdentity $submission.Reply $jobId
}
else {
    $jobId = $Resume.Trim()
    if ($jobId -notmatch '^[0-9a-fA-F]{32}$') {
        throw "-Resume must be the 32-character hexadecimal Pipeline job ID."
    }

    $statusArguments = @(
        "job", "status", $jobId,
        "--project-path", $project, "--json"
    )
    $status = Invoke-UnityJson $statusArguments "census resume validation for $jobId"
    Assert-CensusJobIdentity $status.Reply $jobId `
        -RequireResult:($status.Reply.data.state -eq "completed")
    Write-CensusDiagnostic "validated retained census job $jobId"
}

$checkpointSaved = Save-CensusJobReceipt $jobId

if ($DetachOnly) {
    Write-Output $jobId
    if (-not $checkpointSaved) { exit 2 }
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
    Write-CensusDiagnostic "waiting stopped, but job $jobId remains retained; use -Resume $jobId.`n$waitBody"
    exit $wait.ExitCode
}

try {
    $waitReply = $waitBody | ConvertFrom-Json
}
catch {
    Write-CensusDiagnostic "wait returned invalid JSON; job $jobId remains queryable.`n$waitBody"
    exit 1
}

try {
    Assert-CensusJobIdentity $waitReply $jobId `
        -RequireResult:($waitReply.data.state -eq "completed")
}
catch {
    Write-CensusDiagnostic $_.Exception.Message
    exit 1
}

Write-Output $waitBody
if ($waitReply.data.state -ne "completed") {
    Write-CensusDiagnostic "job $jobId is '$($waitReply.data.state)', not completed; use -Resume $jobId."
    exit 6
}

$result = $waitReply.data.result
if ($result.passed -ne $true -or @($result.failures).Count -ne 0) { exit 1 }
if (-not $checkpointSaved) { exit 2 }
exit 0

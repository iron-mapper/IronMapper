#Requires -Version 7
<#
.SYNOPSIS
    Release benchmark run, unattended: published IronMapper 1.1.3 and the local sources in one session.

.DESCRIPTION
    Runs, in one go:
      0) pre-flight checks (branch, clean tree except benchmarks/results/, run-all.ps1 -CheckOnly)
      1) published IronMapper 1.1.3 -> benchmarks/results/1.1.3-rerun
      2) local sources (main)       -> benchmarks/results/2.0.0
      3) verification of both runs and a summary file (artifacts/bench-summary.txt)

    Build outputs are cleaned before each run. The computer is kept awake while the script runs (power settings are
    not changed; normal sleep returns when the script ends, even on failure). If run 1 fails, run 2 still runs.

    A run passes when run-all.ps1 exits with 0, its log ends with "All done in" and its RESULTS.md has none of the
    markers generate-report.ps1 writes for a missing or shortened measurement ("_Not measured", "**Quick run**",
    "**Dry run**"; case-sensitive, so ordinary text such as "is not measured separately" does not count).

    -Dry passes -Dry to run-all.ps1 (one BDN iteration, a few minutes per run) to check the commands: results go to
    artifacts/bench-dry/ (benchmarks/results/ is not touched), logs to artifacts/bench-dry-*.log, the branch and
    clean-tree checks only warn, and RESULTS.md must carry the "**Dry run**" marker.

.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-benchmarks.ps1
.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-benchmarks.ps1 -SkipRerun
    # Do not run 1.1.3 again; if results/1.1.3-rerun and its log already exist, they are verified instead;
    # otherwise compare with results/baseline-1.1.3.
.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-benchmarks.ps1 -Dry
#>
param(
    [string]$Repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$Branch = 'main',
    [switch]$SkipRerun,
    [switch]$Dry
)

$ErrorActionPreference = 'Stop'
$bench   = Join-Path $Repo 'benchmarks'
$logDir  = Join-Path $Repo 'artifacts'
$prefix  = $Dry ? 'bench-dry' : 'bench'
$summary = Join-Path $logDir "$prefix-summary.txt"
# Relative to benchmarks/ (run-all.ps1 -ResultsDir).
$resultsRoot = $Dry ? '../artifacts/bench-dry' : 'results'
$started = Get-Date

New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-Summary([string]$text) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $text
    Write-Host $line -ForegroundColor Cyan
    Add-Content -Path $summary -Value $line
}

function Clear-BuildOutputs {
    dotnet build-server shutdown | Out-Host
    foreach ($dir in 'src', 'tests', 'probes', 'tools') {
        $path = Join-Path $bench $dir
        if (Test-Path $path) {
            Get-ChildItem $path -Recurse -Directory -Include bin, obj -ErrorAction SilentlyContinue |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Summary "Build outputs cleaned"
}

# Returns ONLY the exit code. All output of run-all.ps1 goes to the console (Out-Host) and to the log, never into the
# function's return value (otherwise the caller gets an object array instead of an int).
function Invoke-Run([string]$name, [string[]]$runArgs) {
    $log = Join-Path $logDir "$prefix-$name.log"
    Clear-BuildOutputs
    Write-Summary "START $name (log: $log)"
    if ($Dry) { $runArgs = @('-Dry') + $runArgs }
    Push-Location $bench
    try {
        pwsh -NoProfile -File ./scripts/run-all.ps1 @runArgs *>&1 |
            Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    Write-Summary "END   $name, exit code $code"
    return [int]$code
}

# Returns $true/$false only.
function Test-Run([string]$name, [int]$exitCode, [string]$resultsDir) {
    $log = Join-Path $logDir "$prefix-$name.log"
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($exitCode -ne 0) { $problems.Add("exit code $exitCode") }
    if (-not (Test-Path $log)) {
        $problems.Add("log not found: $log")
    }
    elseif (-not (Select-String -Path $log -Pattern 'All done in' -SimpleMatch -Quiet)) {
        $problems.Add("no 'All done in' in log")
    }
    $results = Join-Path $bench "$resultsDir/RESULTS.md"
    if (-not (Test-Path $results)) {
        $problems.Add("missing $resultsDir/RESULTS.md")
    }
    else {
        # Markers written by generate-report.ps1: "_Not measured._", "_Not measured: `results/x` does not exist._",
        # "> **Quick run** (...)", "> **Dry run** (...)".
        $notMeasured = Select-String -Path $results -Pattern '_Not measured[.:]' -CaseSensitive
        if ($notMeasured) {
            $problems.Add("RESULTS.md has '_Not measured' at line(s) $(($notMeasured.LineNumber) -join ', ')")
        }
        $hasDry = Select-String -Path $results -Pattern '**Dry run**' -SimpleMatch -CaseSensitive -Quiet
        $hasQuick = Select-String -Path $results -Pattern '**Quick run**' -SimpleMatch -CaseSensitive -Quiet
        if ($hasQuick) { $problems.Add("RESULTS.md is marked 'Quick run'") }
        if ($Dry -and -not $hasDry) { $problems.Add("RESULTS.md of a -Dry run has no 'Dry run' marker") }
        if (-not $Dry -and $hasDry) { $problems.Add("RESULTS.md is marked 'Dry run'") }
    }
    if ($problems.Count -eq 0) {
        Write-Summary "OK    $name"
        return $true
    }
    Write-Summary "FAIL  $name`: $($problems -join '; ')"
    return $false
}

# A problem that stops a real run; with -Dry only a warning.
function Stop-Or-Warn([string]$message) {
    if (-not $Dry) { throw $message }
    Write-Summary "WARN  $message (ignored with -Dry)"
}

# Keep the system awake: ES_CONTINUOUS | ES_SYSTEM_REQUIRED.
Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@
[Win32.Power]::SetThreadExecutionState([uint32]'0x80000001') | Out-Null

try {
    Write-Summary ($Dry ? "Benchmark run started (-Dry)" : "Benchmark run started")

    # ---- 0. Pre-flight: fail fast while you are still at the computer ----
    Set-Location $Repo
    $currentBranch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($currentBranch -ne $Branch) {
        Stop-Or-Warn "Current branch is '$currentBranch', expected '$Branch'. Run: git switch $Branch; git pull"
    }
    # Results of earlier runs (benchmarks/results/...) are allowed; anything else is not. Porcelain v1 lines are
    # "XY path" (renames "XY old -> new"); compare the path part only.
    $dirty = git status --porcelain | Where-Object { $_.Substring(3) -notmatch '^"?benchmarks/results/' }
    if ($dirty) { Stop-Or-Warn "Working tree is not clean:`n$($dirty -join "`n")" }
    Write-Summary "Branch $currentBranch, commit $((git rev-parse --short HEAD).Trim())$($dirty ? '' : ', tree clean')"

    Clear-BuildOutputs
    Push-Location $bench
    try {
        pwsh -NoProfile -File ./scripts/run-all.ps1 -CheckOnly | Out-Host
        $checkCode = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($checkCode -ne 0) { throw "run-all.ps1 -CheckOnly failed with exit code $checkCode" }
    Write-Summary "Pre-flight OK. From here on nothing needs your attention."

    $allOk = $true

    # ---- 1. Published 1.1.3 ----
    $rerunDir = "$resultsRoot/1.1.3-rerun"
    if (-not $SkipRerun) {
        $code1 = Invoke-Run '1.1.3-rerun' @('-ResultsDir', $rerunDir)
        if (-not (Test-Run '1.1.3-rerun' $code1 $rerunDir)) { $allOk = $false }
    }
    elseif (Test-Path (Join-Path $logDir "$prefix-1.1.3-rerun.log")) {
        Write-Summary "SKIP  1.1.3-rerun run (-SkipRerun); verifying the existing results"
        if (-not (Test-Run '1.1.3-rerun' 0 $rerunDir)) { $allOk = $false }
    }
    else {
        Write-Summary "SKIP  1.1.3-rerun (-SkipRerun, no earlier run); compare with results/baseline-1.1.3"
    }

    # ---- 2. Local sources ----
    $localDir = "$resultsRoot/2.0.0"
    $code2 = Invoke-Run '2.0.0' @('-UseLocalIronMapper', '-ResultsDir', $localDir)
    if (-not (Test-Run '2.0.0' $code2 $localDir)) { $allOk = $false }

    # ---- 3. Summary ----
    $elapsed = (Get-Date) - $started
    if (-not $allOk) {
        Write-Summary ("FINISHED WITH PROBLEMS in {0:hh\:mm\:ss}. See FAIL lines above and the logs." -f $elapsed)
        exit 1
    }
    Write-Summary ("ALL DONE in {0:hh\:mm\:ss}. Next: generate the comparison and update the docs." -f $elapsed)
    exit 0
}
catch {
    Write-Summary "ABORTED: $($_.Exception.Message)"
    exit 2
}
finally {
    # Let the system sleep normally again.
    [Win32.Power]::SetThreadExecutionState([uint32]'0x80000000') | Out-Null
}

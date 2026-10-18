#Requires -Version 7.0
<#
.SYNOPSIS
    Full benchmark run (BENCHMARK_SPEC.md, 12): everything in order, then results/RESULTS.md.

.DESCRIPTION
    Steps:
      1. Build      dotnet build IronMapper.Comparison.slnx -c Release
      2. Tests      dotnet test (gate: nothing is measured unless the equivalence tests pass; writes feature-matrix.md)
      3. Runtime    BenchmarkDotNet: Flat, Nested, Collection, CustomConfig, InPlace, DiOverhead
                    full: --launchCount 3 (every benchmark in 3 processes, see README "Dynamic PGO"); quick: --job short
      4. Startup    run-startup.ps1 -Mode Both: JIT + NativeAOT process start, BDN StartupBenchmark → startup.md
      5. AOT        run-aot.ps1 -ReportOnly (uses the NativeAOT publish of step 4) → aot.md, aot-size-breakdown.md
      6. Footprint  run-footprint.ps1 → footprint.md
      7. Build time run-build-time.ps1 → build-time.md
      8. Report     generate-report.ps1 → RESULTS.md

    Before starting, prints the expected duration and checks the environment: .NET SDK 10, free memory (NativeAOT),
    AutoMapper license key (Test-Path only), NativeAOT toolchain. A missing key or toolchain is a warning
    (AutoMapper logs a license warning; NativeAOT probes are reported as "publish failed"); too little free memory
    stops the run unless -Force.

    -Quick checks that everything works in a fraction of the time: BDN --job short, 5 startup runs, build time with
    N = 20 and 2 builds. RESULTS.md then carries a "Quick run" warning; its numbers are not for publication.

    Run durations are written to results/raw/run-info.json after every step (RESULTS.md shows them).

.EXAMPLE
    pwsh ./scripts/run-all.ps1              # full run
.EXAMPLE
    pwsh ./scripts/run-all.ps1 -Quick       # smoke test of the whole pipeline
.EXAMPLE
    pwsh ./scripts/run-all.ps1 -CheckOnly   # only the duration estimate and the environment checks
#>
[CmdletBinding()]
param(
    [switch] $Quick,
    # Only print the estimate and the environment checks.
    [switch] $CheckOnly,
    # Continue although less than MinFreeMemoryGB is free (passed on to run-startup.ps1 as well).
    [switch] $Force,
    # NativeAOT with a single ILC thread (less memory, slower); passed to run-startup.ps1.
    [switch] $LowMemory,
    [double] $MinFreeMemoryGB = 4
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'BenchmarkCommon.psm1') -Force

$benchmarksDir = Get-BenchmarksDir
$solution = Join-Path $benchmarksDir 'IronMapper.Comparison.slnx'
$logDir = Join-Path (Get-ResultsRawDir) 'run-all'
$mode = $Quick ? 'Quick' : 'Full'
$runtimeClasses = 'Flat', 'Nested', 'Collection', 'CustomConfig', 'InPlace', 'DiOverhead'

<#
    Expected duration per step, minutes, measured / extrapolated on the reference machine
    (Intel Core i7-12650H, 64 GB, Windows 11, .NET 10.0.12, 2026-10-18). Runtime (full): 53 benchmark cases
    × 3 launches × ~25 s per launch (default BDN job; the GC-heavy Collection 100 000 cases take longer).
#>
$estimates = [ordered]@{
    Build     = @{ Full = 1; Quick = 1 }
    Tests     = @{ Full = 1; Quick = 1 }
    Runtime   = @{ Full = 70; Quick = 8 }
    Startup   = @{ Full = 8; Quick = 3 }
    AOT       = @{ Full = 1; Quick = 1 }
    Footprint = @{ Full = 1; Quick = 1 }
    BuildTime = @{ Full = 10; Quick = 2 }
    Report    = @{ Full = 1; Quick = 1 }
}

# ---------------------------------------------------------------------------------------------------------------------
# Estimate and environment checks
# ---------------------------------------------------------------------------------------------------------------------

$total = ($estimates.Values | ForEach-Object { $_[$mode] } | Measure-Object -Sum).Sum
Write-Host "IronMapper comparison benchmarks — $mode run"
Write-Host ("Expected duration on the reference machine: ~{0} min ({1})" -f $total,
    (($estimates.Keys | ForEach-Object { "$_ $($estimates[$_][$mode])" }) -join ', '))
Write-Host 'Close other applications and keep the machine on mains power: background load changes the numbers.'
Write-Host

$problems = @()
function Write-Check([string] $name, [bool] $ok, [string] $detail, [switch] $Fatal) {
    $mark = if ($ok) { 'ok  ' } elseif ($Fatal) { 'FAIL' } else { 'warn' }
    Write-Host ("  [{0}] {1}: {2}" -f $mark, $name, $detail)
    if (-not $ok -and $Fatal) { $script:problems += "${name}: $detail" }
}

Write-Host 'Environment:'
$sdk = (& dotnet --version).Trim()
Write-Check '.NET SDK' ($sdk -like '10.*') "dotnet --version = $sdk (10.0.4xx expected)" -Fatal
$free = Get-FreeMemoryBytes
if ($null -eq $free) { Write-Check 'Free memory' $false 'cannot be determined' }
else {
    $freeGB = $free / 1GB
    $okMemory = $freeGB -ge $MinFreeMemoryGB
    Write-Check 'Free memory' ($okMemory -or $Force) ([string]::Format([cultureinfo]::InvariantCulture, "{0:N1} GB free, {1} GB needed for the NativeAOT publish{2}", $freeGB, $MinFreeMemoryGB, ((-not $okMemory -and $Force) ? ' (-Force)' : ''))) -Fatal
}
$license = Test-AutoMapperLicense
Write-Check 'AutoMapper license' $license.Ok $license.Detail
$toolchain = Test-NativeToolchain
Write-Check 'NativeAOT toolchain' $toolchain.Ok ($toolchain.Ok ? $toolchain.Detail : "$($toolchain.Detail); the NativeAOT probes will be reported as 'publish failed'")
Write-Host

if ($problems.Count) { throw "Environment check failed:`n  $($problems -join "`n  ")" }
if ($CheckOnly) { return }

# ---------------------------------------------------------------------------------------------------------------------
# Steps
# ---------------------------------------------------------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$started = Get-Date
$runInfo = [ordered]@{ Mode = $mode; Started = (Get-UtcStamp); TotalSeconds = 0; Steps = [System.Collections.Generic.List[object]]::new() }

function Save-RunInfo {
    $runInfo.TotalSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds)
    $runInfo | ConvertTo-Json -Depth 4 | Set-Content -Path (Get-RunInfoPath) -Encoding utf8NoBOM
}

function Invoke-Step([string] $name, [scriptblock] $action) {
    $estimate = $estimates[$name][$mode]
    Write-Host ("==> {0} (~{1} min) — {2:HH:mm}" -f $name, $estimate, (Get-Date))
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $action
    $sw.Stop()
    $runInfo.Steps.Add([ordered]@{ Name = $name; Seconds = [math]::Round($sw.Elapsed.TotalSeconds) })
    Save-RunInfo
    Write-Host ("    done in {0:h\:mm\:ss}" -f $sw.Elapsed)
}

# Runs dotnet with the output in a log file; throws with the log tail on failure.
function Invoke-Dotnet([string] $logName, [string[]] $arguments) {
    $log = Join-Path $logDir $logName
    & dotnet @arguments *> $log
    if ($LASTEXITCODE -ne 0) {
        $tail = (Get-Content $log -Tail 30) -join "`n"
        throw "dotnet $($arguments[0]) failed (exit code $LASTEXITCODE); log: $log`n$tail"
    }
}

Invoke-Step 'Build' { Invoke-Dotnet 'build.log' @('build', $solution, '-c', 'Release', '-nologo') }

Invoke-Step 'Tests' {
    # The gate of BENCHMARK_SPEC.md, 6: no measurements unless the mappers produce equivalent results.
    Invoke-Dotnet 'tests.log' @('test', $solution, '-c', 'Release', '--no-build', '-nologo')
}

Invoke-Step 'Runtime' {
    $project = Join-Path $benchmarksDir 'src' 'Comparison.Benchmarks' 'Comparison.Benchmarks.csproj'
    $filters = $runtimeClasses | ForEach-Object { "*.${_}Benchmark.*" }
    $job = $Quick ? @('--job', 'short') : @('--launchCount', '3')
    $stepStart = Get-Date
    Invoke-Dotnet 'bdn-runtime.log' (@('run', '-c', 'Release', '-f', 'net10.0', '--project', $project, '--no-build', '--', '--filter') + $filters + $job)
    $bdnResults = Join-Path (Get-ResultsRawDir) 'bdn' 'results'
    foreach ($class in $runtimeClasses) {
        $json = Join-Path $bdnResults "Comparison.Benchmarks.${class}Benchmark-report-full.json"
        if (-not (Test-Path $json) -or (Get-Item $json).LastWriteTime -lt $stepStart) {
            throw "BenchmarkDotNet produced no report for ${class}Benchmark; see $(Join-Path $logDir 'bdn-runtime.log')"
        }
    }
}

Invoke-Step 'Startup' {
    $startupArgs = @{ Mode = 'Both'; Force = $Force; LowMemory = $LowMemory; MinFreeMemoryGB = $MinFreeMemoryGB }
    if ($Quick) { $startupArgs.Runs = 5; $startupArgs.WarmupRuns = 1 }
    & (Join-Path $PSScriptRoot 'run-startup.ps1') @startupArgs
}

Invoke-Step 'AOT' { & (Join-Path $PSScriptRoot 'run-aot.ps1') -ReportOnly }

Invoke-Step 'Footprint' { & (Join-Path $PSScriptRoot 'run-footprint.ps1') }

Invoke-Step 'BuildTime' {
    if ($Quick) { & (Join-Path $PSScriptRoot 'run-build-time.ps1') -Sizes 20 -Runs 2 -WarmupRuns 1 }
    else { & (Join-Path $PSScriptRoot 'run-build-time.ps1') }
}

Invoke-Step 'Report' { & (Join-Path $PSScriptRoot 'generate-report.ps1') }

Write-Host ("All done in {0:h\:mm\:ss}. Results: {1}" -f ((Get-Date) - $started), (Join-Path (Get-ResultsDir) 'RESULTS.md'))

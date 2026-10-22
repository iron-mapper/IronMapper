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
         RuntimeNet8 the same on net8.0 (raw/bdn-net8.0; RESULTS.md shows it in its own section); off with -SkipNet8
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
    -Dry: BDN --job Dry (one iteration), 2 startup runs, build time N = 5 with 1 build: only checks that every command
    works (a few minutes); its numbers mean nothing.

    -ResultsDir writes everything (reports and raw data) to another folder than results/, e.g. results/1.2.0 (relative to
    benchmarks/), through the environment variable IRONMAPPER_RESULTS_DIR. -UseLocalIronMapper measures the local
    sources of ../src instead of the IronMapper package of Directory.Packages.props: the build, the tests and the
    runtime benchmarks through project references (the environment variable UseLocalIronMapper=true, which every build
    of the run, BenchmarkDotNet's included, reads as an MSBuild property); cold start, NativeAOT, size and build time
    through the local sources packed as <Version>-local.<timestamp> into raw/local-packages (IronMapperPackageVersion,
    IronMapperPackageSource), because a project reference adds the .pdb and XML docs to the publish folder and rebuilds
    ../src inside every timed `dotnet build --no-incremental`. RESULTS.md names the source.

    Run durations are written to results/raw/run-info.json after every step (RESULTS.md shows them).

.EXAMPLE
    pwsh ./scripts/run-all.ps1              # full run
.EXAMPLE
    pwsh ./scripts/run-all.ps1 -Quick       # smoke test of the whole pipeline
.EXAMPLE
    pwsh ./scripts/run-all.ps1 -CheckOnly   # only the duration estimate and the environment checks
.EXAMPLE
    pwsh ./scripts/run-all.ps1 -UseLocalIronMapper -ResultsDir results/1.2.0    # the release run of the local sources
#>
[CmdletBinding()]
param(
    [switch] $Quick,
    # One BDN iteration and minimal startup/build-time runs: checks the commands only.
    [switch] $Dry,
    # Folder for this run's reports and raw data, relative to benchmarks/ (default: results).
    [string] $ResultsDir,
    # Measure the local sources of ../src instead of the published IronMapper package.
    [switch] $UseLocalIronMapper,
    # Do not run the runtime benchmarks on net8.0.
    [switch] $SkipNet8,
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

# Set for this process and the processes it starts (dotnet, BenchmarkDotNet, the other scripts); restored at the end.
$previousResultsDir = $env:IRONMAPPER_RESULTS_DIR
$previousUseLocal = $env:UseLocalIronMapper
$previousPackageVersion = $env:IronMapperPackageVersion
$previousPackageSource = $env:IronMapperPackageSource
if ($ResultsDir) { $env:IRONMAPPER_RESULTS_DIR = $ResultsDir }
$env:UseLocalIronMapper = $UseLocalIronMapper ? 'true' : 'false'

$benchmarksDir = Get-BenchmarksDir
$solution = Join-Path $benchmarksDir 'IronMapper.Comparison.slnx'
$logDir = Join-Path (Get-ResultsRawDir) 'run-all'
$mode = $Dry ? 'Dry' : $Quick ? 'Quick' : 'Full'
$runtimeClasses = 'Flat', 'Nested', 'Collection', 'CustomConfig', 'InPlace', 'DiOverhead'

<#
    Expected duration per step, minutes, measured / extrapolated on the reference machine
    (Intel Core i7-12650H, 64 GB, Windows 11, .NET 10.0.12, 2026-10-18). Runtime (full): 53 benchmark cases
    × 3 launches × ~25 s per launch (default BDN job; the GC-heavy Collection 100 000 cases take longer).
#>
$estimates = [ordered]@{
    Build       = @{ Full = 1; Quick = 1; Dry = 1 }
    Tests       = @{ Full = 1; Quick = 1; Dry = 1 }
    Runtime     = @{ Full = 70; Quick = 8; Dry = 2 }
    RuntimeNet8 = @{ Full = 70; Quick = 8; Dry = 2 }
    Startup     = @{ Full = 8; Quick = 3; Dry = 3 }
    AOT         = @{ Full = 1; Quick = 1; Dry = 1 }
    Footprint   = @{ Full = 1; Quick = 1; Dry = 1 }
    BuildTime   = @{ Full = 10; Quick = 2; Dry = 1 }
    Report      = @{ Full = 1; Quick = 1; Dry = 1 }
}
if ($SkipNet8) { $estimates.Remove('RuntimeNet8') }

# ---------------------------------------------------------------------------------------------------------------------
# Estimate and environment checks
# ---------------------------------------------------------------------------------------------------------------------

$total = ($estimates.Values | ForEach-Object { $_[$mode] } | Measure-Object -Sum).Sum
Write-Host "IronMapper comparison benchmarks — $mode run"
Write-Host ("IronMapper: {0}; results: {1}" -f ((Test-UseLocalIronMapper) ? 'local sources (../src)' : 'package of Directory.Packages.props'),
    (Get-ResultsDirDisplay))
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

function Restore-Environment {
    $env:IRONMAPPER_RESULTS_DIR = $previousResultsDir
    $env:UseLocalIronMapper = $previousUseLocal
    $env:IronMapperPackageVersion = $previousPackageVersion
    $env:IronMapperPackageSource = $previousPackageSource
}

if ($problems.Count) { Restore-Environment; throw "Environment check failed:`n  $($problems -join "`n  ")" }
if ($CheckOnly) { Restore-Environment; return }
try {

# ---------------------------------------------------------------------------------------------------------------------
# Steps
# ---------------------------------------------------------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$started = Get-Date
# The commit the run measures, so a report regenerated later names it rather than the HEAD of that moment.
# Generated results (benchmarks/results/**) are not changes of the code under test.
$runCommit = (& git -C $benchmarksDir rev-parse --short HEAD 2>$null)
$runDirty = [bool] (& git -C $benchmarksDir status --porcelain -- .. ':(exclude)results' 2>$null)
$runInfo = [ordered]@{ Mode = $mode; Started = (Get-UtcStamp); TotalSeconds = 0
    IronMapper = (Test-UseLocalIronMapper) ? 'local' : 'package'; ResultsDir = (Get-ResultsDirDisplay)
    Commit = $runCommit; UncommittedChanges = $runDirty
    Steps = [System.Collections.Generic.List[object]]::new() }

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

# The runtime benchmarks on one target framework; BenchmarkDotNet writes raw/bdn (net10.0) or raw/bdn-net8.0 (Program.cs).
function Invoke-RuntimeBenchmarks([string] $framework, [string] $folder, [string] $logName) {
    $project = Join-Path $benchmarksDir 'src' 'Comparison.Benchmarks' 'Comparison.Benchmarks.csproj'
    $filters = $runtimeClasses | ForEach-Object { "*.${_}Benchmark.*" }
    $job = $Dry ? @('--job', 'Dry') : $Quick ? @('--job', 'short') : @('--launchCount', '3')
    $stepStart = Get-Date
    Invoke-Dotnet $logName (@('run', '-c', 'Release', '-f', $framework, '--project', $project, '--no-build', '--', '--filter') + $filters + $job)
    $bdnResults = Join-Path (Get-ResultsRawDir) $folder 'results'
    foreach ($class in $runtimeClasses) {
        $json = Join-Path $bdnResults "Comparison.Benchmarks.${class}Benchmark-report-full.json"
        if (-not (Test-Path $json) -or (Get-Item $json).LastWriteTime -lt $stepStart) {
            throw "BenchmarkDotNet produced no report for ${class}Benchmark ($framework); see $(Join-Path $logDir $logName)"
        }
    }
}

Invoke-Step 'Runtime' { Invoke-RuntimeBenchmarks 'net10.0' 'bdn' 'bdn-runtime.log' }

if (-not $SkipNet8) { Invoke-Step 'RuntimeNet8' { Invoke-RuntimeBenchmarks 'net8.0' 'bdn-net8.0' 'bdn-runtime-net8.0.log' } }

# From here on the local sources are measured as a package (see the description).
if ($UseLocalIronMapper) {
    $packageDir = Join-Path (Get-ResultsRawDir) 'local-packages'
    $env:IronMapperPackageVersion = New-LocalIronMapperPackage $packageDir (Join-Path $logDir 'pack.log')
    $env:IronMapperPackageSource = $packageDir
    $env:UseLocalIronMapper = 'false'
    $runInfo.LocalPackageVersion = $env:IronMapperPackageVersion
    Save-RunInfo
    Write-Host "IronMapper from here on: local sources packed as $($env:IronMapperPackageVersion)"
}

Invoke-Step 'Startup' {
    $startupArgs = @{ Mode = 'Both'; Force = $Force; LowMemory = $LowMemory; MinFreeMemoryGB = $MinFreeMemoryGB }
    if ($Dry) { $startupArgs.Runs = 2; $startupArgs.WarmupRuns = 1 }
    elseif ($Quick) { $startupArgs.Runs = 5; $startupArgs.WarmupRuns = 1 }
    & (Join-Path $PSScriptRoot 'run-startup.ps1') @startupArgs
}

Invoke-Step 'AOT' { & (Join-Path $PSScriptRoot 'run-aot.ps1') -ReportOnly }

Invoke-Step 'Footprint' { & (Join-Path $PSScriptRoot 'run-footprint.ps1') }

Invoke-Step 'BuildTime' {
    if ($Dry) { & (Join-Path $PSScriptRoot 'run-build-time.ps1') -Sizes 5 -Runs 1 -WarmupRuns 1 }
    elseif ($Quick) { & (Join-Path $PSScriptRoot 'run-build-time.ps1') -Sizes 20 -Runs 2 -WarmupRuns 1 }
    else { & (Join-Path $PSScriptRoot 'run-build-time.ps1') }
}

Invoke-Step 'Report' { & (Join-Path $PSScriptRoot 'generate-report.ps1') }

Write-Host ("All done in {0:h\:mm\:ss}. Results: {1}" -f ((Get-Date) - $started), (Join-Path (Get-ResultsDir) 'RESULTS.md'))
}
finally { Restore-Environment }

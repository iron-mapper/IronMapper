#Requires -Version 7.0
<#
.SYNOPSIS
    Cold start measurements (BENCHMARK_SPEC.md, 8): process start of the StartupProbe.* consoles (JIT and NativeAOT)
    and the in-process BenchmarkDotNet StartupBenchmark. Writes results/startup.md.

.DESCRIPTION
    1. Publishes every probe framework-dependent (JIT) and/or with NativeAOT for the current RID. NativeAOT probes are
       published strictly one after another, with `dotnet build-server shutdown` after each one (ILC needs a lot of
       memory); before that the script checks the free memory. NativeAOT publishes also keep every trim/AOT warning
       separately and the mstat size file (results/raw/startup/mstat/), which run-aot.ps1 and run-footprint.ps1 use.
       Publishing and the toolchain setup live in BenchmarkCommon.psm1.
    2. Runs each probe WarmupRuns times (disk cache), then Runs times. The runs are interleaved (run 1 of every probe,
       then run 2 of every probe, ...), so a slow phase of the machine is spread over all probes instead of hitting one.
    3. Wall-clock time from Process.Start to exit (stdout read to the end): median / p95 / min / mean.
       Every run must exit with 0 and print the same checksum as the Baseline probe (the mapping really ran).
       Each mode writes its own CSV: results/raw/startup-jit.csv, results/raw/startup-aot.csv. The full stderr of a
       failed run is kept in results/raw/startup/run-<mode>-<probe>.stderr.txt.
    4. Runs StartupBenchmark (BDN, RunStrategy.ColdStart, 20 launches) when the JIT mode is measured.
    5. Builds results/startup.md from every CSV that exists and the BDN report, so the modes can be run separately.

    Works with PowerShell 7 on Windows, Linux and macOS. NativeAOT needs the platform toolchain
    (Windows: MSVC build tools; Linux: clang + zlib; macOS: Xcode command line tools); if publishing fails, the probe
    is reported as "publish failed" and the log is kept in results/raw/startup/.

.EXAMPLE
    pwsh ./scripts/run-startup.ps1                      # both modes + BDN
.EXAMPLE
    pwsh ./scripts/run-startup.ps1 -Mode Jit -SkipPublish
    pwsh ./scripts/run-startup.ps1 -Mode Aot -LowMemory
.EXAMPLE
    pwsh ./scripts/run-startup.ps1 -ReportOnly          # rebuild startup.md from the existing CSVs
#>
[CmdletBinding()]
param(
    [int] $Runs = 30,
    [int] $WarmupRuns = 3,
    [ValidateSet('Jit', 'Aot', 'Both')]
    [string] $Mode = 'Both',
    [string[]] $Probes = @('Baseline', 'Manual', 'IronMapper', 'IronMapper_IMapper', 'Mapperly', 'Mapster', 'AutoMapper'),
    # Reuse the published probes from a previous run.
    [switch] $SkipPublish,
    # Skip the in-process BenchmarkDotNet StartupBenchmark (otherwise it runs together with the JIT mode).
    [switch] $SkipBdn,
    # NativeAOT: compile with a single ILC thread (IlcSingleThreaded=true → `ilc --parallelism:1`). Slower, less memory.
    [switch] $LowMemory,
    # Do not ask for confirmation when less than MinFreeMemoryGB is free before the NativeAOT publish.
    [switch] $Force,
    [double] $MinFreeMemoryGB = 4,
    # Only rebuild results/startup.md from the existing CSVs and the BDN report.
    [switch] $ReportOnly,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'BenchmarkCommon.psm1') -Force

$reportPath = Join-Path (Get-ResultsDir) 'startup.md'
$bdnReport = Join-Path (Get-ResultsRawDir) 'bdn' 'results' 'Comparison.Benchmarks.StartupBenchmark-report-github.md'
$allModes = @('Jit', 'Aot')

function Confirm-FreeMemory {
    $free = Get-FreeMemoryBytes
    if ($null -eq $free) {
        Write-Warning 'Free memory could not be determined.'
        return
    }
    $freeGB = $free / 1GB
    Write-Host ("Free memory before NativeAOT publish: {0:N1} GB" -f $freeGB)
    if ($freeGB -ge $MinFreeMemoryGB -or $Force) { return }

    Write-Warning ("Less than {0} GB of memory is free; NativeAOT compilation may run out of memory. Consider -LowMemory." -f $MinFreeMemoryGB)
    $interactive = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected
    if (-not $interactive) {
        throw 'Not enough free memory and no interactive console to confirm. Free memory, or run again with -Force.'
    }
    $answer = Read-Host 'Continue anyway? [y/N]'
    if ($answer -notmatch '^(y|yes)$') { throw 'Aborted by the user.' }
}

function Invoke-ProbeOnce([string] $exe) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = Split-Path -Parent $exe

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        throw "Timeout after $TimeoutSeconds s: $exe"
    }
    $sw.Stop()

    return [pscustomobject]@{
        Ms       = $sw.Elapsed.TotalMilliseconds
        ExitCode = $process.ExitCode
        Output   = $stdoutTask.Result.Trim()
        Error    = $stderrTask.Result.Trim()
    }
}

# Publishes (unless -SkipPublish), runs and writes results/raw/startup-<mode>.csv for one mode.
function Measure-Mode([string] $m, [string] $rid) {
    if ($m -eq 'Aot') { Initialize-NativeToolchain }
    if ($SkipPublish) {
        $published = foreach ($probe in $Probes) {
            $dir = Get-ProbeDir $m $probe
            [pscustomobject]@{ Probe = $probe; Mode = $m; Ok = (Test-Path (Get-ProbeExecutable $dir $probe)); Dir = $dir; Log = '' }
        }
    }
    else {
        if ($m -eq 'Aot') { Confirm-FreeMemory }
        # Strictly sequential: one probe at a time.
        $published = foreach ($probe in $Probes) {
            Write-Host "Publishing $probe ($m)..."
            Publish-StartupProbe -Probe $probe -Mode $m -Rid $rid -LowMemory:$LowMemory
        }
    }
    foreach ($p in $published | Where-Object { -not $_.Ok }) {
        Write-Warning "Publish failed or missing: $($p.Probe) ($m)$($p.Log ? "; see $($p.Log)" : '')"
    }

    $runnable = @($published | Where-Object Ok)
    $samples = @{}; $outputs = @{}; $failures = @{}
    foreach ($p in $runnable) {
        $samples[$p.Probe] = [System.Collections.Generic.List[double]]::new()
        $outputs[$p.Probe] = [System.Collections.Generic.HashSet[string]]::new()
        $failures[$p.Probe] = $null
        $errorPath = Get-RunErrorPath $m $p.Probe
        if (Test-Path $errorPath) { Remove-Item -Force $errorPath }
    }

    $total = $WarmupRuns + $Runs
    for ($i = 1; $i -le $total; $i++) {
        $isWarmup = $i -le $WarmupRuns
        Write-Progress -Activity "Startup probes ($m)" -Status ($isWarmup ? "warmup $i/$WarmupRuns" : "run $($i - $WarmupRuns)/$Runs") -PercentComplete (100 * $i / $total)
        foreach ($p in $runnable) {
            if ($failures[$p.Probe]) { continue }
            try { $r = Invoke-ProbeOnce (Get-ProbeExecutable $p.Dir $p.Probe) }
            catch { $failures[$p.Probe] = $_.Exception.Message; continue }
            if ($r.ExitCode -ne 0) {
                # The full stderr (exception, inner exceptions, stack) is kept for the reports.
                Set-Content -Path (Get-RunErrorPath $m $p.Probe) -Value $r.Error -Encoding utf8NoBOM
                $firstLine = ($r.Error -split '?
' | Select-Object -First 1).Trim()
                $failures[$p.Probe] = "exit code $($r.ExitCode): $firstLine"
                continue
            }
            [void] $outputs[$p.Probe].Add($r.Output)
            if (-not $isWarmup) { $samples[$p.Probe].Add($r.Ms) }
        }
    }
    Write-Progress -Activity "Startup probes ($m)" -Completed

    $sdk = (& dotnet --version).Trim()
    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm', [cultureinfo]::InvariantCulture)
    $os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    $rows = foreach ($p in $published) {
        $row = [ordered]@{
            Mode = $m; Probe = $p.Probe; Status = ''; Runs = 0
            MedianMs = ''; P95Ms = ''; MinMs = ''; MeanMs = ''; Checksum = ''
            Date = $date; Os = $os; Rid = $rid; Sdk = $sdk; WarmupRuns = $WarmupRuns
        }
        if (-not $p.Ok) { $row.Status = 'publish failed' }
        elseif ($failures[$p.Probe]) { $row.Status = "fails at runtime: $($failures[$p.Probe])" }
        else {
            $values = [double[]] ($samples[$p.Probe] | Sort-Object)
            $distinct = @($outputs[$p.Probe])
            $row.Runs = $values.Count
            $row.MedianMs = ConvertTo-InvariantNumber (Get-Percentile $values 0.5)
            $row.P95Ms = ConvertTo-InvariantNumber (Get-Percentile $values 0.95)
            $row.MinMs = ConvertTo-InvariantNumber $values[0]
            $row.MeanMs = ConvertTo-InvariantNumber (($values | Measure-Object -Average).Average)
            $row.Checksum = ($distinct.Count -eq 1) ? $distinct[0] : "inconsistent: $($distinct -join ' ')"
            $row.Status = ($distinct.Count -eq 1) ? 'ok' : 'checksum differs between runs'
        }
        [pscustomobject] $row
    }
    $csv = Get-StartupCsvPath $m
    $rows | Export-Csv -Path $csv -NoTypeInformation -Encoding utf8NoBOM
    Write-Host "Written: $csv"
}

function Write-StartupReport {
    $data = foreach ($m in $allModes) {
        $csv = Get-StartupCsvPath $m
        if (Test-Path $csv) { [pscustomobject]@{ Mode = $m; Rows = @(Import-Csv $csv) } }
    }

    # Expected checksum: the Baseline output (the source checksum, no mapper) of any mode.
    $expected = $data | ForEach-Object { $_.Rows } |
        Where-Object { $_.Probe -eq 'Baseline' -and $_.Status -eq 'ok' } | Select-Object -First 1 -ExpandProperty Checksum
    $jitChecksums = @{}
    $jit = $data | Where-Object Mode -eq 'Jit'
    if ($jit) { foreach ($r in $jit.Rows) { $jitChecksums[$r.Probe] = $r.Checksum } }

    $sb = [System.Text.StringBuilder]::new()
    [void] $sb.AppendLine('# Cold start')
    [void] $sb.AppendLine()
    [void] $sb.AppendLine('> Generated by `scripts/run-startup.ps1` from `results/raw/startup-*.csv` and the BenchmarkDotNet report. Do not edit by hand.')
    [void] $sb.AppendLine()
    [void] $sb.AppendLine('## Process start (`probes/StartupProbe.*`)')
    [void] $sb.AppendLine()
    [void] $sb.AppendLine('Each probe creates one order, sets up its mapper (the same configuration as the runtime benchmarks), maps the order, ' +
        'prints a checksum and exits. Time is wall-clock from `Process.Start` to process exit, measured by the script; ' +
        'the launch overhead of the script is the same for every probe, so compare the Δ column. ' +
        '`Baseline` creates the same order and prints the checksum of the source without any mapper. ' +
        'Runs are interleaved across probes after the warmup runs.')
    [void] $sb.AppendLine()
    if ($expected) { [void] $sb.AppendLine("Expected checksum (Baseline): ``$expected``. Status `ok` means every run exited with 0 and printed this value.") }
    [void] $sb.AppendLine()

    if (-not $data) { [void] $sb.AppendLine('_No measurements yet: run `scripts/run-startup.ps1`._'); [void] $sb.AppendLine() }

    foreach ($d in $data) {
        $title = if ($d.Mode -eq 'Jit') { 'Framework-dependent (JIT)' } else { 'NativeAOT' }
        $meta = $d.Rows | Select-Object -First 1
        [void] $sb.AppendLine("### $title")
        [void] $sb.AppendLine()
        [void] $sb.AppendLine("$($meta.Date) UTC · $($meta.Os) ($($meta.Rid)) · .NET SDK $($meta.Sdk) · $(($d.Rows | ForEach-Object { [int] $_.Runs } | Measure-Object -Maximum).Maximum) runs after $($meta.WarmupRuns) warmup runs")
        [void] $sb.AppendLine()
        [void] $sb.AppendLine('| Probe | Median, ms | p95, ms | Min, ms | Mean, ms | Δ median vs Baseline, ms | Status |')
        [void] $sb.AppendLine('|---|---:|---:|---:|---:|---:|---|')
        $baseline = $d.Rows | Where-Object { $_.Probe -eq 'Baseline' -and $_.Status -eq 'ok' } | Select-Object -First 1
        foreach ($r in $d.Rows) {
            $status = $r.Status
            if ($status -eq 'ok' -and $expected -and $r.Checksum -ne $expected) { $status = "wrong checksum ($($r.Checksum))" }
            if ($status -eq 'ok' -and $d.Mode -eq 'Aot' -and $jitChecksums.ContainsKey($r.Probe) -and $jitChecksums[$r.Probe] -ne $r.Checksum) {
                $status = 'checksum differs from JIT'
            }
            $delta = if ($r.MedianMs -and $baseline) { Format-Ms ([double] $r.MedianMs - [double] $baseline.MedianMs) } else { '–' }
            [void] $sb.AppendLine("| $($r.Probe) | $(Format-Ms $r.MedianMs) | $(Format-Ms $r.P95Ms) | $(Format-Ms $r.MinMs) | $(Format-Ms $r.MeanMs) | $delta | $(Format-Cell $status) |")
        }
        [void] $sb.AppendLine()
    }

    [void] $sb.AppendLine('## In-process cold start (BenchmarkDotNet `StartupBenchmark`)')
    [void] $sb.AppendLine()
    [void] $sb.AppendLine('`RunStrategy.ColdStart`, 20 launches, 1 invocation per launch: create the configuration from scratch ' +
        '(nothing for the source generators) and map the first order. Shows the cost of building the configuration ' +
        '(expression trees, reflection) and of the first-call JIT, without process start.')
    [void] $sb.AppendLine()
    if (Test-Path $bdnReport) { [void] $sb.AppendLine((Get-Content $bdnReport -Raw).Trim()) }
    else { [void] $sb.AppendLine('_No BenchmarkDotNet report yet: it runs together with `-Mode Jit` or `-Mode Both`._') }
    [void] $sb.AppendLine()

    Write-ReportFile $reportPath $sb.ToString()
}

New-Item -ItemType Directory -Force -Path (Get-StartupLogDir) | Out-Null

if (-not $ReportOnly) {
    $rid = Get-CurrentRid
    $modes = if ($Mode -eq 'Both') { $allModes } else { @($Mode) }
    Write-Host "RID: $rid; modes: $($modes -join ', '); runs: $Runs (+$WarmupRuns warmup)$($LowMemory ? '; ILC single-threaded' : '')"

    foreach ($m in $modes) { Measure-Mode $m $rid }

    if ('Jit' -in $modes -and -not $SkipBdn) {
        Write-Host 'Running StartupBenchmark (BenchmarkDotNet, ColdStart)...'
        $bdnLog = Join-Path (Get-StartupLogDir) 'bdn-startup.log'
        $project = Join-Path (Get-BenchmarksDir) 'src' 'Comparison.Benchmarks' 'Comparison.Benchmarks.csproj'
        & dotnet run -c Release -f net10.0 --project $project -- --filter '*StartupBenchmark*' *> $bdnLog
        if ($LASTEXITCODE -ne 0) { Write-Warning "StartupBenchmark failed; see $bdnLog" }
    }
}

Write-StartupReport

#Requires -Version 7.0
<#
.SYNOPSIS
    Cold start measurements (BENCHMARK_SPEC.md, 8): process start of the StartupProbe.* consoles (JIT and NativeAOT)
    and the in-process BenchmarkDotNet StartupBenchmark. Writes results/startup.md.

.DESCRIPTION
    1. Publishes every probe framework-dependent (JIT) and/or with NativeAOT for the current RID.
    2. Runs each probe WarmupRuns times (disk cache), then Runs times. The runs are interleaved (run 1 of every probe,
       then run 2 of every probe, ...), so a slow phase of the machine is spread over all probes instead of hitting one.
    3. Wall-clock time from Process.Start to exit (stdout read to the end), median / p95 / min / mean.
       Every run must exit with 0 and print the same checksum as the Baseline probe (the mapping really ran).
    4. Runs StartupBenchmark (BDN, RunStrategy.ColdStart, 20 launches) and embeds its table.

    Works with PowerShell 7 on Windows, Linux and macOS. NativeAOT needs the platform toolchain
    (Windows: MSVC build tools; Linux: clang + zlib; macOS: Xcode command line tools); if publishing fails, the probe
    is reported as "publish failed" and the log is kept in results/raw/startup/.

.EXAMPLE
    pwsh ./scripts/run-startup.ps1
.EXAMPLE
    pwsh ./scripts/run-startup.ps1 -Mode Jit -Runs 10 -SkipBdn
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
    # Skip the in-process BenchmarkDotNet StartupBenchmark.
    [switch] $SkipBdn,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarksDir = Split-Path -Parent $PSScriptRoot
$rawDir = Join-Path $benchmarksDir 'results' 'raw' 'startup'
$publishRoot = Join-Path $rawDir 'publish'
$reportPath = Join-Path $benchmarksDir 'results' 'startup.md'
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

function Get-CurrentRid {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    return "$os-$arch"
}

function Get-ProbeExecutable([string] $dir, [string] $probe) {
    $name = "StartupProbe.$probe"
    if ($IsWindows) { $name += '.exe' }
    return Join-Path $dir $name
}

function Publish-Probe([string] $probe, [string] $mode, [string] $rid) {
    $project = Join-Path $benchmarksDir 'probes' "StartupProbe.$probe" "StartupProbe.$probe.csproj"
    $outDir = Join-Path $publishRoot $mode.ToLowerInvariant() $probe
    $log = Join-Path $rawDir "publish-$($mode.ToLowerInvariant())-$probe.log"
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

    # --disable-build-servers: no MSBuild/compiler server processes stay in memory between the publishes
    # (NativeAOT compilation itself needs a lot of memory).
    $publishArgs = @('publish', $project, '-c', 'Release', '-r', $rid, '-o', $outDir, '-nologo', '--disable-build-servers')
    if ($mode -eq 'Aot') { $publishArgs += '-p:PublishAot=true' }
    else { $publishArgs += @('--self-contained', 'false') }

    & dotnet @publishArgs *> $log
    $ok = $LASTEXITCODE -eq 0 -and (Test-Path (Get-ProbeExecutable $outDir $probe))
    return [pscustomobject]@{ Probe = $probe; Mode = $mode; Ok = $ok; Dir = $outDir; Log = $log }
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

function Get-Percentile([double[]] $sorted, [double] $p) {
    # Nearest-rank method.
    $rank = [math]::Ceiling($p * $sorted.Count)
    return $sorted[[math]::Max(0, $rank - 1)]
}

function Format-Ms([double] $value) { $value.ToString('N1', [cultureinfo]::InvariantCulture) }

$rid = Get-CurrentRid
$modes = if ($Mode -eq 'Both') { @('Jit', 'Aot') } else { @($Mode) }
Write-Host "RID: $rid; modes: $($modes -join ', '); runs: $Runs (+$WarmupRuns warmup)"

# --- 1. Publish --------------------------------------------------------------------------------------------------
$published = foreach ($m in $modes) {
    foreach ($probe in $Probes) {
        if ($SkipPublish) {
            $dir = Join-Path $publishRoot $m.ToLowerInvariant() $probe
            [pscustomobject]@{ Probe = $probe; Mode = $m; Ok = (Test-Path (Get-ProbeExecutable $dir $probe)); Dir = $dir; Log = '' }
        }
        else {
            Write-Host "Publishing $probe ($m)..."
            Publish-Probe $probe $m $rid
        }
    }
}
foreach ($p in $published | Where-Object { -not $_.Ok }) {
    Write-Warning "Publish failed: $($p.Probe) ($($p.Mode)); see $($p.Log)"
}

# --- 2. Run ------------------------------------------------------------------------------------------------------
$runnable = @($published | Where-Object Ok)
$samples = @{}
$outputs = @{}
$failures = @{}
foreach ($p in $runnable) {
    $key = "$($p.Mode)/$($p.Probe)"
    $samples[$key] = [System.Collections.Generic.List[double]]::new()
    $outputs[$key] = [System.Collections.Generic.HashSet[string]]::new()
    $failures[$key] = $null
}

$total = $WarmupRuns + $Runs
for ($i = 1; $i -le $total; $i++) {
    $isWarmup = $i -le $WarmupRuns
    Write-Progress -Activity 'Startup probes' -Status ($isWarmup ? "warmup $i/$WarmupRuns" : "run $($i - $WarmupRuns)/$Runs") -PercentComplete (100 * $i / $total)
    foreach ($p in $runnable) {
        $key = "$($p.Mode)/$($p.Probe)"
        if ($failures[$key]) { continue }
        try {
            $r = Invoke-ProbeOnce (Get-ProbeExecutable $p.Dir $p.Probe)
        }
        catch {
            $failures[$key] = $_.Exception.Message
            continue
        }
        if ($r.ExitCode -ne 0) {
            $firstLine = ($r.Error -split "`n" | Select-Object -First 1)
            $failures[$key] = "exit code $($r.ExitCode): $firstLine"
            continue
        }
        [void] $outputs[$key].Add($r.Output)
        if (-not $isWarmup) { $samples[$key].Add($r.Ms) }
    }
}
Write-Progress -Activity 'Startup probes' -Completed

# --- 3. Statistics -----------------------------------------------------------------------------------------------
$expectedChecksum = $null
foreach ($m in $modes) {
    $key = "$m/Baseline"
    if ($outputs.ContainsKey($key) -and $outputs[$key].Count -eq 1) { $expectedChecksum = @($outputs[$key])[0]; break }
}

$rows = foreach ($p in $published) {
    $key = "$($p.Mode)/$($p.Probe)"
    $row = [ordered]@{
        Mode = $p.Mode; Probe = $p.Probe; Status = ''; Median = $null; P95 = $null; Min = $null; Mean = $null
        Checksum = ''; Runs = 0
    }
    if (-not $p.Ok) { $row.Status = 'publish failed' }
    elseif ($failures[$key]) { $row.Status = "fails at runtime ($($failures[$key]))" }
    else {
        $values = [double[]] ($samples[$key] | Sort-Object)
        $row.Runs = $values.Count
        $row.Median = Get-Percentile $values 0.5
        $row.P95 = Get-Percentile $values 0.95
        $row.Min = $values[0]
        $row.Mean = ($values | Measure-Object -Average).Average
        $distinct = @($outputs[$key])
        $row.Checksum = ($distinct.Count -eq 1) ? $distinct[0] : "inconsistent: $($distinct -join ', ')"
        $row.Status = if ($distinct.Count -ne 1) { 'checksum differs between runs' }
                      elseif ($expectedChecksum -and $distinct[0] -ne $expectedChecksum) { 'wrong checksum' }
                      else { 'ok' }
    }
    [pscustomobject] $row
}

$rows | ForEach-Object {
    [pscustomobject]@{
        Mode = $_.Mode; Probe = $_.Probe; Status = $_.Status; Runs = $_.Runs
        MedianMs = $_.Median; P95Ms = $_.P95; MinMs = $_.Min; MeanMs = $_.Mean; Checksum = $_.Checksum
    }
} | Export-Csv -Path (Join-Path $rawDir 'startup-summary.csv') -NoTypeInformation -UseCulture:$false

# --- 4. BenchmarkDotNet ColdStart --------------------------------------------------------------------------------
$bdnTable = $null
if (-not $SkipBdn) {
    Write-Host 'Running StartupBenchmark (BenchmarkDotNet, ColdStart)...'
    $bdnLog = Join-Path $rawDir 'bdn-startup.log'
    $project = Join-Path $benchmarksDir 'src' 'Comparison.Benchmarks' 'Comparison.Benchmarks.csproj'
    & dotnet run -c Release --project $project -- --filter '*StartupBenchmark*' *> $bdnLog
    if ($LASTEXITCODE -ne 0) { Write-Warning "StartupBenchmark failed; see $bdnLog" }
    $bdnReport = Join-Path $benchmarksDir 'results' 'raw' 'bdn' 'results' 'Comparison.Benchmarks.StartupBenchmark-report-github.md'
    if (Test-Path $bdnReport) { $bdnTable = Get-Content $bdnReport -Raw }
}

# --- 5. Report ---------------------------------------------------------------------------------------------------
$sdk = (& dotnet --version).Trim()
$sb = [System.Text.StringBuilder]::new()
[void] $sb.AppendLine('# Cold start')
[void] $sb.AppendLine()
[void] $sb.AppendLine('> Generated by `scripts/run-startup.ps1`. Do not edit by hand.')
[void] $sb.AppendLine()
[void] $sb.AppendLine("- Date: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm', [cultureinfo]::InvariantCulture)) UTC")
[void] $sb.AppendLine("- OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) ($rid)")
[void] $sb.AppendLine("- .NET SDK: $sdk")
[void] $sb.AppendLine("- Runs per probe: $Runs after $WarmupRuns warmup runs, interleaved across probes")
[void] $sb.AppendLine("- Expected checksum (Baseline): ``$expectedChecksum``")
[void] $sb.AppendLine()
[void] $sb.AppendLine('## Process start (`probes/StartupProbe.*`)')
[void] $sb.AppendLine()
[void] $sb.AppendLine('Each probe creates one order, sets up its mapper (the same configuration as the runtime benchmarks), maps the order, ' +
    'prints a checksum and exits. Time is wall-clock from `Process.Start` to process exit, measured by the script; ' +
    'the launch overhead of the script is the same for every probe, so compare the Δ column. ' +
    '`Baseline` creates the same order and prints the checksum of the source without any mapper.')
[void] $sb.AppendLine()

foreach ($m in $modes) {
    $title = if ($m -eq 'Jit') { 'Framework-dependent (JIT)' } else { 'NativeAOT' }
    [void] $sb.AppendLine("### $title")
    [void] $sb.AppendLine()
    [void] $sb.AppendLine('| Probe | Median, ms | p95, ms | Min, ms | Mean, ms | Δ median vs Baseline, ms | Status |')
    [void] $sb.AppendLine('|---|---:|---:|---:|---:|---:|---|')
    $baseline = $rows | Where-Object { $_.Mode -eq $m -and $_.Probe -eq 'Baseline' -and $_.Status -eq 'ok' } | Select-Object -First 1
    foreach ($r in $rows | Where-Object Mode -eq $m) {
        if ($r.Median -is [double]) {
            $delta = if ($baseline) { Format-Ms ($r.Median - $baseline.Median) } else { 'n/a' }
            [void] $sb.AppendLine("| $($r.Probe) | $(Format-Ms $r.Median) | $(Format-Ms $r.P95) | $(Format-Ms $r.Min) | $(Format-Ms $r.Mean) | $delta | $($r.Status) |")
        }
        else {
            [void] $sb.AppendLine("| $($r.Probe) | – | – | – | – | – | $($r.Status) |")
        }
    }
    [void] $sb.AppendLine()
}

[void] $sb.AppendLine('## In-process cold start (BenchmarkDotNet `StartupBenchmark`)')
[void] $sb.AppendLine()
[void] $sb.AppendLine('`RunStrategy.ColdStart`, 20 launches, 1 invocation per launch: create the configuration from scratch ' +
    '(nothing for the source generators) and map the first order. Shows the cost of building the configuration ' +
    '(expression trees, reflection) and of the first-call JIT, without process start.')
[void] $sb.AppendLine()
if ($bdnTable) {
    # Keep only the table and its preamble from the BDN GitHub report.
    [void] $sb.AppendLine($bdnTable.Trim())
}
elseif ($SkipBdn) { [void] $sb.AppendLine('_Skipped (`-SkipBdn`)._') }
else { [void] $sb.AppendLine('_StartupBenchmark did not produce a report; see `results/raw/startup/bdn-startup.log`._') }
[void] $sb.AppendLine()

Set-Content -Path $reportPath -Value ($sb.ToString() -replace "`r`n", "`n") -NoNewline -Encoding utf8NoBOM
Write-Host "Written: $reportPath"

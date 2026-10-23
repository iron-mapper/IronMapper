#Requires -Version 7.0
<#
.SYNOPSIS
    Compares two runs of the suite (e.g. the published 1.1.3 and the local sources) and writes comparison.md into the
    folder of the current run; generate-report.ps1 embeds it into that run's RESULTS.md. Measures nothing.

.DESCRIPTION
    Both runs must come from one session on one machine (scripts/run-benchmarks.ps1): only then are the absolute
    numbers comparable. The libraries other than IronMapper are the same packages in both runs, so their change between
    the runs shows the run-to-run spread; a change of IronMapper within that spread is not a change.

    Sections: runtime benchmarks (.NET 10 and, when measured, .NET 8) with the ratio to Manual of the same run; cold
    start (process, JIT; IronMapper_IMapper also over Manual_DI); NativeAOT; size; build time.

.EXAMPLE
    pwsh ./scripts/compare-results.ps1 -Baseline results/1.1.3-rerun -Current results/2.0.0
#>
[CmdletBinding()]
param(
    # Folders relative to benchmarks/ (or absolute).
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Current,
    [string] $BaselineLabel = '1.1.3',
    [string] $CurrentLabel = '2.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'BenchmarkCommon.psm1') -Force

$c = [cultureinfo]::InvariantCulture
$runtimeLibraries = @('Manual', 'IronMapper', 'IronMapper_IMapper', 'Mapperly', 'Mapster', 'AutoMapper')
$cases = @(
    @{ Class = 'Flat'; Parameters = ''; Label = 'Flat' }
    @{ Class = 'Nested'; Parameters = ''; Label = 'Nested' }
    @{ Class = 'Collection'; Parameters = 'Count=10'; Label = 'Collection, 10' }
    @{ Class = 'Collection'; Parameters = 'Count=1000'; Label = 'Collection, 1000' }
    @{ Class = 'Collection'; Parameters = 'Count=100000'; Label = 'Collection, 100k' }
    @{ Class = 'CustomConfig'; Parameters = ''; Label = 'CustomConfig' }
    @{ Class = 'InPlace'; Parameters = ''; Label = 'InPlace' }
)
$diCases = @(
    @{ Prefix = 'Flat'; Label = 'DiOverhead, flat' }
    @{ Prefix = 'Nested'; Label = 'DiOverhead, nested' }
)

# ---------------------------------------------------------------------------------------------------------------------
# Reading one run (the helpers of BenchmarkCommon read IRONMAPPER_RESULTS_DIR)
# ---------------------------------------------------------------------------------------------------------------------

function Read-Bdn([string] $resultsFolder) {
    $all = @{}
    if (-not (Test-Path $resultsFolder)) { return $all }
    foreach ($file in Get-ChildItem $resultsFolder -Filter 'Comparison.Benchmarks.*Benchmark-report-full.json') {
        $class = $file.Name -replace '^Comparison\.Benchmarks\.(\w+)Benchmark-report-full\.json$', '$1'
        $json = Get-Content $file.FullName -Raw | ConvertFrom-Json
        $all[$class] = @(foreach ($b in $json.Benchmarks) {
            $alloc = if ($b.PSObject.Properties['Memory'] -and $b.Memory) { [double] $b.Memory.BytesAllocatedPerOperation } else { $null }
            $margin = if ($b.Statistics -and $b.Statistics.PSObject.Properties['ConfidenceInterval']) { [double] $b.Statistics.ConfidenceInterval.Margin } else { $null }
            [pscustomobject]@{
                Method = $b.Method; Parameters = "$($b.Parameters)"; MeanNs = $b.Statistics ? [double] $b.Statistics.Mean : $null
                MarginNs = $margin; AllocatedBytes = $alloc
            }
        })
    }
    $all
}

function Read-Run([string] $dir) {
    $previous = $env:IRONMAPPER_RESULTS_DIR
    $env:IRONMAPPER_RESULTS_DIR = $dir
    try {
        $raw = Get-ResultsRawDir
        if (-not (Test-Path $raw)) { throw "No raw data in $raw" }
        $info = (Test-Path (Get-RunInfoPath)) ? (Get-Content (Get-RunInfoPath) -Raw | ConvertFrom-Json) : $null
        $jit = (Test-Path (Get-StartupCsvPath 'Jit')) ? @(Import-Csv (Get-StartupCsvPath 'Jit')) : @()
        $aot = (Test-Path (Get-StartupCsvPath 'Aot')) ? @(Import-Csv (Get-StartupCsvPath 'Aot')) : @()
        $sizes = @{}
        foreach ($probe in Get-StartupProbeNames) {
            $jitDir = Get-ProbeDir 'Jit' $probe
            $aotExe = Get-ProbeExecutable (Get-ProbeDir 'Aot' $probe) $probe
            $sizes[$probe] = [pscustomobject]@{
                Jit = (Test-Path $jitDir) ? (Measure-PublishFolder $jitDir).Size : $null
                Aot = (Test-Path $aotExe) ? (Get-Item $aotExe).Length : $null
                AotWarnings = @(Get-IlWarnings (Get-PublishLogPath 'Aot' $probe)).Count
            }
        }
        $buildCsv = Join-Path $raw 'build-time.csv'
        [pscustomobject]@{
            Dir = Get-ResultsDirDisplay
            Info = $info
            Bdn = Read-Bdn (Join-Path $raw 'bdn' 'results')
            BdnNet8 = Read-Bdn (Join-Path $raw 'bdn-net8.0' 'results')
            Jit = $jit
            Aot = $aot
            Sizes = $sizes
            Build = (Test-Path $buildCsv) ? @(Import-Csv $buildCsv) : @()
            BuildReport = Join-Path (Get-ResultsDir) 'build-time.md'
            Nupkg = Get-IronMapperNupkg $info
        }
    }
    finally { $env:IRONMAPPER_RESULTS_DIR = $previous }
}

# .nupkg sizes of IronMapper and IronMapper.Extensions.DI of a run: the locally packed ones (run-info LocalPackageVersion,
# raw/local-packages), else the version of Directory.Packages.props from the NuGet global packages folder.
function Get-IronMapperNupkg($info) {
    $local = $info -and $info.PSObject.Properties['LocalPackageVersion'] -and $info.LocalPackageVersion
    $version = $local ? $info.LocalPackageVersion : (Get-PackageVersions | Where-Object Id -eq 'IronMapper').Version
    $globalDir = $env:NUGET_PACKAGES ? $env:NUGET_PACKAGES : (Join-Path $HOME '.nuget' 'packages')
    $result = [ordered]@{ Version = $version }
    foreach ($id in 'IronMapper', 'IronMapper.Extensions.DI') {
        $path = $local ? (Join-Path (Get-ResultsRawDir) 'local-packages' "$id.$version.nupkg") :
            (Join-Path $globalDir $id.ToLowerInvariant() $version "$($id.ToLowerInvariant()).$version.nupkg")
        $result[$id] = (Test-Path $path) ? (Get-Item $path).Length : $null
    }
    [pscustomobject]$result
}

# ---------------------------------------------------------------------------------------------------------------------
# Formatting
# ---------------------------------------------------------------------------------------------------------------------

function Format-Time($ns) {
    if ($null -eq $ns) { return '–' }
    if ($ns -lt 1e3) { return $ns.ToString('N1', $c) + ' ns' }
    if ($ns -lt 1e6) { return ($ns / 1e3).ToString('N2', $c) + ' µs' }
    ($ns / 1e6).ToString('N2', $c) + ' ms'
}
function Format-X($value) { ($null -eq $value) ? '–' : ('×' + $value.ToString('N2', $c)) }
function Format-Change($old, $new) {
    if ($null -eq $old -or $null -eq $new -or $old -eq 0) { return '–' }
    $p = ($new / $old - 1) * 100
    $text = [math]::Abs($p).ToString('N0', $c) + ' %'
    if ([math]::Round($p) -eq 0) { return '0 %' }
    ($p -lt 0 ? '−' : '+') + $text
}
function Format-SignedMs($value) {
    if ($null -eq $value) { return '–' }
    ($value -lt 0 ? '−' : '+') + [math]::Abs($value).ToString('N1', $c)
}

function Find-Bdn($bdn, [string] $class, [string] $method, [string] $parameters = '') {
    if (-not $bdn.ContainsKey($class)) { return $null }
    $bdn[$class] | Where-Object { $_.Method -eq $method -and $_.Parameters -eq $parameters } | Select-Object -First 1
}

# "mean (×ratio to Manual)" of one benchmark.
function Format-Case($r, $manual) {
    if (-not $r) { return '–' }
    $ratio = ($manual -and $manual.MeanNs) ? $r.MeanNs / $manual.MeanNs : $null
    "$(Format-Time $r.MeanNs) ($(Format-X $ratio))"
}

# ---------------------------------------------------------------------------------------------------------------------
# Document
# ---------------------------------------------------------------------------------------------------------------------

$old = Read-Run $Baseline
$new = Read-Run $Current

$sb = [System.Text.StringBuilder]::new()
function Add([string] $text = '') { [void] $sb.AppendLine($text) }

function Get-RunText($run, [string] $label) {
    $i = $run.Info
    if (-not $i) { return "``$($run.Dir)``" }
    $source = ($i.IronMapper -eq 'local') ? 'local sources' : 'published package'
    $commit = ($i.PSObject.Properties['Commit'] -and $i.Commit) ? ", commit ``$($i.Commit)``" : ''
    "**$label** — ``$($run.Dir)``: $source$commit, $($i.Mode) run started $($i.Started)"
}

Add '# Comparison of two runs'
Add
Add '> Generated by `scripts/compare-results.ps1` from the raw data of both runs. Do not edit by hand.'
Add
Add "- $(Get-RunText $old $BaselineLabel)"
Add "- $(Get-RunText $new $CurrentLabel)"
Add
Add ("Both runs come from one session on the same machine. Every library except IronMapper is the same package in both, " +
    "so its change between the runs is the run-to-run spread (look at `Manual` and `Mapperly`); a change of IronMapper " +
    "within that spread is not a change. Ratios are to `Manual` of the same run, which cancels most of the drift between the runs.")
Add

function Add-RuntimeSection([string] $title, [string] $property) {
    $oldBdn = $old.$property
    $newBdn = $new.$property
    if (-not $oldBdn.Count -or -not $newBdn.Count) { return }
    Add "## $title"
    Add
    Add "Mean (×ratio to ``Manual`` of the same run), $BaselineLabel → $CurrentLabel, and the change of the mean."
    Add
    Add "| Scenario | Library | $BaselineLabel | $CurrentLabel | Change | Allocated |"
    Add '|---|---|---:|---:|---:|---:|'
    foreach ($case in $cases) {
        $oldManual = Find-Bdn $oldBdn $case.Class 'Manual' $case.Parameters
        $newManual = Find-Bdn $newBdn $case.Class 'Manual' $case.Parameters
        foreach ($lib in $runtimeLibraries) {
            $o = Find-Bdn $oldBdn $case.Class $lib $case.Parameters
            $n = Find-Bdn $newBdn $case.Class $lib $case.Parameters
            if (-not $o -and -not $n) { continue }
            $alloc = if ($o -and $n -and (Format-Size $o.AllocatedBytes) -ne (Format-Size $n.AllocatedBytes)) { "$(Format-Size $o.AllocatedBytes) → $(Format-Size $n.AllocatedBytes)" }
                     elseif ($n) { "$(Format-Size $n.AllocatedBytes)" } else { '–' }
            $name = ($lib -like 'IronMapper*') ? "**$lib**" : $lib
            Add "| $($case.Label) | $name | $(Format-Case $o $oldManual) | $(Format-Case $n $newManual) | $(Format-Change $o.MeanNs $n.MeanNs) | $alloc |"
        }
    }
    foreach ($case in $diCases) {
        $oldManual = Find-Bdn $oldBdn 'DiOverhead' "$($case.Prefix)_Manual"
        $newManual = Find-Bdn $newBdn 'DiOverhead' "$($case.Prefix)_Manual"
        foreach ($suffix in 'Manual', 'IronMapper', 'IronMapper_IMapper', 'AutoMapper_IMapper', 'Mapster_IMapper') {
            $method = "$($case.Prefix)_$suffix"
            $o = Find-Bdn $oldBdn 'DiOverhead' $method
            $n = Find-Bdn $newBdn 'DiOverhead' $method
            if (-not $o -and -not $n) { continue }
            $alloc = if ($o -and $n -and (Format-Size $o.AllocatedBytes) -ne (Format-Size $n.AllocatedBytes)) { "$(Format-Size $o.AllocatedBytes) → $(Format-Size $n.AllocatedBytes)" }
                     elseif ($n) { "$(Format-Size $n.AllocatedBytes)" } else { '–' }
            $name = ($suffix -like 'IronMapper*') ? "**$suffix**" : $suffix
            Add "| $($case.Label) | $name | $(Format-Case $o $oldManual) | $(Format-Case $n $newManual) | $(Format-Change $o.MeanNs $n.MeanNs) | $alloc |"
        }
    }
    Add
}

Add-RuntimeSection 'Runtime, .NET 10' 'Bdn'
Add-RuntimeSection 'Runtime, .NET 8' 'BdnNet8'

# Cold start: median of the process start + one mapping minus the Baseline console of the same run.
function Get-ColdStart($run, [string] $probe, [string] $over = 'Baseline') {
    $p = $run.Jit | Where-Object { $_.Probe -eq $probe -and $_.Status -eq 'ok' } | Select-Object -First 1
    $b = $run.Jit | Where-Object { $_.Probe -eq $over -and $_.Status -eq 'ok' } | Select-Object -First 1
    ($p -and $b) ? [double] $p.MedianMs - [double] $b.MedianMs : $null
}
if ($old.Jit.Count -and $new.Jit.Count) {
    Add '## Cold start (JIT)'
    Add
    Add ("Median process start + one mapping, framework-dependent, minus the ``Baseline`` console (no mapper) of the same run, ms. " +
        "The last row is IronMapper's own part of the ``IMapper`` cold start: ``IronMapper_IMapper`` minus ``Manual_DI`` (hand-written mapping behind the same DI container).")
    Add
    Add "| Probe | $BaselineLabel, Δ ms | $CurrentLabel, Δ ms |"
    Add '|---|---:|---:|'
    foreach ($probe in Get-StartupProbeNames | Where-Object { $_ -ne 'Baseline' }) {
        $name = ($probe -like 'IronMapper*') ? "**$probe**" : $probe
        Add "| $name | $(Format-SignedMs (Get-ColdStart $old $probe)) | $(Format-SignedMs (Get-ColdStart $new $probe)) |"
    }
    Add "| **IronMapper_IMapper − Manual_DI** | $(Format-SignedMs (Get-ColdStart $old 'IronMapper_IMapper' 'Manual_DI')) | $(Format-SignedMs (Get-ColdStart $new 'IronMapper_IMapper' 'Manual_DI')) |"
    Add
}

function Get-AotText($run, [string] $probe) {
    $row = $run.Aot | Where-Object Probe -eq $probe | Select-Object -First 1
    $base = $run.Aot | Where-Object Probe -eq 'Baseline' | Select-Object -First 1
    if (-not $row) { return '–' }
    $works = ($row.Status -eq 'ok' -and $base -and $row.Checksum -eq $base.Checksum) ? 'works' : (($row.Status -eq 'publish failed') ? 'publish failed' : 'fails')
    $warnings = $run.Sizes[$probe].AotWarnings
    "$works, $warnings warning$($warnings -eq 1 ? '' : 's')"
}
function Get-SizeDelta($run, [string] $probe, [string] $kind) {
    $s = $run.Sizes[$probe].$kind
    $b = $run.Sizes['Baseline'].$kind
    ($null -ne $s -and $null -ne $b) ? (Format-SignedSize ($s - $b)) : '–'
}
if ($old.Aot.Count -and $new.Aot.Count) {
    Add '## NativeAOT and size'
    Add
    Add 'NativeAOT: does the executable run and print the right checksum, trim/AOT warnings of the publish. Δ sizes against the `Baseline` console of the same run (decimal units).'
    Add
    Add "| Probe | NativeAOT, $BaselineLabel | NativeAOT, $CurrentLabel | Δ executable (AOT), $BaselineLabel | Δ executable (AOT), $CurrentLabel | Δ publish (JIT), $BaselineLabel | Δ publish (JIT), $CurrentLabel |"
    Add '|---|---|---|---:|---:|---:|---:|'
    foreach ($probe in Get-StartupProbeNames | Where-Object { $_ -ne 'Baseline' }) {
        $name = ($probe -like 'IronMapper*') ? "**$probe**" : $probe
        Add "| $name | $(Get-AotText $old $probe) | $(Get-AotText $new $probe) | $(Get-SizeDelta $old $probe 'Aot') | $(Get-SizeDelta $new $probe 'Aot') | $(Get-SizeDelta $old $probe 'Jit') | $(Get-SizeDelta $new $probe 'Jit') |"
    }
    Add
    Add ("Packages (.nupkg): IronMapper $(Format-Size $old.Nupkg.IronMapper) → $(Format-Size $new.Nupkg.IronMapper), " +
        "IronMapper.Extensions.DI $(Format-Size $old.Nupkg.'IronMapper.Extensions.DI') → $(Format-Size $new.Nupkg.'IronMapper.Extensions.DI') " +
        "($($old.Nupkg.Version) → $($new.Nupkg.Version)).")
    Add
}

# "Generated code" column of the IronMapper row of build-time.md for one N (the probes are regenerated by every run).
function Get-GeneratedCode([string] $report, [int] $n) {
    if (-not (Test-Path $report)) { return '–' }
    $inSection = $false
    foreach ($line in Get-Content $report) {
        if ($line -match '^## N = (\d+)') { $inSection = [int] $Matches[1] -eq $n; continue }
        if ($inSection -and $line -match '^\| IronMapper \|') { return ($line.Split('|') | ForEach-Object Trim)[10] }
    }
    '–'
}
if ($old.Build.Count -and $new.Build.Count) {
    Add '## Build time'
    Add
    Add 'Median `dotnet build --no-incremental` minus the `Baseline` project (models only) of the same run, seconds; generated code of IronMapper.'
    Add
    Add "| N | Library | $BaselineLabel, Δ s | $CurrentLabel, Δ s |"
    Add '|---:|---|---:|---:|'
    $sizes = @($new.Build | ForEach-Object { [int] $_.N } | Sort-Object -Unique)
    foreach ($n in $sizes) {
        foreach ($lib in 'Manual', 'IronMapper', 'Mapperly', 'Mapster', 'AutoMapper') {
            $values = foreach ($run in $old, $new) {
                $r = $run.Build | Where-Object { [int] $_.N -eq $n -and $_.Library -eq $lib } | Select-Object -First 1
                $b = $run.Build | Where-Object { [int] $_.N -eq $n -and $_.Library -eq 'Baseline' } | Select-Object -First 1
                ($r -and $b) ? (Format-SignedSeconds ([double] $r.MedianS - [double] $b.MedianS)) : '–'
            }
            $name = ($lib -eq 'IronMapper') ? '**IronMapper**' : $lib
            Add "| $n | $name | $($values[0]) | $($values[1]) |"
        }
        Add "| $n | IronMapper, generated code | $(Get-GeneratedCode $old.BuildReport $n) | $(Get-GeneratedCode $new.BuildReport $n) |"
    }
    Add
}

$env:IRONMAPPER_RESULTS_DIR = $Current
try { Write-ReportFile (Join-Path (Get-ResultsDir) 'comparison.md') $sb.ToString() }
finally { $env:IRONMAPPER_RESULTS_DIR = $null }

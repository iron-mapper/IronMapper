#Requires -Version 7.0
<#
    Shared helpers of the benchmark scripts (run-startup.ps1, run-aot.ps1, run-footprint.ps1, run-build-time.ps1).
    Import with: Import-Module (Join-Path $PSScriptRoot 'BenchmarkCommon.psm1') -Force
#>

Set-StrictMode -Version Latest

$script:BenchmarksDir = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------------------------------------------------

function Get-BenchmarksDir { $script:BenchmarksDir }

function Get-ResultsDir { Join-Path $script:BenchmarksDir 'results' }

function Get-ResultsRawDir { Join-Path $script:BenchmarksDir 'results' 'raw' }

# Output of run-startup.ps1: publish logs, published probes, stderr of failed runs, mstat files.
function Get-StartupLogDir { Join-Path (Get-ResultsRawDir) 'startup' }

function Get-StartupProbeNames { @('Baseline', 'Manual', 'IronMapper', 'IronMapper_IMapper', 'Mapperly', 'Mapster', 'AutoMapper') }

function Get-StartupProbeProject([string] $Probe) {
    Join-Path $script:BenchmarksDir 'probes' "StartupProbe.$Probe" "StartupProbe.$Probe.csproj"
}

function Get-ProbeDir([string] $Mode, [string] $Probe) {
    Join-Path (Get-StartupLogDir) 'publish' $Mode.ToLowerInvariant() $Probe
}

function Get-ProbeExecutable([string] $Dir, [string] $Probe) {
    $name = "StartupProbe.$Probe"
    if ($IsWindows) { $name += '.exe' }
    Join-Path $Dir $name
}

function Get-PublishLogPath([string] $Mode, [string] $Probe) {
    Join-Path (Get-StartupLogDir) "publish-$($Mode.ToLowerInvariant())-$Probe.log"
}

# mstat of a NativeAOT probe (copied there by Publish-StartupProbe).
function Get-MstatPath([string] $Probe) { Join-Path (Get-StartupLogDir) 'mstat' "StartupProbe.$Probe.mstat" }

# Full stderr of the first failed run of a probe (written by run-startup.ps1).
function Get-RunErrorPath([string] $Mode, [string] $Probe) {
    Join-Path (Get-StartupLogDir) "run-$($Mode.ToLowerInvariant())-$Probe.stderr.txt"
}

function Get-StartupCsvPath([string] $Mode) { Join-Path (Get-ResultsRawDir) "startup-$($Mode.ToLowerInvariant()).csv" }

# ---------------------------------------------------------------------------------------------------------------------
# Environment
# ---------------------------------------------------------------------------------------------------------------------

function Get-CurrentRid {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    "$os-$arch"
}

<#
    NativeAOT on Windows: ILCompiler's findvcvarsall.bat calls vcvarsall.bat, and VsDevCmd.bat (VS 2026) runs
    `vswhere.exe` by bare name after a pushd into the Visual Studio Installer folder, i.e. it relies on cmd finding the
    exe in the current directory. With NoDefaultCurrentDirectoryInExePath set (some shells and sandboxes set it) that
    lookup fails, the error text ends up in the linker path and the publish fails with MSB3073. Putting the Installer
    folder on PATH fixes it. Only on Windows, and only when vswhere.exe is not already found on PATH.
#>
function Initialize-NativeToolchain {
    if (-not $IsWindows) { return }
    if (Get-Command 'vswhere.exe' -CommandType Application -ErrorAction SilentlyContinue) { return }
    $installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer'
    if (-not (Test-Path (Join-Path $installerDir 'vswhere.exe'))) {
        Write-Warning "vswhere.exe not found (looked on PATH and in $installerDir); NativeAOT publish needs the Visual Studio C++ build tools."
        return
    }
    $env:PATH = "$installerDir$([System.IO.Path]::PathSeparator)$env:PATH"
    Write-Host "vswhere.exe was not on PATH; added $installerDir for the NativeAOT toolchain."
}

# Free (available) physical memory in bytes, or $null when it cannot be determined.
function Get-FreeMemoryBytes {
    try {
        if ($IsWindows) {
            return [double] (Get-CimInstance -ClassName Win32_OperatingSystem).FreePhysicalMemory * 1KB
        }
        if ($IsLinux) {
            $line = Get-Content '/proc/meminfo' | Where-Object { $_ -match '^MemAvailable:\s+(\d+)\s+kB' } | Select-Object -First 1
            if ($line -and $line -match '(\d+)') { return [double] $Matches[1] * 1KB }
            return $null
        }
        if ($IsMacOS) {
            $vm = & vm_stat
            $pageSize = if ($vm[0] -match 'page size of (\d+) bytes') { [double] $Matches[1] } else { 4096 }
            $pages = 0
            foreach ($name in 'Pages free', 'Pages inactive', 'Pages speculative') {
                $l = $vm | Where-Object { $_ -like "${name}:*" } | Select-Object -First 1
                if ($l -and $l -match '(\d+)\.?$') { $pages += [double] $Matches[1] }
            }
            return $pages * $pageSize
        }
    }
    catch { }
    return $null
}

# One line for report headers: CPU, memory, OS, RID, SDK, runtime.
function Get-MachineSummary {
    $cpu = ''; $memory = ''
    try {
        if ($IsWindows) {
            $p = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1
            $cpu = "$($p.Name.Trim()) ($($p.NumberOfCores) cores, $($p.NumberOfLogicalProcessors) threads)"
            $memory = '{0:N0} GB RAM' -f ((Get-CimInstance -ClassName Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
        }
        elseif ($IsLinux) {
            $cpu = ((Get-Content '/proc/cpuinfo' | Where-Object { $_ -like 'model name*' } | Select-Object -First 1) -split ':', 2)[1].Trim()
            $kb = [double] ((Get-Content '/proc/meminfo' | Select-Object -First 1) -replace '[^\d]', '')
            $memory = '{0:N0} GB RAM' -f ($kb * 1KB / 1GB)
        }
        elseif ($IsMacOS) {
            $cpu = (& sysctl -n machdep.cpu.brand_string).Trim()
            $memory = '{0:N0} GB RAM' -f ([double] (& sysctl -n hw.memsize) / 1GB)
        }
    }
    catch { }
    $os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    $sdk = (& dotnet --version).Trim()
    $runtime = ((& dotnet --list-runtimes) | Where-Object { $_ -like 'Microsoft.NETCore.App 10.*' } | Select-Object -Last 1) -replace '^Microsoft\.NETCore\.App (\S+).*$', '$1'
    $parts = @($cpu, $memory, "$os ($(Get-CurrentRid))", ".NET SDK $sdk", ".NET runtime $runtime") | Where-Object { $_ }
    $parts -join ' · '
}

function Get-UtcStamp { (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm', [cultureinfo]::InvariantCulture) + ' UTC' }

# ---------------------------------------------------------------------------------------------------------------------
# Publishing the StartupProbe.* consoles
# ---------------------------------------------------------------------------------------------------------------------

<#
    Publishes one probe framework-dependent (Jit) or with NativeAOT (Aot) into Get-ProbeDir.
    NativeAOT publishes also write the mstat (-p:IlcGenerateMstatFile=true, copied to Get-MstatPath) and every
    trim/AOT warning separately (-p:TrimmerSingleWarn=false; by default ILC prints one IL2104/IL3053 per assembly).
    Neither option changes the produced executable. After a NativeAOT publish the build servers are shut down.
#>
function Publish-StartupProbe {
    param(
        [Parameter(Mandatory)] [string] $Probe,
        [Parameter(Mandatory)] [ValidateSet('Jit', 'Aot')] [string] $Mode,
        [string] $Rid = (Get-CurrentRid),
        # NativeAOT: one ILC thread (IlcSingleThreaded=true → `ilc --parallelism:1`). Slower, less memory.
        [switch] $LowMemory
    )
    $project = Get-StartupProbeProject $Probe
    $outDir = Get-ProbeDir $Mode $Probe
    $log = Get-PublishLogPath $Mode $Probe
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

    # --disable-build-servers: no MSBuild/compiler server processes stay in memory between the publishes.
    $publishArgs = @('publish', $project, '-c', 'Release', '-r', $Rid, '-o', $outDir, '-nologo', '--disable-build-servers')
    if ($Mode -eq 'Aot') {
        $publishArgs += @('-p:PublishAot=true', '-p:IlcGenerateMstatFile=true', '-p:TrimmerSingleWarn=false')
        if ($LowMemory) { $publishArgs += '-p:IlcSingleThreaded=true' }
    }
    else { $publishArgs += @('--self-contained', 'false') }

    & dotnet @publishArgs *> $log
    $ok = $LASTEXITCODE -eq 0 -and (Test-Path (Get-ProbeExecutable $outDir $Probe))

    if ($Mode -eq 'Aot') {
        & dotnet build-server shutdown *>> $log
        $mstat = Get-MstatPath $Probe
        if (Test-Path $mstat) { Remove-Item -Force $mstat }
        $native = Join-Path (Split-Path -Parent $project) 'obj' 'Release' 'net10.0' $Rid 'native' "StartupProbe.$Probe.mstat"
        if ($ok -and (Test-Path $native)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $mstat) | Out-Null
            Copy-Item $native $mstat
        }
    }
    [pscustomobject]@{ Probe = $Probe; Mode = $Mode; Ok = $ok; Dir = $outDir; Log = $log }
}

<#
    Trim/AOT analysis warnings (IL2xxx, IL3xxx) from a publish log, deduplicated (MSBuild prints each warning again in
    the summary). Returns objects with Code, Origin and Message.
#>
function Get-IlWarnings([string] $Log) {
    if (-not (Test-Path $Log)) { return @() }
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($line in Get-Content $Log) {
        if ($line -notmatch '^\s*(?<origin>.*?)\s*:\s*(?:Trim analysis |AOT analysis )?warning (?<code>IL[23]\d{3})\s*:\s*(?<msg>.*?)(?:\s+\[[^\]]+\.csproj\])?\s*$') { continue }
        $code = $Matches.code; $msg = $Matches.msg; $origin = $Matches.origin
        if ($origin -eq '' -or $origin.StartsWith('ILC')) { $origin = 'ILC' }
        if (-not $seen.Add("$code|$origin|$msg")) { continue }
        [pscustomobject]@{ Code = $code; Origin = $origin; Message = $msg }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Statistics and formatting
# ---------------------------------------------------------------------------------------------------------------------

function Get-Percentile([double[]] $Sorted, [double] $P) {
    # Nearest-rank method.
    $rank = [math]::Ceiling($P * $Sorted.Count)
    $Sorted[[math]::Max(0, $rank - 1)]
}

function Format-Ms($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    ([double] $Value).ToString('N1', [cultureinfo]::InvariantCulture)
}

function Format-Seconds($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    ([double] $Value).ToString('N2', [cultureinfo]::InvariantCulture)
}

function Format-SignedSeconds($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    ([double] $Value).ToString('+0.00;−0.00;0.00', [cultureinfo]::InvariantCulture)
}

function ConvertTo-InvariantNumber($Value) {
    if ($null -eq $Value) { return '' }
    ([double] $Value).ToString('R', [cultureinfo]::InvariantCulture)
}

# Bytes as "1,234,567" (invariant).
function Format-Bytes($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    ([long] $Value).ToString('N0', [cultureinfo]::InvariantCulture)
}

function Format-SignedBytes($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    ([long] $Value).ToString('+#,0;−#,0;0', [cultureinfo]::InvariantCulture)
}

# Bytes as "1.23 MB" / "456.7 KB" (decimal units: 1 KB = 1,000 bytes, 1 MB = 1,000,000 bytes).
function Format-Size($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    $v = [double] $Value
    $sign = if ($v -lt 0) { '−' } else { '' }
    $a = [math]::Abs($v)
    if ($a -ge 1e6) { return $sign + ($a / 1e6).ToString('N2', [cultureinfo]::InvariantCulture) + ' MB' }
    if ($a -ge 1e3) { return $sign + ($a / 1e3).ToString('N1', [cultureinfo]::InvariantCulture) + ' KB' }
    return $sign + $a.ToString('N0', [cultureinfo]::InvariantCulture) + ' B'
}

function Format-SignedSize($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return '–' }
    if ([double] $Value -gt 0) { return '+' + (Format-Size $Value) }
    if ([double] $Value -eq 0) { return '0' }
    Format-Size $Value
}

# Text for a Markdown table cell: no line breaks, no pipes.
function Format-Cell([string] $Text) {
    ($Text -replace '\r?\n|\r', ' ' -replace '\|', '\|').Trim()
}

<#
    Writes a report file: UTF-8 without BOM, LF line endings only (also removes stray CRs, e.g. from process output),
    so the committed results/** files are identical on every OS (see benchmarks/.gitattributes).
#>
function Write-ReportFile([string] $Path, [string] $Content) {
    $text = $Content -replace "`r`n", "`n" -replace "`r", ''
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [System.IO.File]::WriteAllText($Path, $text, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Written: $Path"
}

# ---------------------------------------------------------------------------------------------------------------------
# mstat (NativeAOT size breakdown)
# ---------------------------------------------------------------------------------------------------------------------

<#
    Runs tools/MstatReport on the given mstat files (Label → path; the first one is the reference) and returns its
    Markdown. Builds the tool on first use.
#>
function Invoke-MstatReport {
    param(
        [Parameter(Mandatory)] [System.Collections.Specialized.OrderedDictionary] $Inputs,
        [string] $Diff,
        [int] $Top = 15,
        [int] $HeadingLevel = 3
    )
    $project = Join-Path $script:BenchmarksDir 'tools' 'MstatReport' 'MstatReport.csproj'
    $toolArgs = @()
    foreach ($label in $Inputs.Keys) { $toolArgs += @('--input', "$label=$($Inputs[$label])") }
    if ($Diff) { $toolArgs += @('--diff', $Diff) }
    $toolArgs += @('--top', $Top, '--heading-level', $HeadingLevel)

    $build = & dotnet build $project -c Release -nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Building MstatReport failed:`n$($build -join "`n")" }
    $dll = Join-Path (Split-Path -Parent $project) 'bin' 'Release' 'net10.0' 'MstatReport.dll'
    $output = & dotnet $dll @toolArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "MstatReport failed:`n$($output -join "`n")" }
    ($output -join "`n")
}

# Table rows of one section of MstatReport output (the section whose heading starts with $Title).
function Get-MstatTable([string] $Markdown, [string] $Title) {
    $section = ($Markdown -split '(?m)^#+ ') | Where-Object { $_.StartsWith($Title) } | Select-Object -First 1
    if (-not $section) { return '' }
    (($section -split "`n") | Where-Object { $_ -like '|*' }) -join "`n"
}

# mstat files of the given probes (Label → path), only if all of them exist; otherwise $null.
function Get-MstatInputs([string[]] $Probes) {
    $inputs = [ordered]@{}
    foreach ($probe in $Probes) {
        $path = Get-MstatPath $probe
        if (-not (Test-Path $path)) { return $null }
        $inputs[$probe] = $path
    }
    $inputs
}

Export-ModuleMember -Function *

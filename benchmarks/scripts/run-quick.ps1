#Requires -Version 7
<#
.SYNOPSIS
    Quick A/B of the runtime benchmarks: the published IronMapper package against the local sources (../src), on the
    same machine, in one session, with the same BenchmarkDotNet job ("quick profile", plan item 5.2).

.DESCRIPTION
    For every framework of -Frameworks, one after the other: the published package (-PublishedVersion, default 2.0.0),
    then the local sources. Before each variant the build outputs of src/ and tests/ are removed and the benchmark
    project is built again, because the variants differ in package and project references.

    The variant is chosen with environment variables (UseLocalIronMapper, IronMapperPackageVersion), not with
    `-p:` arguments: BenchmarkDotNet builds the benchmark project again in a child process and passes no MSBuild
    properties of the parent `dotnet run`, but the child inherits the environment. (With `dotnet run -p:UseLocalIronMapper=true`
    the child build uses the package of Directory.Packages.props, 1.1.3.) After every run the script checks the
    IronMapper of the build and of that child build (--keepFiles), from the restore of Comparison.Mappers
    (project.assets.json) and IronMapper.dll: the published variant must have the package IronMapper <version>, no
    project reference into ../src, and the package's assembly (the same bytes as lib/*/IronMapper.dll,
    `<version>+<commit of tag v<version>>`); the local variant the project reference to src/IronMapper, no IronMapper
    package, and the sources' assembly (`+<HEAD commit>`; in the build of the benchmark project also the same bytes as
    src/IronMapper/bin/Release/*/IronMapper.dll, the child build compiles ../src into its own folder). A wrong build
    stops the script; a wrong child build marks the run INVALID in the summary.

    Job: the job of the full run (run-all.ps1): BenchmarkDotNet's default job with --launchCount 3. Each benchmark runs
    in 3 processes, because Dynamic PGO makes the result of one process vary by more than the effects looked for here
    (benchmarks/README.md, "Dynamic PGO"); the numbers are comparable with results/<release>/. About 75 s per benchmark
    case and variant: Nested + InPlace (12 cases) on 2 frameworks and 2 variants take about an hour. -Dry runs
    --job Dry (one iteration) to check the commands in a few minutes; its numbers mean nothing.

    Pre-flight checks (stop before anything is measured; -Dry only warns about the git checks): the branch (-Branch,
    when given), the commit and whether the tree has changes (recorded; changes are expected for an A/B of work in
    progress), the tag v<PublishedVersion>, .NET SDK 10 and the runtimes of -Frameworks, free memory, AutoMapper
    license (warning only), and that the published package really is the one on nuget.org: after the restore the
    .nupkg.metadata of IronMapper and IronMapper.Extensions.DI in the global packages folder must name nuget.org as the
    source, and the cached .nupkg must have the SHA-512 of the .nupkg downloaded from nuget.org. A package with that version packed
    locally (eng/verify-package.ps1 packs 2.0.0 too, into its own folder) would fail here; delete
    ~/.nuget/packages/ironmapper/<version> then.

    Output: artifacts/quick/<yyyyMMdd-HHmmss>/<variant>-<framework>/ (BenchmarkDotNet artifacts, run.log, build.log,
    assemblies.json), run-info.json and summary.md in the run folder; nothing in benchmarks/results/ is written. The
    summary has, per framework and benchmark, Mean, Ratio (to Manual of the same run and category) and Allocated of both
    variants and their difference.

.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-quick.ps1
.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-quick.ps1 -Filter '*.NestedBenchmark.*' -Frameworks net8.0
.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-quick.ps1 -Dry
.EXAMPLE
    pwsh -NoProfile -File benchmarks/scripts/run-quick.ps1 -SummarizeOnly artifacts/quick/20261024-090000
#>
param(
    [string[]] $Filter = @('*.NestedBenchmark.*', '*.InPlaceBenchmark.*'),
    [string[]] $Frameworks = @('net10.0', 'net8.0'),
    [string] $PublishedVersion = '2.0.0',
    # The branch that must be checked out (not checked when empty).
    [string] $Branch = '',
    # Only the pre-flight checks.
    [switch] $CheckOnly,
    [switch] $Dry,
    # Only write summary.md of an existing run folder (artifacts/quick/<stamp>) again; nothing is built or measured.
    [string] $SummarizeOnly = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'BenchmarkCommon.psm1') -Force

$bench = Get-BenchmarksDir
$repo = Split-Path -Parent $bench
$project = Join-Path $bench 'src' 'Comparison.Benchmarks'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = Join-Path $repo 'artifacts' 'quick' $stamp
$job = $Dry ? @('--job', 'Dry') : @('--launchCount', '3')
$jobText = $Dry ? 'Dry (one iteration; numbers mean nothing)' : 'default job, --launchCount 3 (as the full run)'
$variants = @('published', 'local')
$nugetOrg = 'https://api.nuget.org/v3/index.json'

$saved = @{}
foreach ($name in 'UseLocalIronMapper', 'IronMapperPackageVersion', 'IronMapperPackageSource', 'IRONMAPPER_RESULTS_DIR') {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}

function Restore-Environment {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

function Set-Variant([string] $Variant) {
    # IronMapperPackageSource adds a local feed (run-all.ps1 -UseLocalIronMapper); never for this A/B.
    $env:IronMapperPackageSource = $null
    $env:IRONMAPPER_RESULTS_DIR = $null
    if ($Variant -eq 'published') {
        $env:UseLocalIronMapper = 'false'
        $env:IronMapperPackageVersion = $PublishedVersion
    }
    else {
        $env:UseLocalIronMapper = 'true'
        $env:IronMapperPackageVersion = $null
    }
}

$problems = [System.Collections.Generic.List[string]]::new()
function Write-Check([string] $Name, [bool] $Ok, [string] $Detail, [switch] $Fatal) {
    $mark = if ($Ok) { 'ok  ' } elseif ($Fatal) { 'FAIL' } else { 'warn' }
    Write-Host ("  [{0}] {1}: {2}" -f $mark, $Name, $Detail)
    if (-not $Ok -and $Fatal) { $script:problems.Add("${Name}: $Detail") }
}

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $repo @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    ($output -join "`n").Trim()
}

function Clear-BuildOutputs {
    & dotnet build-server shutdown *> $null
    foreach ($dir in 'src', 'tests') {
        Get-ChildItem (Join-Path $bench $dir) -Recurse -Directory -Include bin, obj -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Logged([string] $Log, [string[]] $Arguments) {
    & dotnet @Arguments *> $Log
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $tail = (Get-Content $Log -Tail 30) -join "`n"
        throw "dotnet $($Arguments[0]) failed (exit code $code); log: $Log`n$tail"
    }
}

# The global packages folder (NUGET_PACKAGES or ~/.nuget/packages), as NuGet reports it.
function Get-GlobalPackagesFolder {
    $line = (& dotnet nuget locals global-packages --list) | Select-Object -First 1
    ($line -replace '^\s*global-packages:\s*', '').Trim()
}

# The restored package is the one published on nuget.org: source in .nupkg.metadata, and the SHA-512 of the cached .nupkg
# (and of <id>.<version>.nupkg.sha512, which NuGet wrote when it downloaded it) equals the SHA-512 of the .nupkg downloaded
# from nuget.org now. (The contentHash of .nupkg.metadata is not comparable: for a signed package it excludes the signature.)
function Test-PublishedPackage([string] $Id) {
    $lower = $Id.ToLowerInvariant()
    $folder = Join-Path $script:globalPackages $lower $PublishedVersion
    $metadata = Join-Path $folder '.nupkg.metadata'
    if (-not (Test-Path $metadata)) { return [pscustomobject]@{ Ok = $false; Detail = "not restored: $metadata" } }
    $meta = Get-Content $metadata -Raw | ConvertFrom-Json
    if ($meta.source -ne $nugetOrg) {
        return [pscustomobject]@{ Ok = $false; Detail = "restored from '$($meta.source)', not nuget.org (delete $folder and run again)" }
    }
    $download = Join-Path ([System.IO.Path]::GetTempPath()) "$lower.$PublishedVersion.$stamp.nupkg"
    try {
        Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$lower/$PublishedVersion/$lower.$PublishedVersion.nupkg" -OutFile $download
        $bytes = [System.IO.File]::ReadAllBytes($download)
        $hash = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData($bytes))
    }
    catch { return [pscustomobject]@{ Ok = $false; Detail = "cannot download it from nuget.org to compare: $($_.Exception.Message)" } }
    finally { Remove-Item $download -ErrorAction SilentlyContinue }
    $cachedFile = Join-Path $folder "$lower.$PublishedVersion.nupkg"
    $cached = (Test-Path $cachedFile) ? [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($cachedFile))) : ''
    $recorded = (Get-Content (Join-Path $folder "$lower.$PublishedVersion.nupkg.sha512") -Raw -ErrorAction SilentlyContinue)
    if ($cached -ne $hash -or ($recorded ?? '').Trim() -ne $hash) {
        return [pscustomobject]@{ Ok = $false; Detail = "the cached package differs from the one on nuget.org (delete $folder and run again)" }
    }
    [pscustomobject]@{ Ok = $true; Detail = "nuget.org, SHA-512 matches the package there ($($hash.Substring(0, 12))...)" }
}

function Get-FileSha512([string] $Path) {
    [Convert]::ToHexString([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($Path)))
}

# The files with the given SHA-512 among the published package's lib/*/IronMapper.dll or the ../src build outputs
# (src/IronMapper/bin/Release/*/IronMapper.dll), as 'package lib/<tfm>' / 'src <tfm>'; 'none' if no file has it.
function Find-AssemblyFile([string] $Hash) {
    $places = @(
        @('package lib', (Join-Path $script:globalPackages 'ironmapper' $PublishedVersion 'lib')),
        @('src', (Join-Path $repo 'src' 'IronMapper' 'bin' 'Release')))
    foreach ($place in $places) {
        foreach ($file in Get-ChildItem $place[1] -Recurse -Filter 'IronMapper.dll' -ErrorAction SilentlyContinue) {
            if ((Get-FileSha512 $file.FullName) -eq $Hash) { return "$($place[0])/$($file.Directory.Name)" }
        }
    }
    'none'
}

# IronMapper of one build: what restore resolved for Comparison.Mappers (the project with the IronMapper references;
# project.assets.json: the IronMapper package, or the project references into ../src), and the IronMapper.dll of the
# build output (informational version, SHA-512 and the file with the same bytes).
# The generator cannot be checked in the output: in the build of the benchmark project it is an analyzer (package
# analyzers/, or a ProjectReference with ReferenceOutputAssembly=false) and is not copied; Directory.Build.targets switches
# it together with IronMapper. The bytes of IronMapper.dll identify the local variant only in the build of the benchmark
# project: BenchmarkDotNet's child build compiles ../src again with its own OutDir/ArtifactsPath, so the embedded PDB
# path, and with it the bytes, differ from src/IronMapper/bin.
function Get-AssemblyInfo([string] $Dir, [string] $AssetsFile, [string] $Framework) {
    $dll = Join-Path $Dir 'IronMapper.dll'
    if (-not (Test-Path $dll)) { return $null }
    $packages = @(); $references = @()
    if (Test-Path $AssetsFile) {
        $assets = Get-Content $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
        $packages = @($assets.libraries.Keys | Where-Object { $_ -like 'IronMapper*' -and $assets.libraries[$_].type -eq 'package' })
        $restoreFramework = $assets.project.restore.frameworks[$Framework]
        $references = @($restoreFramework ? $restoreFramework.projectReferences.Keys : @())
    }
    $hash = Get-FileSha512 $dll
    [pscustomobject]@{
        Directory         = $Dir
        AssetsFile        = (Test-Path $AssetsFile) ? $AssetsFile : $null
        Packages          = $packages
        ProjectReferences = @($references | Where-Object { $_.StartsWith((Join-Path $repo 'src') + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
        Version           = (Get-Item $dll).VersionInfo.ProductVersion
        Sha512            = $hash
        SameBytesAs       = Find-AssemblyFile $hash
    }
}

function Test-AssemblyInfo($Info, [string] $Variant, [switch] $ChildBuild) {
    if ($null -eq $Info) { return 'IronMapper.dll not found' }
    if (-not $Info.AssetsFile) { return 'project.assets.json of Comparison.Mappers not found' }
    $srcProject = Join-Path $repo 'src' 'IronMapper' 'IronMapper.csproj'
    $fromSrc = @($Info.ProjectReferences | Where-Object { $_ -ieq $srcProject }).Count -gt 0
    if ($Variant -eq 'published') {
        if ($Info.Packages -notcontains "IronMapper/$PublishedVersion") { return "restore did not resolve the package IronMapper $PublishedVersion (packages: $(@($Info.Packages) -join ', '))" }
        if (@($Info.ProjectReferences).Count -gt 0) { return "project references into ../src: $(@($Info.ProjectReferences) -join ', ')" }
        if ($Info.SameBytesAs -notlike 'package lib/*') { return "IronMapper.dll is not the package's (same bytes as: $($Info.SameBytesAs))" }
        if ($Info.Version -ne $script:expectedPublished) { return "version $($Info.Version), expected $script:expectedPublished" }
    }
    else {
        if (-not $fromSrc) { return "no project reference to $srcProject (packages: $(@($Info.Packages) -join ', '))" }
        if (@($Info.Packages).Count -gt 0) { return "IronMapper packages restored: $(@($Info.Packages) -join ', ')" }
        if ($Info.SameBytesAs -like 'package lib/*') { return "IronMapper.dll is the package's ($($Info.SameBytesAs))" }
        if (-not $ChildBuild -and $Info.SameBytesAs -notlike 'src/*') { return "IronMapper.dll is not the build of ../src (same bytes as: $($Info.SameBytesAs))" }
        if ($Info.Version -notlike "*+$script:head*") { return "version $($Info.Version), expected the HEAD commit $script:head" }
    }
    $null
}

# BenchmarkDotNet's child build of the last run (kept with --keepFiles), then removed.
function Get-ChildBuildInfo([string] $Framework) {
    $dirs = @(Get-ChildItem (Join-Path $project 'bin' 'Release' $Framework) -Directory -Filter 'Comparison.Benchmarks-*' -ErrorAction SilentlyContinue)
    $infos = foreach ($dir in $dirs) {
        Get-AssemblyInfo (Join-Path $dir.FullName 'bin' 'Release' $Framework) (Join-Path $dir.FullName 'obj' 'Comparison.Mappers' 'project.assets.json') $Framework
    }
    foreach ($dir in $dirs) { Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    @($infos | Where-Object { $_ })
}

# Status of one run from its assemblies.json: ok, FAILED (exit code) or INVALID (wrong IronMapper in the child build).
function Get-RunStatus([string] $Dir) {
    $file = Join-Path $Dir 'assemblies.json'
    if (-not (Test-Path $file)) { return 'not run' }
    $info = Get-Content $file -Raw | ConvertFrom-Json
    if ($info.ExitCode -ne 0) { return "FAILED (exit code $($info.ExitCode), log: $(Join-Path $Dir 'run.log'))" }
    if (@($info.Problems).Count -gt 0) { return "INVALID: $(@($info.Problems) -join '; ')" }
    'ok'
}

# Mean, Ratio (to Manual, or <Category>_Manual for the categories of DiOverheadBenchmark) and Allocated per benchmark
# case, from the full JSON reports of BenchmarkDotNet in <Dir>/results.
function Read-Results([string] $Dir) {
    $rows = @{}
    foreach ($file in Get-ChildItem (Join-Path $Dir 'results') -Filter '*-report-full.json' -ErrorAction SilentlyContinue) {
        $json = Get-Content $file.FullName -Raw | ConvertFrom-Json
        $benchmarks = @($json.Benchmarks | Where-Object { $_.Statistics })
        foreach ($b in $benchmarks) {
            $baseline = 'Manual'
            $underscore = $b.Method.IndexOf('_')
            if ($underscore -gt 0) {
                $candidate = $b.Method.Substring(0, $underscore) + '_Manual'
                if ($benchmarks | Where-Object { $_.Method -eq $candidate -and $_.Parameters -eq $b.Parameters }) { $baseline = $candidate }
            }
            $base = $benchmarks | Where-Object { $_.Method -eq $baseline -and $_.Parameters -eq $b.Parameters } | Select-Object -First 1
            $key = '{0}.{1}{2}' -f $b.Type, $b.Method, ($b.Parameters ? "($($b.Parameters))" : '')
            $rows[$key] = [pscustomobject]@{
                Mean      = [double] $b.Statistics.Mean
                Ratio     = $base ? [double] $b.Statistics.Mean / [double] $base.Statistics.Mean : [double]::NaN
                Allocated = [double] $b.Memory.BytesAllocatedPerOperation
            }
        }
    }
    $rows
}

# summary.md of a run folder (also with -SummarizeOnly): per framework and benchmark case, both variants and the
# difference. Returns the path.
function Write-QuickSummary([string] $Root) {
    $culture = [cultureinfo]::InvariantCulture
    $info = Get-Content (Join-Path $Root 'run-info.json') -Raw | ConvertFrom-Json
    $version = $info.PublishedVersion
    function F([double] $Value, [string] $Format) { [double]::IsNaN($Value) ? '—' : $Value.ToString($Format, $culture) }

    $sb = [System.Text.StringBuilder]::new()
    [void] $sb.AppendLine("# Quick A/B: IronMapper $version (nuget.org) vs local sources")
    [void] $sb.AppendLine()
    [void] $sb.AppendLine("- Run: $(Split-Path $Root -Leaf), $($info.Machine)")
    [void] $sb.AppendLine("- Local sources: $($info.Branch), $($info.Commit.Substring(0, 7))$($info.Uncommitted ? " + uncommitted changes (diff SHA-256 $($info.DiffSha256))" : '')")
    [void] $sb.AppendLine("- $version`: nuget.org, tag v$version ($($info.PublishedCommit.Substring(0, 7)))")
    [void] $sb.AppendLine("- Job: $($info.Job); filter: $(@($info.Filter) -join ' ')")
    [void] $sb.AppendLine('- Ratio: Mean / Mean of `Manual` of the same run (and category). Δ: local against published.')
    if ($info.Dry) { [void] $sb.AppendLine('- **Dry run**: one iteration, the numbers mean nothing.') }
    foreach ($framework in @($info.Frameworks)) {
        $a = Join-Path $Root "published-$framework"
        $b = Join-Path $Root "local-$framework"
        [void] $sb.AppendLine()
        [void] $sb.AppendLine("## $framework")
        [void] $sb.AppendLine()
        [void] $sb.AppendLine("Published: $(Get-RunStatus $a). Local: $(Get-RunStatus $b).")
        [void] $sb.AppendLine()
        [void] $sb.AppendLine("| Benchmark | $version Mean | $version Ratio | Local Mean | Local Ratio | Δ Ratio | $version Allocated | Local Allocated | Δ Allocated |")
        [void] $sb.AppendLine('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
        $ra = Read-Results $a
        $rb = Read-Results $b
        foreach ($key in @($ra.Keys) + @($rb.Keys) | Sort-Object -Unique) {
            $x = $ra[$key]; $y = $rb[$key]
            $deltaRatio = ($x -and $y -and -not [double]::IsNaN($x.Ratio) -and -not [double]::IsNaN($y.Ratio)) ? [string]::Format($culture, '{0:+0.0;-0.0;0.0} %', ($y.Ratio / $x.Ratio - 1) * 100) : '—'
            $deltaAlloc = ($x -and $y) ? [string]::Format($culture, '{0:+0;-0;0} B', $y.Allocated - $x.Allocated) : '—'
            $cells = @(
                ($key -replace '^Comparison\.Benchmarks\.', ''),
                ($x ? (F $x.Mean '0.0') + ' ns' : '—'), ($x ? (F $x.Ratio '0.00') : '—'),
                ($y ? (F $y.Mean '0.0') + ' ns' : '—'), ($y ? (F $y.Ratio '0.00') : '—'),
                $deltaRatio,
                ($x ? "$($x.Allocated) B" : '—'), ($y ? "$($y.Allocated) B" : '—'), $deltaAlloc)
            [void] $sb.AppendLine('| ' + ($cells -join ' | ') + ' |')
        }
    }
    $path = Join-Path $Root 'summary.md'
    Set-Content -Path $path -Value $sb.ToString().Replace("`r`n", "`n") -NoNewline
    Write-Host
    Write-Host $sb.ToString()
    $path
}

if ($SummarizeOnly) {
    Write-Host "Summary: $(Write-QuickSummary (Resolve-Path $SummarizeOnly).Path)"
    return
}

# Keep the machine awake while measuring (Windows; normal sleep returns when the process ends).
if ($IsWindows) {
    Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@
    [Win32.Power]::SetThreadExecutionState([uint32]'0x80000001') | Out-Null
}

try {
    Write-Host "IronMapper quick A/B: $PublishedVersion (nuget.org) vs local sources"
    Write-Host "Filter: $($Filter -join ' '); frameworks: $($Frameworks -join ', '); job: $jobText"
    Write-Host "Output: $root"
    Write-Host
    Write-Host 'Pre-flight:'

    # ---- git ----
    $currentBranch = Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')
    $script:head = Invoke-Git @('rev-parse', 'HEAD')
    $changes = Invoke-Git @('status', '--porcelain')
    $tagCommit = Invoke-Git @('rev-parse', "v$PublishedVersion^{commit}")
    $diff = (& git -C $repo diff HEAD) -join "`n"
    $diffHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($diff))).Substring(0, 16)
    $gitFatal = -not $Dry
    if ($Branch) {
        Write-Check 'Branch' ($currentBranch -eq $Branch) "$currentBranch (expected $Branch)" -Fatal:$gitFatal
    }
    else {
        Write-Check 'Branch' $true "$currentBranch (not checked: -Branch not given)"
    }
    Write-Check 'Commit' ($null -ne $script:head) ("{0}{1}" -f $script:head, ($changes ? ", uncommitted changes (diff SHA-256 $diffHash)" : ', clean tree'))
    Write-Check "Tag v$PublishedVersion" ($null -ne $tagCommit) ($tagCommit ?? 'not found (git fetch --tags)') -Fatal
    if ($tagCommit -and $script:head -eq $tagCommit -and -not $changes) {
        Write-Check 'A/B' $false "HEAD is v$PublishedVersion without changes: both variants are the same code" -Fatal
    }
    $script:expectedPublished = "$PublishedVersion+$tagCommit"

    # ---- environment ----
    $sdk = (& dotnet --version).Trim()
    Write-Check '.NET SDK' ($sdk -like '10.*') "dotnet --version = $sdk (10.0.x expected)" -Fatal
    $runtimes = & dotnet --list-runtimes
    foreach ($framework in $Frameworks) {
        $major = ($framework -replace '^net(\d+)\..*$', '$1')
        $found = @($runtimes | Where-Object { $_ -like "Microsoft.NETCore.App $major.*" })
        Write-Check "Runtime $framework" ($found.Count -gt 0) ($found.Count -gt 0 ? (($found[-1] -split ' ')[1]) : "no Microsoft.NETCore.App $major.x") -Fatal
    }
    $free = Get-FreeMemoryBytes
    Write-Check 'Free memory' ($null -eq $free -or $free -ge 2GB) ($null -eq $free ? 'cannot be determined' : ('{0:N1} GB' -f ($free / 1GB)))
    $license = Test-AutoMapperLicense
    Write-Check 'AutoMapper license' $license.Ok $license.Detail

    # ---- the published package: restore, then compare with nuget.org ----
    $script:globalPackages = Get-GlobalPackagesFolder
    Set-Variant 'published'
    $restoreLog = Join-Path ([System.IO.Path]::GetTempPath()) "ironmapper-quick-restore-$stamp.log"
    & dotnet restore $project *> $restoreLog
    Write-Check "Restore of $PublishedVersion" ($LASTEXITCODE -eq 0) ($LASTEXITCODE -eq 0 ? 'ok' : "failed, log: $restoreLog") -Fatal
    if ($LASTEXITCODE -eq 0) {
        foreach ($id in 'IronMapper', 'IronMapper.Extensions.DI') {
            $check = Test-PublishedPackage $id
            Write-Check "$id $PublishedVersion" $check.Ok $check.Detail -Fatal
        }
    }

    $cases = $Dry ? 'Dry' : '~75 s per benchmark case and variant'
    Write-Host
    Write-Host "Runs: $($Frameworks.Count) framework(s) x 2 variants; $cases. Close other applications; keep a laptop on mains power."
    if ($problems.Count -gt 0) { throw "Pre-flight failed:`n  " + ($problems -join "`n  ") }
    if ($CheckOnly) { Write-Host 'Checks passed (-CheckOnly: nothing measured).'; return }

    New-Item -ItemType Directory -Force $root | Out-Null
    [ordered]@{
        Started          = Get-UtcStamp
        Machine          = Get-MachineSummary
        Branch           = $currentBranch
        Commit           = $script:head
        Uncommitted      = [bool] $changes
        DiffSha256       = $diffHash
        PublishedVersion = $PublishedVersion
        PublishedCommit  = $tagCommit
        Filter           = $Filter
        Frameworks       = $Frameworks
        Job              = $jobText
        Dry              = [bool] $Dry
    } | ConvertTo-Json | Set-Content (Join-Path $root 'run-info.json')

    # ---- runs ----
    $runs = [System.Collections.Generic.List[object]]::new()
    foreach ($framework in $Frameworks) {
        foreach ($variant in $variants) {
            $dir = Join-Path $root "$variant-$framework"
            New-Item -ItemType Directory -Force $dir | Out-Null
            Write-Host ("[{0:HH:mm:ss}] {1} on {2}: build" -f (Get-Date), $variant, $framework)
            Set-Variant $variant
            Clear-BuildOutputs
            Invoke-Logged (Join-Path $dir 'build.log') @('build', $project, '-c', 'Release', '-f', $framework, '-nologo')
            $hostInfo = Get-AssemblyInfo (Join-Path $project 'bin' 'Release' $framework) (Join-Path $bench 'src' 'Comparison.Mappers' 'obj' 'project.assets.json') $framework
            $hostProblem = Test-AssemblyInfo $hostInfo $variant
            if ($hostProblem) { throw "$variant on ${framework}: the build has the wrong IronMapper: $hostProblem" }

            Write-Host ("[{0:HH:mm:ss}] {1} on {2}: run" -f (Get-Date), $variant, $framework)
            $log = Join-Path $dir 'run.log'
            & dotnet run -c Release -f $framework --project $project --no-build -- --filter @Filter @job --keepFiles --artifacts $dir *> $log
            $code = $LASTEXITCODE
            $childInfos = Get-ChildBuildInfo $framework
            $childProblems = @($childInfos | ForEach-Object { Test-AssemblyInfo $_ $variant -ChildBuild } | Where-Object { $_ })
            if ($childInfos.Count -eq 0) { $childProblems += 'the child build of BenchmarkDotNet was not found' }
            [ordered]@{ Host = $hostInfo; ChildBuilds = $childInfos; Problems = $childProblems; ExitCode = $code } |
                ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dir 'assemblies.json')
            $status = Get-RunStatus $dir
            Write-Host ("[{0:HH:mm:ss}] {1} on {2}: {3}" -f (Get-Date), $variant, $framework, $status)
            $runs.Add([pscustomobject]@{ Variant = $variant; Framework = $framework; Status = $status })
        }
    }

    $summary = Write-QuickSummary $root
    Write-Host "Summary: $summary"
    if ($runs | Where-Object { $_.Status -ne 'ok' }) { exit 1 }
}
finally {
    Restore-Environment
    if ($IsWindows) { [Win32.Power]::SetThreadExecutionState([uint32]'0x80000000') | Out-Null }
}

<#
.SYNOPSIS
    NativeAOT probes against the packed IronMapper packages.

.DESCRIPTION
    1. Packs IronMapper and IronMapper.Extensions.DI (unless -SkipPack).
    2. Publishes tests/IronMapper.AotProbe (generated extension methods) with PublishAot for every framework, requires
       zero trim/AOT warnings, runs the executable and checks that it exits with 0 (it checks the mapped values).
    3. Publishes tests/IronMapper.AotProbe.DI (IMapper from the DI package) and requires IL2026 on the AddIronMapper
       line of its Program.cs and no other trim/AOT warning (no summary IL2104 for an IronMapper assembly). Not run:
       IMapper does not work in NativeAOT yet (plan item 1.3, issue #26).
    The probes are restored into an isolated package cache: IronMapper* only from -PackageDir, everything else
    (Microsoft.Extensions.DependencyInjection, the ILCompiler packages) from nuget.org.
    Needs the NativeAOT prerequisites (Linux: clang, zlib1g-dev; Windows: the C++ build tools of Visual Studio).

.EXAMPLE
    pwsh ./eng/verify-aot.ps1
    pwsh ./eng/verify-aot.ps1 -PackageDir ./artifacts/packages -SkipPack -Framework net10.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $PackageDir,
    [string] $Configuration = 'Release',
    [string[]] $Framework = @('net8.0', 'net10.0'),
    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackageDir) { $PackageDir = Join-Path $repoRoot 'artifacts/packages' }
$PackageDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDir)
$rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

# The Windows NativeAOT toolchain (findvcvarsall.bat) runs vswhere.exe by bare name; when the Visual Studio Installer
# folder is not on PATH the lookup fails and the publish fails with MSB3073 (same fix as benchmarks/scripts).
if ($IsWindows -and -not (Get-Command 'vswhere.exe' -CommandType Application -ErrorAction SilentlyContinue)) {
    $installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer'
    if (Test-Path (Join-Path $installerDir 'vswhere.exe')) { $env:PATH = "$installerDir$([System.IO.Path]::PathSeparator)$env:PATH" }
}

if (-not $Version) {
    $Version = (& dotnet msbuild (Join-Path $repoRoot 'src/IronMapper/IronMapper.csproj') -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $Version) { throw 'Could not read Version from src/IronMapper/IronMapper.csproj' }
}
Write-Host "NativeAOT probes for IronMapper $Version ($rid; $($Framework -join ', '))"

if (-not $SkipPack) {
    foreach ($project in 'src/IronMapper/IronMapper.csproj', 'src/IronMapper.Extensions.DI/IronMapper.Extensions.DI.csproj') {
        Invoke-Dotnet pack (Join-Path $repoRoot $project) -c $Configuration -o $PackageDir "-p:Version=$Version"
    }
}

$probeA = Join-Path $repoRoot 'tests/IronMapper.AotProbe/IronMapper.AotProbe.csproj'
$probeB = Join-Path $repoRoot 'tests/IronMapper.AotProbe.DI/IronMapper.AotProbe.DI.csproj'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "ironmapper-aot-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

# Package source mapping: the released IronMapper with the same version on nuget.org must never be picked.
$nugetConfig = Join-Path $work 'nuget.config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$([System.Security.SecurityElement]::Escape($PackageDir))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="IronMapper*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -Path $nugetConfig -Encoding utf8

# Publishes one probe for one framework; returns the IL warnings (unique, the same warning is printed by the analyzer
# and by ILC) and the output directory.
function Publish-Probe([string] $project, [string] $tf) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
    $out = Join-Path $work "$name-$tf"
    $log = Join-Path $work "$name-$tf.log"
    $props = "-p:IronMapperVersion=$Version", "-p:RestorePackagesPath=$(Join-Path $work 'packages')", "-p:RestoreConfigFile=$nugetConfig"

    $started = Get-Date
    & dotnet publish $project -c $Configuration -f $tf -r $rid -o $out @props 2>&1 | Tee-Object -FilePath $log | Out-Host
    $exit = $LASTEXITCODE
    Write-Host ("{0} {1}: publish {2:N0} s, exit code {3}" -f $name, $tf, ((Get-Date) - $started).TotalSeconds, $exit)

    $warnings = @(Get-Content $log | Where-Object { $_ -match '\b(warning|error) IL\d{4}' } |
        ForEach-Object { ($_ -replace '\s*\[[^\]]*\.csproj[^\]]*\]\s*$', '').Trim() } | Sort-Object -Unique)
    [pscustomobject]@{ Name = $name; Framework = $tf; ExitCode = $exit; Warnings = $warnings; Output = $out }
}

$errors = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($tf in $Framework) {
        # A: generated extension methods: no warnings, the executable runs and checks the mapped values.
        $a = Publish-Probe $probeA $tf
        if ($a.ExitCode -ne 0) { $errors.Add("$($a.Name) ${tf}: publish failed (exit code $($a.ExitCode))") }
        foreach ($w in $a.Warnings) { $errors.Add("$($a.Name) ${tf}: unexpected warning: $w") }
        if ($a.ExitCode -eq 0) {
            $exe = Join-Path $a.Output ($IsWindows ? "$($a.Name).exe" : $a.Name)
            & $exe | Out-Host
            if ($LASTEXITCODE -ne 0) { $errors.Add("$($a.Name) ${tf}: the NativeAOT executable exited with $LASTEXITCODE") }
        }

        # B: IMapper: exactly IL2026 on the AddIronMapper call of Program.cs, nothing else.
        $b = Publish-Probe $probeB $tf
        $source = Join-Path (Split-Path -Parent $probeB) 'Program.cs'
        $line = (Select-String -Path $source -Pattern 'AddIronMapper\(' | Select-Object -First 1).LineNumber
        # The analyzer prints 'Program.cs(8,1): warning IL2026', ILC 'Program.cs(8): Trim analysis warning IL2026'.
        $expected = "Program\.cs\($line(,\d+)?\): (Trim analysis )?warning IL2026: .*AddIronMapper"
        if ($b.ExitCode -ne 0) { $errors.Add("$($b.Name) ${tf}: publish failed (exit code $($b.ExitCode))") }
        if (-not ($b.Warnings | Where-Object { $_ -match $expected })) {
            $errors.Add("$($b.Name) ${tf}: no IL2026 for AddIronMapper at Program.cs line $line")
        }
        foreach ($w in $b.Warnings | Where-Object { $_ -notmatch $expected }) {
            $errors.Add("$($b.Name) ${tf}: unexpected warning: $w")
        }
        foreach ($w in $b.Warnings | Where-Object { $_ -match $expected }) { Write-Host "$($b.Name) ${tf}: expected: $w" }
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "::error::$_" }
    throw "NativeAOT probes failed ($($errors.Count) problem(s))."
}
Write-Host "NativeAOT probes passed for IronMapper $Version."

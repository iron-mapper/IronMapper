<#
.SYNOPSIS
    NativeAOT probes against the packed IronMapper packages.

.DESCRIPTION
    1. Packs IronMapper and IronMapper.Extensions.DI (unless -SkipPack).
    2. Publishes the probes with PublishAot for every framework, requires zero trim/AOT warnings and runs the
       executables (each checks the mapped values and exits with 0). The ILC map file shows whether the generated
       registration for IMapper (feature switch IronMapper.Registration.IsSupported) is in the executable:
       - A: tests/IronMapper.AotProbe, generated extension methods, no DI package: the registration must be removed;
       - A with IronMapperRegistration=true: the registration must stay (explicit override);
       - B: tests/IronMapper.AotProbe.DI, IMapper (registry, collections, runtime types, original exceptions);
       - B with IronMapperRegistration=false: the executable must fail with the message that names the property;
       - C: tests/IronMapper.AotProbe.Transitive, IMapper through a library that references the DI package, mappings
         in a library without it; also run with the JIT (dotnet run) first.
       The sizes of A with and without the registration are printed.
    3. Publishes A and B trimmed without NativeAOT (ILLink: PublishTrimmed, self-contained) with the same requirements;
       whether the registration is in the trimmed probe assembly is read from the assembly itself (the name of the
       generated RegisterIronMapperMappingsCore is gone when the trimmer removed the method).
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
$probeC = Join-Path $repoRoot 'tests/IronMapper.AotProbe.Transitive/IronMapper.AotProbe.Transitive.csproj'
# Registration: whether the generated registration must be in the executable. Fails: the text the executable must
# fail with (exit code not 0). Jit: run it with the JIT (dotnet run) before the publish.
$cases = @(
    @{ Name = 'A'; Project = $probeA; Registration = $false }
    @{ Name = 'A, IronMapperRegistration=true'; Project = $probeA; Properties = @('-p:IronMapperRegistration=true')
       Registration = $true }
    @{ Name = 'B'; Project = $probeB; Registration = $true }
    @{ Name = 'B, IronMapperRegistration=false'; Project = $probeB; Properties = @('-p:IronMapperRegistration=false')
       Registration = $false; Fails = '<IronMapperRegistration>true</IronMapperRegistration>' }
    @{ Name = 'C'; Project = $probeC; Registration = $true; Jit = $true }
    @{ Name = 'A, PublishTrimmed'; Project = $probeA; Trimmed = $true; Registration = $false }
    @{ Name = 'B, PublishTrimmed'; Project = $probeB; Trimmed = $true; Registration = $true }
)
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

$restoreProps = @("-p:IronMapperVersion=$Version", "-p:RestorePackagesPath=$(Join-Path $work 'packages')",
    "-p:RestoreConfigFile=$nugetConfig")

# Publishes one case for one framework (own artifacts folder, so the variants of one project do not share obj/);
# returns the IL warnings (unique, the same warning is printed by the analyzer and by ILC/ILLink), the executable and
# the file that shows whether the generated registration was kept: the ILC map file (NativeAOT) or the trimmed probe
# assembly (ILLink).
function Publish-Probe($case, [string] $tf, [int] $index) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($case.Project)
    $out = Join-Path $work "out$index-$tf"
    $log = Join-Path $work "out$index-$tf.log"
    $artifacts = Join-Path $work "a$index-$tf"
    $props = $restoreProps + '-p:IlcGenerateMapFile=true'
    if ($case.ContainsKey('Trimmed')) { $props += '-p:PublishAot=false', '-p:PublishTrimmed=true', '--self-contained' }
    if ($case.ContainsKey('Properties')) { $props += $case.Properties }

    $started = Get-Date
    & dotnet publish $case.Project -c $Configuration -f $tf -r $rid -o $out --artifacts-path $artifacts @props 2>&1 |
        Tee-Object -FilePath $log | Out-Host
    $exit = $LASTEXITCODE
    Write-Host ("{0} {1}: publish {2:N0} s, exit code {3}" -f $case.Name, $tf, ((Get-Date) - $started).TotalSeconds, $exit)

    $warnings = @(Get-Content $log | Where-Object { $_ -match '\b(warning|error) IL\d{4}' } |
        ForEach-Object { ($_ -replace '\s*\[[^\]]*\.csproj[^\]]*\]\s*$', '').Trim() } | Sort-Object -Unique)
    $map = if ($case.ContainsKey('Trimmed')) { Get-Item (Join-Path $out "$name.dll") -ErrorAction SilentlyContinue }
    else { Get-ChildItem -Path $artifacts -Recurse -Filter "$name.map.xml" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    [pscustomobject]@{
        ExitCode = $exit; Warnings = $warnings; Map = $map; Executable = Join-Path $out ($IsWindows ? "$name.exe" : $name)
    }
}

$errors = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($tf in $Framework) {
        $sizes = @{}
        for ($i = 0; $i -lt $cases.Count; $i++) {
            $case = $cases[$i]
            $label = "$($case.Name) ${tf}"

            if ($case.ContainsKey('Jit')) {
                & dotnet run --project $case.Project -c $Configuration -f $tf --artifacts-path (Join-Path $work "jit$i-$tf") @restoreProps |
                    Out-Host
                if ($LASTEXITCODE -ne 0) { $errors.Add("${label}: the JIT run exited with $LASTEXITCODE") }
            }

            $p = Publish-Probe $case $tf $i
            if ($p.ExitCode -ne 0) { $errors.Add("${label}: publish failed (exit code $($p.ExitCode))"); continue }
            foreach ($w in $p.Warnings) { $errors.Add("${label}: unexpected warning: $w") }
            $sizes[$case.Name] = (Get-Item $p.Executable).Length

            if ($null -eq $p.Map) { $errors.Add("${label}: no ILC map file or trimmed probe assembly") }
            else {
                # Latin1 keeps every byte, so the name is found in the metadata of an assembly as well as in the map file.
                $text = [System.IO.File]::ReadAllText($p.Map.FullName, [System.Text.Encoding]::Latin1)
                $registered = $text.Contains('RegisterIronMapperMappingsCore')
                if ($registered -ne $case.Registration) {
                    $errors.Add("${label}: generated registration in the executable: expected $($case.Registration), found $registered")
                }
            }

            $output = & $p.Executable 2>&1 | Out-String
            $exitCode = $LASTEXITCODE
            Write-Host $output.TrimEnd()
            if ($case.ContainsKey('Fails')) {
                if ($exitCode -eq 0 -or -not $output.Contains($case.Fails)) {
                    $errors.Add("${label}: expected a failure that mentions '$($case.Fails)', got exit code $exitCode")
                }
            }
            elseif ($exitCode -ne 0) { $errors.Add("${label}: the published executable exited with $exitCode") }
        }

        if ($sizes.ContainsKey('A') -and $sizes.ContainsKey('A, IronMapperRegistration=true')) {
            Write-Host ("Probe A {0}: {1:N0} bytes; with the registration (IronMapperRegistration=true) {2:N0} bytes" -f
                $tf, $sizes['A'], $sizes['A, IronMapperRegistration=true'])
        }
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

<#
.SYNOPSIS
    Packs IronMapper and verifies that the resulting .nupkg files are usable by consumers.

.DESCRIPTION
    1. Packs IronMapper and IronMapper.Extensions.DI (unless -SkipPack).
    2. Checks the .nupkg layout: lib/ contains the runtime assembly, analyzers/dotnet/cs contains
       the source generator, and Roslyn (Microsoft.CodeAnalysis.*) is not a package dependency.
    3. Restores tests/IronMapper.PackageConsumer from the local feed only (isolated package cache),
       builds it and runs it. The consumer calls generated MapTo* methods, so the build fails
       if the source generator is not loaded from the package.

.EXAMPLE
    pwsh ./eng/verify-package.ps1
    pwsh ./eng/verify-package.ps1 -Version 1.1.1 -PackageDir ./nupkgs -SkipPack
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $PackageDir,
    [string] $Configuration = 'Release',
    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackageDir) { $PackageDir = Join-Path $repoRoot 'artifacts/packages' }
$PackageDir = [System.IO.Path]::GetFullPath($PackageDir)

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not $Version) {
    $Version = (& dotnet msbuild (Join-Path $repoRoot 'src/IronMapper/IronMapper.csproj') -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $Version) { throw 'Could not read Version from src/IronMapper/IronMapper.csproj' }
}
Write-Host "Verifying IronMapper $Version packages in $PackageDir"

# ---------------------------------------------------------------------------
# 1. Pack
# ---------------------------------------------------------------------------
if (-not $SkipPack) {
    foreach ($project in 'src/IronMapper/IronMapper.csproj', 'src/IronMapper.Extensions.DI/IronMapper.Extensions.DI.csproj') {
        Invoke-Dotnet pack (Join-Path $repoRoot $project) -c $Configuration -o $PackageDir "-p:Version=$Version"
    }
}

# ---------------------------------------------------------------------------
# 2. Inspect .nupkg contents
# ---------------------------------------------------------------------------
$errors = [System.Collections.Generic.List[string]]::new()

function Read-Package([string] $id) {
    $path = Join-Path $PackageDir "$id.$Version.nupkg"
    if (-not (Test-Path $path)) { throw "Package not found: $path" }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -eq "$id.nuspec" }
        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    $dependencies = @($nuspec.package.metadata.dependencies.group | ForEach-Object {
        if ($_.PSObject.Properties['dependency']) { $_.dependency }
    })
    [pscustomobject]@{ Id = $id; Entries = $entries; Dependencies = $dependencies }
}

function Assert-Entry($package, [string] $entry) {
    if ($package.Entries -notcontains $entry) { $errors.Add("$($package.Id): missing '$entry'") }
}

function Assert-NoRoslynDependency($package) {
    foreach ($dep in $package.Dependencies) {
        if ($dep.id -like 'Microsoft.CodeAnalysis*' -or $dep.id -like 'IronMapper.Generator*' -or $dep.id -like 'IronMapper.CodeFixes*') {
            $errors.Add("$($package.Id): unexpected package dependency '$($dep.id)'")
        }
    }
}

$core = Read-Package 'IronMapper'
Assert-Entry $core 'lib/netstandard2.0/IronMapper.dll'
Assert-Entry $core 'lib/net10.0/IronMapper.dll'
Assert-Entry $core 'analyzers/dotnet/cs/IronMapper.Generator.dll'
Assert-Entry $core 'analyzers/dotnet/cs/IronMapper.CodeFixes.dll'
Assert-NoRoslynDependency $core
foreach ($entry in $core.Entries) {
    if ($entry -like 'lib/*' -and $entry -notlike 'lib/*/IronMapper.dll' -and $entry -notlike 'lib/*/IronMapper.xml') {
        $errors.Add("IronMapper: unexpected file in lib/: '$entry'")
    }
    if ($entry -like 'analyzers/*' -and $entry -notlike 'analyzers/dotnet/cs/IronMapper.*.dll') {
        $errors.Add("IronMapper: unexpected file in analyzers/: '$entry'")
    }
}

$di = Read-Package 'IronMapper.Extensions.DI'
Assert-Entry $di 'lib/netstandard2.1/IronMapper.Extensions.DI.dll'
Assert-Entry $di 'lib/net10.0/IronMapper.Extensions.DI.dll'
Assert-NoRoslynDependency $di
foreach ($dep in $di.Dependencies | Where-Object { $_.id -eq 'IronMapper' }) {
    if ($dep.version -ne $Version -and $dep.version -ne "[$Version]") {
        $errors.Add("IronMapper.Extensions.DI: depends on IronMapper '$($dep.version)', expected '$Version'")
    }
    # The generator must flow transitively to consumers that only reference the DI package.
    if ($dep.PSObject.Properties['exclude'] -and $dep.exclude -match 'Analyzers') {
        $errors.Add("IronMapper.Extensions.DI: dependency on IronMapper excludes analyzers ('$($dep.exclude)')")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "::error::$_" }
    throw "Package layout verification failed ($($errors.Count) problem(s))."
}
Write-Host 'Package layout OK.'

# ---------------------------------------------------------------------------
# 3. Build and run a consumer project against the packed IronMapper.nupkg
# ---------------------------------------------------------------------------
$consumer = Join-Path $repoRoot 'tests/IronMapper.PackageConsumer/IronMapper.PackageConsumer.csproj'
# A fresh package cache guarantees the package comes from $PackageDir, not from a stale global cache.
$packagesPath = Join-Path ([System.IO.Path]::GetTempPath()) "ironmapper-pkgcheck-$([guid]::NewGuid().ToString('N'))"

try {
    Invoke-Dotnet restore $consumer --force `
        "-p:IronMapperVersion=$Version" `
        "-p:RestoreSources=$PackageDir" `
        "-p:RestorePackagesPath=$packagesPath"
    Invoke-Dotnet build $consumer --no-restore -c $Configuration "-p:IronMapperVersion=$Version"
    Invoke-Dotnet run --project $consumer --no-build -c $Configuration
}
finally {
    Remove-Item -Recurse -Force $packagesPath -ErrorAction SilentlyContinue
}

Write-Host "IronMapper $Version packages verified."

<#
.SYNOPSIS
    Packs IronMapper and verifies that the resulting .nupkg files are usable by consumers.

.DESCRIPTION
    1. Packs IronMapper and IronMapper.Extensions.DI (unless -SkipPack).
    2. Checks the .nupkg layout: lib/ contains the runtime assembly, analyzers/dotnet/cs contains
       the source generator, and Roslyn (Microsoft.CodeAnalysis.*) is not a package dependency.
    3. Restores tests/IronMapper.PackageConsumer from the local feed only (isolated package cache),
       builds it and runs it. The consumer calls generated MapTo* methods, so the build fails
       if the source generator is not loaded from the package. It references two libraries that also
       use the package (tests/IronMapper.PackageConsumer.InternalLib, .PublicLib), which are restored
       and built with it.
    4. Builds tests/IronMapper.PackageConsumer.ImplicitUsings from the same feed in several variants: the global
       using IronMapper.Generated from buildTransitive/IronMapper.props (ImplicitUsings on and no using; only the DI
       package referenced; a project without mappings; the Quick Start of README.md and README.ru.md, marked with
       <!-- quickstart:begin --> / <!-- quickstart:end -->) and the cases
       where it must not be added or must not get in the way (ImplicitUsings off, LangVersion 9,
       IronMapperImplicitUsings=false, an explicit using next to the global one; with EnforceCodeStyleInBuild and
       IDE0005 as an error the explicit using must fail the build with IDE0005 at its line, and build with
       IronMapperImplicitUsings=false), and netstandard2.0/2.1 libraries
       (the generated throw helper instead of ArgumentNullException.ThrowIfNull, the generated copy of
       ModuleInitializerAttribute, C# 8 without a module initializer). Each executable variant is also run.
       The feature switch IronMapper.Registration.IsSupported (buildTransitive/IronMapper.targets) is checked in the
       runtimeconfig.json of every executable variant: not set by default and with the DI package, false with
       PublishTrimmed and without the DI package, true with IronMapperRegistration=true; a framework-dependent publish
       without the DI package keeps the registration (the registry finds the mapping by type). IronMapperRegistration=false
       set in the project generates no registration (no RegisterIronMapperMappings, no private MapInto).
       IronMapperNullSourceReturnsNull=true: null -> null without a nullable warning in the user's code (TreatWarningsAsErrors),
       also in a netstandard2.0 library (the generated copy of NotNullIfNotNullAttribute); IronMapperEmitPolyfills=false
       declares no copy there (the build fails without another declaration).
       IronMapperReportUnmappedInProfiles (CompilerVisibleProperty in buildTransitive/IronMapper.props): a profile
       with an unmapped property builds without it; with it the build fails with IM0001 at the CreateMap line
       (TreatWarningsAsErrors), and .editorconfig dotnet_diagnostic.IM0001.severity applies (error without
       TreatWarningsAsErrors, none with it).
       IM0002: an attribute mapping with an incompatible property type fails the build with IM0002 at the destination
       property and with no error inside a generated file.
       IronMapperGenerateInPlace/List/Array, IronMapperExcludeFromCodeCoverage and IronMapperGeneratedCodeAttribute: the
       public overloads and the attributes of the generated class GeneratedMappers_{AssemblyName} ([GeneratedCode] with the
       package version, [ExcludeFromCodeCoverage]) with the defaults and
       with everything turned off; the registry used by IMapper maps List, array and in-place either way.

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

# Paths inside the repository are built from the script location, never from the current directory.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackageDir) { $PackageDir = Join-Path $repoRoot 'artifacts/packages' }
# Resolve a relative -PackageDir against the PowerShell location (Set-Location / cd), not the .NET process
# directory: [IO.Path]::GetFullPath uses the latter, e.g. C:\WINDOWS\system32. Works for folders that do not exist yet.
$PackageDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDir)

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
Assert-Entry $core 'buildTransitive/IronMapper.props'
Assert-Entry $core 'buildTransitive/IronMapper.targets'
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
Assert-Entry $di 'buildTransitive/IronMapper.Extensions.DI.props'
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

    # -----------------------------------------------------------------------
    # 4. global using IronMapper.Generated (buildTransitive/IronMapper.props)
    # -----------------------------------------------------------------------
    $implicitConsumer = Join-Path $repoRoot 'tests/IronMapper.PackageConsumer.ImplicitUsings/IronMapper.PackageConsumer.ImplicitUsings.csproj'
    $sources = Join-Path (Split-Path -Parent $implicitConsumer) 'Sources'
    $variantsRoot = Join-Path $packagesPath 'implicit-usings'
    New-Item -ItemType Directory -Force -Path $variantsRoot | Out-Null

    # The DI variant needs Microsoft.Extensions.DependencyInjection.Abstractions from nuget.org. Package source
    # mapping keeps IronMapper* on the local feed: the released package with the same version must never be picked.
    $nugetConfig = Join-Path $variantsRoot 'nuget.config'
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

    # The Quick Start of a README: the csharp block between <!-- quickstart:begin --> and <!-- quickstart:end -->
    # (HTML comments, not shown on GitHub or nuget.org) is a whole Program.cs.
    function Export-QuickStart([string] $readme, [string] $name) {
        $text = (Get-Content -Raw -Encoding utf8 (Join-Path $repoRoot $readme)) -replace "`r`n", "`n"
        $found = [regex]::Matches($text, '(?ms)^<!-- quickstart:begin -->\s*^```csharp\n(.*?)^```\s*^<!-- quickstart:end -->')
        if ($found.Count -ne 1) { throw "Expected one quickstart:begin/end block with a csharp block in $readme, found $($found.Count)" }
        $match = $found[0]
        $path = Join-Path $variantsRoot "$name.cs"
        Set-Content -Path $path -Value $match.Groups[1].Value -NoNewline -Encoding utf8
        $path
    }

    # A copy of a file of Sources/ in its own folder with an .editorconfig (applied by the path of the file).
    function Export-WithEditorConfig([string] $name, [string] $rules, [string] $file = 'ProfileUnmapped.cs') {
        $folder = Join-Path $variantsRoot $name
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Copy-Item (Join-Path $sources $file) $folder
        "root = true`n`n[*.cs]`n$rules`n" | Set-Content -Path (Join-Path $folder '.editorconfig') -NoNewline -Encoding utf8
        Join-Path $folder $file
    }

    # IM0001 in the build output: at the CreateMap call of ProfileUnmapped.cs (file, line, column).
    $createMapLine = (Select-String -Path (Join-Path $sources 'ProfileUnmapped.cs') -SimpleMatch 'CreateMap<').LineNumber
    $unmappedError = "ProfileUnmapped\.cs\($createMapLine,\d+\): error IM0001: Property 'Missing' on destination type 'OrderDto'"

    # IM0002 in the build output: at OrderDto.Count of IncompatibleProperty.cs (the destination property), and no error
    # inside a generated file (BuildErrorNot).
    $countLine = (Select-String -Path (Join-Path $sources 'IncompatibleProperty.cs') -Pattern '^\s+public int Count' |
        Select-Object -First 1).LineNumber
    $incompatibleError = "IncompatibleProperty\.cs\($countLine,\d+\): error IM0002: Property 'Count' of 'OrderDto' is not mapped"

    # A strict code style (IDE0005 as an error in the build) and an explicit `using IronMapper.Generated;` next to the
    # global one of the package: IDE0005 at that line. The compiler reports IDE0005 in the build only with
    # GenerateDocumentationFile (CS1591 turned off: the consumer has no XML comments).
    $strictStyle = "dotnet_diagnostic.IDE0005.severity = error`ndotnet_diagnostic.CS1591.severity = none"
    $strictProperties = @('-p:EnforceCodeStyleInBuild=true', '-p:GenerateDocumentationFile=true')
    $generatedUsingLine = (Select-String -Path (Join-Path $sources 'ExplicitUsingStrict.cs') -Pattern '^using IronMapper\.Generated;' |
        Select-Object -First 1).LineNumber
    $unnecessaryUsingError = "ExplicitUsingStrict\.cs\($generatedUsingLine,\d+\): error IDE0005:"

    $variants = @(
        @{ Name = 'no using, ImplicitUsings on'; Source = "$sources/NoUsing.cs"; GlobalUsing = $true; Output = 'NoUsing: OK' }
        @{ Name = 'no using, only IronMapper.Extensions.DI referenced'; Source = "$sources/NoUsing.cs"; GlobalUsing = $true
           Output = 'NoUsing: OK'; Properties = @('-p:IronMapperPackage=IronMapper.Extensions.DI') }
        @{ Name = 'no mappings in the project'; Source = "$sources/NoMappings.cs"; GlobalUsing = $true; Output = 'NoMappings: OK' }
        @{ Name = 'README.md Quick Start'; Source = (Export-QuickStart 'README.md' 'QuickStart.en')
           GlobalUsing = $true; Output = 'Alice' }
        @{ Name = 'README.ru.md Quick Start'; Source = (Export-QuickStart 'README.ru.md' 'QuickStart.ru')
           GlobalUsing = $true; Output = 'Alice' }
        @{ Name = 'explicit using next to the global one'; Source = "$sources/ExplicitUsing.cs"; GlobalUsing = $true
           Output = 'ExplicitUsing: OK' }
        @{ Name = 'explicit using, EnforceCodeStyleInBuild, IDE0005 as error (IDE0005 at the using)'
           Source = (Export-WithEditorConfig 'strict-style' $strictStyle 'ExplicitUsingStrict.cs'); GlobalUsing = $true
           BuildError = $unnecessaryUsingError; Properties = $strictProperties }
        @{ Name = 'explicit using, EnforceCodeStyleInBuild, IDE0005 as error, IronMapperImplicitUsings=false'
           Source = (Export-WithEditorConfig 'strict-style-off' $strictStyle 'ExplicitUsingStrict.cs'); GlobalUsing = $false
           Output = 'ExplicitUsingStrict: OK'; Properties = $strictProperties + @('-p:IronMapperImplicitUsings=false') }
        @{ Name = 'ImplicitUsings off'; Source = "$sources/ExplicitUsing.cs"; GlobalUsing = $false
           Output = 'ExplicitUsing: OK'; Properties = @('-p:ImplicitUsings=disable') }
        @{ Name = 'ImplicitUsings off, LangVersion 9'; Source = "$sources/ExplicitUsing.cs"; GlobalUsing = $false
           Output = 'ExplicitUsing: OK'; Properties = @('-p:ImplicitUsings=disable', '-p:LangVersion=9') }
        @{ Name = 'IronMapperImplicitUsings=false'; Source = "$sources/ExplicitUsing.cs"; GlobalUsing = $false
           Output = 'ExplicitUsing: OK'; Properties = @('-p:IronMapperImplicitUsings=false') }
        # Feature switch IronMapper.Registration.IsSupported (Switch: its value in runtimeconfig.json, $null = not set).
        @{ Name = 'framework-dependent publish without the DI package (registration stays)'; Source = "$sources/Registry.cs"
           GlobalUsing = $true; Output = 'Registry: OK'; Publish = $true }
        @{ Name = 'PublishTrimmed=true without the DI package (switch off)'; Source = "$sources/NoUsing.cs"; GlobalUsing = $true
           Output = 'NoUsing: OK'; Switch = 'false'; Properties = @('-p:PublishTrimmed=true') }
        @{ Name = 'PublishTrimmed=true, only IronMapper.Extensions.DI referenced (switch not set)'; Source = "$sources/NoUsing.cs"
           GlobalUsing = $true; Output = 'NoUsing: OK'
           Properties = @('-p:PublishTrimmed=true', '-p:IronMapperPackage=IronMapper.Extensions.DI') }
        @{ Name = 'PublishTrimmed=true, IronMapperRegistration=true'; Source = "$sources/Registry.cs"; GlobalUsing = $true
           Output = 'Registry: OK'; Switch = 'true'; Properties = @('-p:PublishTrimmed=true', '-p:IronMapperRegistration=true') }
        # IronMapperRegistration=false in the project: no registration generated (item 2.16), the switch off.
        @{ Name = 'IronMapperRegistration=false (no registration generated)'; Source = "$sources/RegistrationOff.cs"
           GlobalUsing = $true; Output = 'RegistrationOff: OK'; Switch = 'false'; Properties = @('-p:IronMapperRegistration=false') }
        # Libraries on targets without ArgumentNullException.ThrowIfNull and ModuleInitializerAttribute: the generated
        # code uses its own throw helper, with [DoesNotReturn] on netstandard2.1 and without it on netstandard2.0, and
        # declares an internal ModuleInitializerAttribute for the IMapper registration; with C# 8 the registration has
        # no [ModuleInitializer] at all. Built only (a library is not run).
        @{ Name = 'netstandard2.0 library (throw helper, ModuleInitializerAttribute copy)'; Source = "$sources/ExplicitUsing.cs"
           GlobalUsing = $true; Run = $false; Properties = @('-p:TargetFramework=netstandard2.0', '-p:OutputType=Library') }
        @{ Name = 'netstandard2.1 library (throw helper, DoesNotReturn)'; Source = "$sources/ExplicitUsing.cs"; GlobalUsing = $true
           Run = $false; Properties = @('-p:TargetFramework=netstandard2.1', '-p:OutputType=Library') }
        @{ Name = 'netstandard2.0 library, C# 8 (registration without module initializer)'; Source = "$sources/ExplicitUsing.cs"
           GlobalUsing = $false; Run = $false
           Properties = @('-p:TargetFramework=netstandard2.0', '-p:OutputType=Library', '-p:ImplicitUsings=disable', '-p:LangVersion=8') }
        # IM0001 for profile mappings (IronMapperReportUnmappedInProfiles, off by default). BuildError: the build
        # must fail and its output must match this pattern.
        @{ Name = 'profile with an unmapped property, IronMapperReportUnmappedInProfiles not set'
           Source = "$sources/ProfileUnmapped.cs"; GlobalUsing = $true; Output = 'ProfileUnmapped: OK' }
        @{ Name = 'IronMapperReportUnmappedInProfiles=true (IM0001 at the CreateMap line, TreatWarningsAsErrors)'
           Source = "$sources/ProfileUnmapped.cs"; GlobalUsing = $true; BuildError = $unmappedError
           Properties = @('-p:IronMapperReportUnmappedInProfiles=true') }
        @{ Name = 'IronMapperReportUnmappedInProfiles=true, .editorconfig severity error, TreatWarningsAsErrors=false'
           Source = (Export-WithEditorConfig 'editorconfig-error' 'dotnet_diagnostic.IM0001.severity = error'); GlobalUsing = $true; BuildError = $unmappedError
           Properties = @('-p:IronMapperReportUnmappedInProfiles=true', '-p:TreatWarningsAsErrors=false') }
        @{ Name = 'IronMapperReportUnmappedInProfiles=true, .editorconfig severity none'
           Source = (Export-WithEditorConfig 'editorconfig-none' 'dotnet_diagnostic.IM0001.severity = none'); GlobalUsing = $true; Output = 'ProfileUnmapped: OK'
           Properties = @('-p:IronMapperReportUnmappedInProfiles=true') }
        # IM0002: an incompatible property fails the build at the user's code, not inside a generated file.
        @{ Name = 'incompatible property type (IM0002 at the destination property, nothing in the generated files)'
           Source = "$sources/IncompatibleProperty.cs"; GlobalUsing = $true; BuildError = $incompatibleError
           BuildErrorNot = '\.g\.cs\(\d+,\d+\): error' }
        # Optional overloads and the attributes of the generated class (Sources/GeneratedOptions.cs checks them by reflection).
        @{ Name = 'generated class attributes and overloads, defaults'; Source = "$sources/GeneratedOptions.cs"; GlobalUsing = $true
           Output = 'GeneratedOptions: OK' }
        @{ Name = 'IronMapperGenerateInPlace/List/Array=false, IronMapperExcludeFromCodeCoverage=true, IronMapperGeneratedCodeAttribute=false'
           Source = "$sources/GeneratedOptions.cs"; GlobalUsing = $true; Output = 'GeneratedOptions: OK'
           Properties = @('-p:IronMapperGenerateInPlace=false', '-p:IronMapperGenerateList=false', '-p:IronMapperGenerateArray=false',
                          '-p:IronMapperExcludeFromCodeCoverage=true', '-p:IronMapperGeneratedCodeAttribute=false',
                          '-p:DefineConstants=OVERLOADS_OFF') }
        # IronMapperNullSourceReturnsNull (issue #27): MapToX(this S? source) -> D? with NotNullIfNotNull; the source maps a
        # non-null order and uses the result as non-null, so a missing or unread attribute fails the build (CS8602,
        # TreatWarningsAsErrors). On netstandard2.0 the generator declares the attribute.
        @{ Name = 'IronMapperNullSourceReturnsNull=true (null -> null, no nullable warning in user code)'
           Source = "$sources/NullSource.cs"; GlobalUsing = $true; Output = 'NullSource: OK'
           Properties = @('-p:IronMapperNullSourceReturnsNull=true') }
        @{ Name = 'netstandard2.0 library, IronMapperNullSourceReturnsNull=true (NotNullIfNotNullAttribute copy)'
           Source = "$sources/NullSource.cs"; GlobalUsing = $true; Run = $false
           Properties = @('-p:TargetFramework=netstandard2.0', '-p:OutputType=Library', '-p:IronMapperNullSourceReturnsNull=true') }
        # IronMapperEmitPolyfills=false (for a project where PolySharp declares the attributes): no copies, so without
        # another declaration the build fails at the attribute in the generated file; proves the property reaches the generator.
        @{ Name = 'netstandard2.0 library, IronMapperEmitPolyfills=false (no attribute copies)'
           Source = "$sources/NullSource.cs"; GlobalUsing = $true; Run = $false
           BuildError = 'error CS02(34|46).*NotNullIfNotNull'
           Properties = @('-p:TargetFramework=netstandard2.0', '-p:OutputType=Library', '-p:IronMapperNullSourceReturnsNull=true',
                          '-p:IronMapperEmitPolyfills=false') }
    )

    $index = 0
    foreach ($variant in $variants) {
        $index++
        Write-Host "Implicit usings, variant ${index}: $($variant.Name)"
        # Own artifacts folder per variant, so restore results and obj/ of different properties do not mix.
        $artifacts = Join-Path $variantsRoot "v$index"
        $properties = @("-p:IronMapperVersion=$Version", "-p:ConsumerSource=$($variant.Source)")
        if ($variant.ContainsKey('Properties')) { $properties += $variant.Properties }

        Invoke-Dotnet restore $implicitConsumer --force --artifacts-path $artifacts --configfile $nugetConfig `
            "-p:RestorePackagesPath=$packagesPath" @properties
        if ($variant.ContainsKey('BuildError')) {
            $buildOutput = & dotnet build $implicitConsumer --no-restore -c $Configuration --artifacts-path $artifacts @properties 2>&1 |
                Out-String
            if ($LASTEXITCODE -eq 0 -or $buildOutput -notmatch $variant.BuildError) {
                Write-Host $buildOutput
                throw "Variant '$($variant.Name)': expected the build to fail with '$($variant.BuildError)' (exit code $LASTEXITCODE)"
            }
            if ($variant.ContainsKey('BuildErrorNot') -and $buildOutput -match $variant.BuildErrorNot) {
                Write-Host $buildOutput
                throw "Variant '$($variant.Name)': the build output must not match '$($variant.BuildErrorNot)'"
            }
            continue
        }
        Invoke-Dotnet build $implicitConsumer --no-restore -c $Configuration --artifacts-path $artifacts @properties

        $globalUsings = Get-ChildItem -Path $artifacts -Recurse -Filter '*.GlobalUsings.g.cs' | Select-Object -First 1
        $hasGlobalUsing = [bool] ($globalUsings -and
            (Select-String -Path $globalUsings.FullName -SimpleMatch 'global using IronMapper.Generated;' -Quiet))
        if ($hasGlobalUsing -ne $variant.GlobalUsing) {
            throw "Variant '$($variant.Name)': global using IronMapper.Generated expected $($variant.GlobalUsing), found $hasGlobalUsing"
        }

        $outputDir = Join-Path $artifacts 'bin'
        if ($variant.ContainsKey('Publish')) {
            Invoke-Dotnet publish $implicitConsumer --no-restore --no-build -c $Configuration --artifacts-path $artifacts @properties
            $outputDir = Join-Path $artifacts 'publish'
        }
        $dll = Get-ChildItem -Path $outputDir -Recurse -Filter 'IronMapper.PackageConsumer.ImplicitUsings.dll' |
            Select-Object -First 1
        if (-not $dll) { throw "Variant '$($variant.Name)': no built assembly under $outputDir" }
        if ($variant.ContainsKey('Run') -and -not $variant.Run) { continue }

        $runtimeConfig = Join-Path $dll.DirectoryName 'IronMapper.PackageConsumer.ImplicitUsings.runtimeconfig.json'
        $configProperties = (Get-Content -Raw $runtimeConfig | ConvertFrom-Json).runtimeOptions.PSObject.Properties['configProperties']
        $switch = $configProperties ? $configProperties.Value.PSObject.Properties['IronMapper.Registration.IsSupported'] : $null
        $actualSwitch = $switch ? "$($switch.Value)".ToLowerInvariant() : $null
        $expectedSwitch = $variant.ContainsKey('Switch') ? $variant.Switch : $null
        if ($actualSwitch -ne $expectedSwitch) {
            throw "Variant '$($variant.Name)': IronMapper.Registration.IsSupported in runtimeconfig.json expected '$expectedSwitch', found '$actualSwitch'"
        }
        $output = & dotnet $dll.FullName
        if ($LASTEXITCODE -ne 0 -or -not ($output -match [regex]::Escape($variant.Output))) {
            throw "Variant '$($variant.Name)': expected exit code 0 and '$($variant.Output)', got $LASTEXITCODE and '$output'"
        }
    }
    Write-Host 'Implicit usings OK.'
}
finally {
    Remove-Item -Recurse -Force $packagesPath -ErrorAction SilentlyContinue
}

Write-Host "IronMapper $Version packages verified."

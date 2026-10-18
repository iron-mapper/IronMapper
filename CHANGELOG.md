# Changelog

## [Unreleased]

### Fixed

- **`IronMapper.Generated.GeneratedMappers` is `internal` when all mappings of the project are internal.** It was
  always `public`, so a project with only internal mappings still exposed the class to every project that referenced
  it, and naming `GeneratedMappers` there reported `CS0436`. The accessibility is now computed over all mappings of
  the assembly and declared once, in `GeneratedMappers.g.cs`; it is `public` if at least one generated method is.
- A project that maps a type pair already exposed by a referenced project no longer fails with `CS0121` inside its
  own generated `MapToXList`/`MapToXArray` methods: they call the method of their own class.
- The class name stays `IronMapper.Generated.GeneratedMappers` in every assembly, so code that compiles on 1.1.2
  (`using IronMapper.Generated;` + `source.MapToX()`, and `GeneratedMappers.MapToX(source)` within one project) keeps
  working. See [Multi-project solutions](docs/configuration.md#multi-project-solutions) for the remaining limitations.

### Build

- `tests/IronMapper.PackageConsumer` references two libraries that also use the packed package
  (`IronMapper.PackageConsumer.InternalLib`, only internal mappings; `IronMapper.PackageConsumer.PublicLib`, public
  mappings) and builds with `TreatWarningsAsErrors`.

## [1.1.2] - 2026-10-18

### Fixed

- **Mappings of `internal` types no longer fail with CS0050/CS0051.** Generated methods (`MapToX`, the in-place
  overload, `MapToXList`, `MapToXArray`) were always `public`. They now get the lower effective accessibility of the
  source and destination types (containing types and generic type arguments included): `public` or `internal`.
  Applies to `[MapTo]`/`[MapFrom]` and `MappingProfile` mappings.
- Generated code uses fully qualified type names, so public types nested in other types and generic types
  (e.g. `Box<Payload>`) as source/destination now compile (previously CS0234/CS0305).
- `IMapper` (`IronMapper.Extensions.DI`) also finds `internal` generated methods.
- **Profile lambdas no longer fail with CS0103/CS1061 in the generated file.** Lambdas in `MapFrom`, `When`,
  `ConvertUsing`, `BeforeMap`/`AfterMap` and `AddTransformer` were copied as text, so static helpers of the profile,
  types and extension methods from the profile's `using` directives and unqualified static members (`Math.Round`)
  were not found. They are now rewritten through the semantic model: fully qualified names, extension methods as
  static calls, and parameters renamed by symbol (previously `s => s.s + "s"` also renamed the member and the
  string literal).

### Added

- Diagnostic `IM0010` (error): a mapping whose source or destination is (or is nested in, or has a type argument that is)
  a `private`/`protected`/`private protected` type is not generated, and the error names the inaccessible type.
  Other mappings are still generated.
- Diagnostic `IM0011` (error): a profile lambda uses a `private`/`protected` member or type; make it `internal`.
- Diagnostic `IM0012` (error): a profile lambda uses profile instance state, `this`, or a local variable/parameter
  of the constructor. In both cases only the affected mapping is skipped.

### Build

- `eng/verify-package.ps1` resolves a relative `-PackageDir` against the PowerShell location instead of the .NET
  process directory (previously, a console started in `C:\WINDOWS\system32` tried to write packages there).

## 1.1.1

### Fixed

- **The `IronMapper` package now ships the source generator.** 1.1.0 contained only `lib/`,
  so the generator never loaded in consuming projects and no `MapTo*` methods were generated.
  `IronMapper.Generator.dll` and `IronMapper.CodeFixes.dll` are now packed into
  `analyzers/dotnet/cs`; Roslyn (`Microsoft.CodeAnalysis.*`) is not a package dependency.
- `IronMapper.Extensions.DI` no longer excludes analyzers from its `IronMapper` dependency,
  so the generator also runs in projects that reference only the DI package.
- Package metadata, README and CI badge point to <https://github.com/iron-mapper/IronMapper>.

### Build

- CI packs both packages, verifies the `.nupkg` layout and builds a consumer project
  (`tests/IronMapper.PackageConsumer`) against the packed package from a local feed
  (`eng/verify-package.ps1`). The publish workflow runs the same check before pushing
  and requires the tag to match `<Version>` in `Directory.Build.props`.

# Changelog

## [Unreleased]

## [1.1.4] - 2026-10-20

The generated code and the public API are the same as in 1.1.3 (the generator only got pipeline tracking names, see
Build). This release updates the README shown on nuget.org.

### Fixed

- **The Quick Start in the README did not compile.** It was missing `using IronMapper.Generated;` (the namespace of the
  generated `MapTo*` methods), so `entity.MapToUserDto()` failed with `CS1061`. The README (also the nuget.org page) and
  Getting started now include the `using` and say that it is required in every file that calls `MapTo*` methods.

### Documentation

- README: minimum .NET SDK for the source generator (8.0.400+, Roslyn 4.11) and what happens on an older SDK
  (`CS9057`, the generator does not run); Known limitations list the property type conversions that currently fail or
  warn inside the generated file. `IMapper` is described as matching methods by signature (true since 1.1.3).
- README rewritten (also the NuGet package page): the Quick Start compiles as shown (see Fixed); "Why IronMapper"
  with measured facts; a performance table from `benchmarks/results/RESULTS.md`
  (IronMapper, Mapperly, Mapster, AutoMapper vs hand-written code, including the scenarios where IronMapper is slower);
  Known limitations with links to issues #22–#27 (flattening by convention and `null` input with nullable declarations
  added); Roadmap from the open issues; absolute links for nuget.org. Same structure in README.ru.md.
- NativeAOT is documented as verified for the generated extension methods and not supported for `IMapper`
  (previously "not verified"), with the measured warnings and sizes (`docs/advanced.md`, migration guide).
  `docs/advanced.md` (Performance) and the migration guide have numbers from the benchmarks instead of "no results
  published yet"; the migration guide lists flattening, nested single objects and constructor mapping as not
  supported and adds `using IronMapper.Generated;` to the extension-method step. `docs/diagnostics.md`: status table
  labeled 1.1.3.

### Build

- Comparison benchmarks (`benchmarks/`): the suite also targets `net8.0`; the runtime scenarios run on .NET 8 with
  `dotnet run -f net8.0` (output in `results/raw/bdn-net8.0/`, the .NET 10 results stay the reference). The 1.1.3
  results are frozen in `benchmarks/results/baseline-1.1.3/` as the reference for 1.2.0. The benchmark program exits
  with code 1 when a benchmark fails or a filter matches nothing.
- CI: the `benchmarks` job is now a smoke check of `benchmarks/` on every push and pull request (build against the
  local sources, equivalence tests, one iteration of every runtime scenario on .NET 10 and .NET 8, no published
  numbers). It replaces the run of the old `tests/IronMapper.Benchmarks` project, which is removed (IronMapper vs
  hand-written code only, superseded by `benchmarks/`). README and `docs/advanced.md` point to `benchmarks/` and its
  results instead.
- CI and publish workflows use the released .NET SDK (`dotnet-quality: 'preview'` removed).
- Snapshot tests of the generated code (Verify.SourceGenerators, `tests/IronMapper.Generator.Tests/Snapshots`) and
  incremental caching tests of the generator (`trackIncrementalGeneratorSteps`). Generator pipeline steps have
  tracking names (no behavior change).
- `.gitattributes`: CRLF for `*.cs`, `*.ps1`, `*.csproj`, `*.props`, `*.md`; LF for `*.sh` and Verify snapshots.

## [1.1.3] - 2026-10-18

### Fixed

- **A type pair defined twice no longer breaks the whole project.** `[MapTo]` + `CreateMap`, `[MapTo]` + `[MapFrom]`,
  two profiles, or `CreateMap` + another map's `ReverseMap()` for the same pair made the generator fail with `CS8785`
  (duplicate hint name), and **no mapping code was generated for the project**. The pair is now generated once and
  `IM0007` is reported (until now it was declared but never reported). Precedence: `CreateMap`, then `ReverseMap()`,
  then `[MapTo]`, then `[MapFrom]`; within one kind, the profile (or type) whose full name sorts first.
- One source type mapped to several destination types with the same simple name (other namespaces, nested in
  different types, other type arguments) failed with `CS0111`. These mappings now get qualified method names
  (`MapToAdminUserDto`, `MapToOrdersDto`, `MapToBoxOfInt32`) and `IM0013` gives the name to call; if no unique name
  exists, `IM0014` and only that group is skipped. Method names that compiled on 1.1.2 do not change.
- Hint names are built from the full names of both types: same simple names in different namespaces or outer types no
  longer collide (`CS8785`).
- Hook and transformer helpers of mappings with the same simple type names (e.g. `Shop.Item → Shop.ItemDto` and
  `Stock.Item → Stock.ItemDto` with `AddTransformer`) no longer fail with `CS0111`: they use the full names then.
- Converter types in `ConvertUsing<T>()`, `UseConverter<T>()` and `[MapConverter]` are emitted with fully qualified
  names, so nested converter types compile (previously `CS0234`).
- Nested collection properties call the element mapping by its actual (possibly qualified) name.
- `IMapper` (`IronMapper.Extensions.DI`) finds generated methods by signature instead of `MapTo{TDest.Name}`, so
  qualified names work through `IMapper` too.
- **`IronMapper.Generated.GeneratedMappers` is `internal` when all mappings of the project are internal.** It was
  always `public`, so a project with only internal mappings still exposed the class to every project that referenced
  it, and naming `GeneratedMappers` there reported `CS0436`. The accessibility is now computed over all generated
  mappings of the assembly and declared once, in `GeneratedMappers.g.cs`; it is `public` if at least one generated method is.
- A project that maps a type pair already exposed by a referenced project no longer fails with `CS0121` inside its
  own generated `MapToXList`/`MapToXArray` methods: they call the method of their own class by its simple (possibly
  qualified) name.
- The class name stays `IronMapper.Generated.GeneratedMappers` in every assembly, so code that compiles on 1.1.2
  (`using IronMapper.Generated;` + `source.MapToX()`, and `GeneratedMappers.MapToX(source)` within one project) keeps
  working. See [Multi-project solutions](docs/configuration.md#multi-project-solutions) for the remaining limitations.

### Added

- Diagnostic `IM0013` (warning): a mapping method name was qualified because of a collision; the message has the name
  to call.
- Diagnostic `IM0014` (error): a method name collision cannot be resolved; the colliding mappings are not generated.

### Build

- `tests/IronMapper.PackageConsumer` references two libraries that also use the packed package
  (`IronMapper.PackageConsumer.InternalLib`, only internal mappings; `IronMapper.PackageConsumer.PublicLib`, public
  mappings, including a method name collision with qualified names called from the consumer) and builds with
  `TreatWarningsAsErrors`.

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

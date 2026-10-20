# Changelog

## [Unreleased]

### Added

- **`global using IronMapper.Generated;` from the package.** C# projects with implicit usings
  (`<ImplicitUsings>enable</ImplicitUsings>`, the default in the .NET 6+ templates) no longer need
  `using IronMapper.Generated;` to call the generated `MapTo*` methods; a forgotten using failed with `CS1061` and no
  hint (the README Quick Start of 1.1.3 had this bug). Added by `buildTransitive/IronMapper.props`, so it also
  applies when the package comes through `IronMapper.Extensions.DI`. Turn it off with
  `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`. Projects without implicit usings (and C# 9 and
  earlier) are not affected and still need the explicit using; an explicit using next to the global one compiles
  without warnings. See [Global using](docs/configuration.md#global-using-of-ironmappergenerated).

### Changed

- With implicit usings every file imports `IronMapper.Generated`. A file that imports another extension method with
  the same name and parameter type as a generated one (e.g. a hand-written `MapToUserDto`) now gets `CS0121` at
  that call; remove or rename the method, or set `IronMapperImplicitUsings` to `false`.
- A project that references the package but has no generated mappings gets an empty `IronMapper.Generated`
  namespace (file `GeneratedMappers.g.cs`), so the global using compiles there (otherwise `CS0246`). Generated code
  of projects with mappings does not change.
- **Publishing an app that uses `IMapper` with trimming or NativeAOT now warns at your call (expected).**
  `AddIronMapper` (both overloads) and `RuntimeMapper` are marked `[RequiresUnreferencedCode]`, so the publish reports
  `IL2026` on the `AddIronMapper` line (and when `RuntimeMapper` is created directly) with what to use instead: the
  generated `MapTo*` extension methods. Until now it reported `IL2026`/`IL2062`/`IL2072`/`IL2075` inside
  `IronMapper.Extensions.DI` (by default one summary `IL2104`), and the app failed with `MappingException` at run
  time. `IMapper` still does not work in NativeAOT ([#26](https://github.com/iron-mapper/IronMapper/issues/26)); the
  warning goes away when it gets a reflection-free registry. Apps with `TreatWarningsAsErrors` that publish trimmed
  with `IMapper` fail at this warning instead of at run time.
- `MappingValidator` is marked `[RequiresUnreferencedCode]` (it reads properties and constructors through
  reflection): calling it in a trimmed or NativeAOT app reports `IL2026`. On .NET 8 and other targets that get the
  `netstandard` assemblies, the attributes are internal copies with the same full names, which the trimmer recognizes.
- **Shape of the generated code: no LINQ, no delegates.** Public signatures, the `null` semantics and the exceptions
  are the same (pinned by new tests before the change).
  - `MapToXList`/`MapToXArray` still take `IEnumerable<T>?`, but check the runtime type first: `T[]` and `List<T>` are
    read with `for` into a result of the exact size, another `ICollection<T>` gives the size through `Count`, any
    other sequence is read with `foreach` (an array result through a `List<T>` and `ToArray`). Elements are mapped by
    a direct call. A `null` source still gives an empty list / `Array.Empty`.
  - A nested collection property is mapped by a private helper `MapCollection_{Property}{scope}` instead of
    `Enumerable.ToList(Enumerable.Select(...))` / `ToArray`. The read loop depends on the declared source type (`for`
    over the indexer for `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`; `foreach` with a known count for
    `ICollection<T>`, `IReadOnlyCollection<T>` and types with a public `Count`; `foreach` otherwise). A `null` source
    collection is still `null`. An `IEnumerable<T>` destination stays lazy, as in 1.1 (a generated iterator).
  - Null checks of the generated methods use `ArgumentNullException.ThrowIfNull` when the project's target has it
    (.NET 6+); otherwise one private throw helper in `GeneratedMappers.g.cs` (with `[DoesNotReturn]` when available).
    The exception and its `ParamName` are the same.
- **`IMapper.MapCollection` returns a materialized `List<TDest>`** (typed as `IEnumerable<TDest>`, the signature is
  the same). The elements are mapped during the call: a `null` source throws `ArgumentNullException` and a `null`
  element or a missing mapping throws at the call, not when the result is enumerated; enumerating the result again
  does not map again. Up to 1.1.4 it was a lazy sequence that mapped on every enumeration.

### Fixed

- **Diagnostics are updated after an edit in the IDE.** An edit that changed only the diagnostics of a mapping (for
  example `[Ignore]` added to the destination property reported in `IM0001`, or one of two `private` members used in a
  profile lambda made `internal`, `IM0011`) kept the old diagnostics until the generator was restarted: the mapping
  model ignored its diagnostics when compared with the previous run. Command-line builds were not affected. The
  generated code does not change.

### Performance

Preliminary numbers: the comparison benchmarks with the local sources (`-p:UseLocalIronMapper=true`) against the
published 1.1.3 in the same session, .NET 10.0.12, Intel Core i7-12650H, 3 launches; ratio to hand-written code
(`Manual`). The published report is updated by the full run of the release.

| Scenario | 1.1.3 | Unreleased | Mapperly |
|---|---:|---:|---:|
| Nested | ×1.19 | ×1.09 | ×0.99 |
| Collection, 10 orders | ×1.20 | ×0.97 | ×1.18 |
| Collection, 1 000 orders | ×1.00 | ×1.01 | ×1.05 |
| In-place update | ×1.08 | ×0.90 | ×1.01 |
| NativeAOT executable, Δ vs an app without a mapper | +255.5 KB | +2.6 KB | +23.6 KB |

- Collections allocate the same as `Manual` (1.1.3: +6 %, the `Select` iterator and list growth).
- .NET 8: Nested ×1.26 → ×1.06, Collection of 10 ×1.14 → ×1.05, Collection of 1 000 ×1.11 → ×1.03.
- The remaining Nested gap is the generated null checks: they keep the JIT from inlining `MapToAddressDto` into
  `MapToCustomerDto` (checked with `DisassemblyDiagnoser`; without the checks the same benchmark gives ×1.02). The
  checks stay: they are the documented behavior.

### Build

- Generator pipeline data is compared by value (`EquatableArray<T>` for every collection, diagnostics included). The
  result of each analysis step was an `ImmutableArray` (compared by reference), so it counted as modified on every
  edit; now an equal result is `Unchanged`. The strict incremental caching tests are enabled (no `Skip`); new tests
  check that diagnostics appear and disappear after an edit.
- `IronMapper` and `IronMapper.Extensions.DI` (net10.0) are built with `IsAotCompatible`: the trim and AOT analyzers
  fail the build on an unannotated reflection use inside the libraries.
- CI: the `aot` job (`eng/verify-aot.ps1`, ubuntu, net8.0 and net10.0) publishes two probes with `PublishAot` against
  the packed packages: `tests/IronMapper.AotProbe` (attribute and profile mappings, collections, in-place update, a
  converter) must have no trim/AOT warnings and its executable must check the mapped values;
  `tests/IronMapper.AotProbe.DI` must report `IL2026` on its `AddIronMapper` line and nothing else.
- `eng/verify-package.ps1` builds `tests/IronMapper.PackageConsumer.ImplicitUsings` in eleven variants against
  the packed package: no using with implicit usings, only `IronMapper.Extensions.DI` referenced, no mappings, the
  Quick Start extracted from `README.md` and `README.ru.md` (between `<!-- quickstart:begin -->` and
  `<!-- quickstart:end -->` comments), an explicit using next to the global one, implicit usings
  off, `LangVersion` 9, `IronMapperImplicitUsings=false` (each also checks whether the global using was generated),
  and netstandard2.0 / netstandard2.1 libraries (the generated throw helper instead of `ThrowIfNull`, with and
  without `[DoesNotReturn]`; built only). The executable variants are also run.
- Tests: `tests/IronMapper.Tests/CollectionShapeTests.cs` maps every pair of `List<T>`, `T[]`, `IEnumerable<T>`,
  `ICollection<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `IList<T>` as a nested property (new object and
  in place: values, order, `null`, empty, `null` element) and `MapToXList`/`MapToXArray` over several runtime source
  types; a new snapshot scenario `CollectionShapes`; snapshot tests also fail on compiler warnings in the generated
  files.
  `tests/IronMapper.PackageConsumer` calls its own and a referenced library's mappings from a file without the using.
- CI runs on push only for `main`; other branches are checked through their pull requests (all jobs run there).

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

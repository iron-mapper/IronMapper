# Changelog

## [Unreleased]

### Added

- **Optional overloads can be turned off.** For the project with the MSBuild properties `IronMapperGenerateInPlace`,
  `IronMapperGenerateList`, `IronMapperGenerateArray` (`true` by default), for one mapping with `GenerateInPlace`,
  `GenerateList`, `GenerateArray` on `[MapTo]`/`[MapFrom]` or `.GenerateMethods(inPlace, list, array)` in a profile
  (overrides the project value; not applied to the reverse mapping of `ReverseMap()`). `MapToX(source)` is always
  generated. `IMapper` keeps mapping a single object, `List<T>`, `T[]`, `MapCollection` and in-place for such a mapping,
  with the same results: without `MapToXList`/`MapToXArray` the registry fills the collection itself; without the public
  in-place overload its body stays as a private method for `IMapper` (so turning in-place off removes the public method,
  not the code). Names of the remaining methods and the collision rules do not change. See
  [Generated overloads and code coverage](docs/configuration.md#generated-overloads-and-code-coverage).
- **`[GeneratedCode("IronMapper", "<version>")]` on the generated class**, and with
  `<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>` also `[ExcludeFromCodeCoverage]`: unused
  generated methods (e.g. `MapToXList` that nothing calls) no longer lower the coverage of a project. Declared once on
  `GeneratedMappers`, so they cover the mapping methods, the registration for `IMapper` and the converter cache.
- Diagnostic `IM0016` (warning): an argument of `GenerateMethods(...)` is not a constant; the project value applies for
  that overload.
- **Diagnostics point to your code.** Every `IMxxxx` diagnostic has a source location: the destination property or the
  `[MapTo]`/`[MapFrom]` attribute (IM0001), the `[MapProperty]` attribute (IM0006), the `CreateMap` or `ReverseMap()`
  call, the profile name (IM0005), the member in a profile lambda (IM0011/IM0012), the converter type (IM0015), the
  duplicate definition (IM0007, with the other definitions as additional locations); the full list is in
  [diagnostics](docs/diagnostics.md). The IDE underlines the place and the build output shows file and line. Up to
  1.1.4 all diagnostics were reported without a location: only listed in the Error List, the per-file `.editorconfig`
  severities (`dotnet_diagnostic.IMxxxx.severity`) did not apply to them, and the code fixes were never offered.
- **IM0001 for profile mappings, opt-in** ([#22](https://github.com/iron-mapper/IronMapper/issues/22)): with
  `<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` a destination property of a
  `CreateMap` mapping that nothing maps is reported at the `CreateMap` call. Mapped means `ForMember` (`MapFrom`,
  `Ignore`, `UseConverter`), `IncludeMembers`, a source property with the same name, or `[Ignore]` on the destination
  property; a mapping with `ConvertUsing` and the reverse mapping of `ReverseMap()` are not checked (as in AutoMapper,
  where reverse maps use `MemberList.None`). Off by default in 1.x (planned on by default in 2.0). The severity is set
  the standard way, e.g. `dotnet_diagnostic.IM0001.severity = error` in `.editorconfig`. See
  [Unmapped properties in profiles](docs/configuration.md#unmapped-properties-in-profiles).
- **Code fixes work.** IM0001: "Add [Ignore] to '...'" on the destination property (adds `using IronMapper.Attributes;`
  when needed, `[property: Ignore]` on a positional record parameter) and, new, "Ignore '...' in this mapping
  (ForMember)" for profile mappings; IM0006: "Did you mean '...'?" with the destination properties of the mapped type.
  Fix All is supported. Both used to find their target by searching the message text in the document (the first
  property with that name, a type found by simple name); they now use the diagnostic's location and properties.
- **`global using IronMapper.Generated;` from the package.** C# projects with implicit usings
  (`<ImplicitUsings>enable</ImplicitUsings>`, the default in the .NET 6+ templates) no longer need
  `using IronMapper.Generated;` to call the generated `MapTo*` methods; a forgotten using failed with `CS1061` and no
  hint (the README Quick Start of 1.1.3 had this bug). Added by `buildTransitive/IronMapper.props`, so it also
  applies when the package comes through `IronMapper.Extensions.DI`. Turn it off with
  `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`. Projects without implicit usings (and C# 9 and
  earlier) are not affected and still need the explicit using; an explicit using next to the global one compiles
  without warnings. See [Global using](docs/configuration.md#global-using-of-ironmappergenerated).
- **`IMapper` maps collections by the destination type** ([#8](https://github.com/iron-mapper/IronMapper/issues/8)):
  `mapper.Map<List<UserDto>>(users)`, `Map<UserDto[]>`, `Map<IEnumerable<UserDto>>`, `Map<IList<UserDto>>`,
  `Map<ICollection<UserDto>>`, `Map<IReadOnlyList<UserDto>>`, `Map<IReadOnlyCollection<UserDto>>` (and the same with
  two type arguments, e.g. `Map<List<User>, List<UserDto>>`) work for any `IEnumerable<User>` source (`List`, array,
  `HashSet`, …) when `User → UserDto` is mapped. They call the generated `MapToUserDtoList`/`MapToUserDtoArray`: the
  result is a `List<UserDto>`, an array for `T[]`. Up to 1.1.4 these calls threw `MappingException`.
- `IMapper.Map<TDest>(object)` and `Map<TSource, TDest>` fall back to the mapping of the nearest base type of the
  source (a `DerivedEntity` without its own mapping uses the one of `Entity`); up to 1.1.4 only the exact type matched.
  `Map<TSource, TDest>` with a source declared as a base type or `object` uses the runtime type.
- `RuntimeMapper.Map(object source, Type sourceType, Type destinationType)` maps by `Type`. It is not on the `IMapper`
  interface (a new interface member would break other implementations in 1.x).
- Namespace `IronMapper.Registration` in the `IronMapper` package: `MapperRegistry` (the registry of the generated
  mappings, mapping by `Type`, `EnsureModuleInitialized(assembly)`) and `MapperCache<TSource, TDest>` (the generated
  delegates of one pair). Used by `IMapper`; can be used without the DI package.
- **MSBuild property `IronMapperRegistration`** and feature switch `IronMapper.Registration.IsSupported`
  (`MapperRegistry.IsSupported`): whether the generated registration for `IMapper` runs and stays in a trimmed app.
  `buildTransitive/IronMapper.targets` turns it off for a `PublishAot` / `PublishTrimmed` app in which no project
  references `IronMapper.Extensions.DI` (the DI package marks itself through its own `buildTransitive` props, so a
  reference through a library counts); `true` / `false` in the application project overrides it. The trimmer (ILLink
  and NativeAOT, through `ILLink.Substitutions.xml` in `IronMapper.dll`) then removes the registration, the registry and
  the generated methods the app does not call. With the switch off, creating `RuntimeMapper` throws
  `InvalidOperationException` that names the property, and `MapperRegistry`'s `MappingException` says the same. See
  [Registration for IMapper](docs/configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps).
- Diagnostic `IM0015` (error): a converter of `[MapConverter]`, `UseConverter<T>()` or `ConvertUsing<T>()` cannot be
  created by the generated code (no parameterless constructor, a `private`/`protected` one, an abstract class, an
  interface, a static class, an open generic type, a `private` nested type). The mapping is not generated; other
  mappings are. Up to 1.1.4 the build failed with `CS7036`, `CS1729`, `CS0122` or `CS0144` inside the generated file.
  See [IM0015](docs/diagnostics.md#im0015--converter-cannot-be-created-by-generated-code).
- Diagnostic `IM0003` (error) is reported: `[MapTo]`/`[MapFrom]` with a type that compiles but cannot be mapped, an
  open generic type (`typeof(Box<>)`) or an array. Up to 1.1.4 such a mapping was dropped without any message. Its
  title and message changed (it was declared, never reported). A type that does not resolve is not reported by
  `IM0003` (see Fixed).

### Changed

- `MapperRegistry.Register<TSource, TDest>`: `mapList` and `mapArray` may be `null` (the generated registration passes
  `null` for a turned-off `MapToXList`/`MapToXArray`); the registry then maps the elements with `map`.
- With implicit usings every file imports `IronMapper.Generated`. A file that imports another extension method with
  the same name and parameter type as a generated one (e.g. a hand-written `MapToUserDto`) now gets `CS0121` at
  that call; remove or rename the method, or set `IronMapperImplicitUsings` to `false`.
- A project that references the package but has no generated mappings gets an empty `IronMapper.Generated`
  namespace (file `GeneratedMappers.g.cs`), so the global using compiles there (otherwise `CS0246`). Generated code
  of projects with mappings does not change.
- **`IMapper` works without reflection, with trimming and NativeAOT** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
  For every assembly with mappings the generator emits `GeneratedMappers.Registration.g.cs`: a module initializer
  (`[ModuleInitializer] RegisterIronMapperMappings()`) that hands the assembly's registration to
  `IronMapper.Registration.MapperRegistry`, and the registration itself, one `Register<TSource, TDest>` call per
  mapping with lambdas that call the generated methods. The mappings are registered when the first `IMapper` is
  created (an assembly first used later: at the next mapping). The module initializer only puts one delegate per
  assembly on a lock-free list and does not initialize the registry, so an app that uses only the extension methods
  registers nothing (it loads `IronMapper.dll` for the module initializer), and a trimmed or NativeAOT app without the
  DI package does not contain the registration (see `IronMapperRegistration` above). `RuntimeMapper` calls the delegates: `Map<TSource, TDest>` is a static field read and
  a delegate call. Publishing with trimming or NativeAOT reports no warnings (`AddIronMapper` and `RuntimeMapper` are
  not annotated; the `IMapper` probe runs in CI). Up to 1.1.4 `RuntimeMapper` scanned the loaded assemblies for
  `GeneratedMappers`, called the methods through `MethodInfo.Invoke`, and the NativeAOT app failed with
  `MappingException`.
- **Exceptions thrown through `IMapper` are no longer wrapped:** an `ArgumentNullException` of the generated code (a
  `null` nested element), an exception of your converter or hook reach the caller as they are, with the stack of the
  generated method. Up to 1.1.4 they came as `TargetInvocationException` with the original as `InnerException`; code
  that catches `TargetInvocationException` around `IMapper` must catch the original exception instead.
- **`AddIronMapper` does not scan assemblies and does not add profiles to the container.** For every assembly passed
  (also `AddProfile<T>()` and `AddProfilesFromAssembly`) it runs the assembly's module initializer
  (`RuntimeHelpers.RunModuleConstructor`), so the mappings of an assembly that nothing has used yet are found; they
  are registered when the first `IMapper` is created. Profiles are read by the source generator; up to 1.1.4 `AddIronMapper` called `GetTypes()` and added
  every `MappingProfile` as a singleton, so code that resolved a profile from the container gets nothing now.
  `AddProfilesFromAssembly(null)` throws `ArgumentNullException` (it used to fail later in `AddIronMapper`). When a
  mapping is not found, `IMapper` runs the module initializers of all loaded assemblies (names not starting with
  `System.` or `Microsoft.`) and looks again, as the old assembly scan did.
- **C# 8 projects** (`LangVersion` below 9) get the registration without `[ModuleInitializer]` (C# 8 does not have
  it): `IMapper` finds their mappings only after `IronMapper.Generated.GeneratedMappers.RegisterIronMapperMappings()`
  is called in that assembly. Projects on targets without `ModuleInitializerAttribute` (`netstandard`, .NET Framework)
  get an internal copy of the attribute in the generated file, unless the compilation already has one.
- The `MappingException` for a missing mapping says to pass the assembly with the mapping to `AddIronMapper`.
- `MappingValidator` is marked `[RequiresUnreferencedCode]` (it reads properties and constructors through
  reflection): calling it in a trimmed or NativeAOT app reports `IL2026`. On .NET 8 and other targets that get the
  `netstandard` assemblies, the attributes are internal copies with the same full names, which the trimmer recognizes.
- **Less generated code for collections.** The loops of `MapToXList`/`MapToXArray` are written once per project, in
  two private generic methods of `GeneratedMappers` (`MapElementsToList`, `MapElementsToArray`). Each mapping's
  `MapToXList`/`MapToXArray` is a one-line call of them with a private struct of the mapping that calls `MapToX`
  directly: a struct type argument makes the runtime compile a separate, specialized copy for each mapping, so there is
  no delegate or interface dispatch per element. Signatures, the `null` semantics, the fast paths for `T[]`, `List<T>`,
  `ICollection<T>` and the capacities are the same (the existing collection tests pass unchanged). Nested collection
  properties keep their per-property helpers.
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
- **A converter without state is created once and reused** (`[MapConverter]`, `UseConverter<T>()`,
  `ConvertUsing<T>()`). A converter is without state when it is a class with a public parameterless constructor, no
  instance fields anywhere in its hierarchy except `object` (auto-property backing fields and field-like events
  included), no finalizer, constructors without code, and every type of the hierarchy declared in the same project.
  Its single instance is created on first use, once per converter type for the assembly (`ConverterCache<T>` in
  `GeneratedMappers.g.cs`, initialized once by the runtime even when several threads race). Every other converter is
  still created with `new` on every call. The difference can only be observed through state kept outside the converter
  (for example a static field that records instances). See
  [Converter instances](docs/configuration.md#converter-instances).

### Fixed

- **A type that does not resolve no longer breaks the generated code.** `typeof(Missing)` in `[MapTo]`/`[MapFrom]`
  (also an inaccessible type, an unknown namespace or type argument), `CreateMap<Source, Missing>()` and an unresolved
  converter type: the compiler reports the error at the user's code (`CS0246` and similar), and the generator now skips
  that mapping. Up to 1.1.4 it still emitted the mapping, which added `CS0400`/`CS0246`/`CS0122` errors inside the
  generated file. Other mappings are generated. A profile whose only `CreateMap` is skipped this way does not get
  `IM0005` (empty profile).
- **Diagnostics are updated after an edit in the IDE.** An edit that changed only the diagnostics of a mapping (for
  example `[Ignore]` added to the destination property reported in `IM0001`, or one of two `private` members used in a
  profile lambda made `internal`, `IM0011`) kept the old diagnostics until the generator was restarted: the mapping
  model ignored its diagnostics when compared with the previous run. Command-line builds were not affected. The
  generated code does not change.

### Performance

The numbers come with the release, from the full run of the comparison benchmarks.

Converters (see Changed): a converter without state is no longer allocated on every call; its effect on time is
measured by the full run.

Generated code without LINQ and delegates (see Changed):

- Collections of a known size are filled in a loop with the final capacity: no `Select` iterator and no list growth,
  so mapping a collection allocates the same as hand-written code (1.1.3 allocated more).
- Nested objects, short collections and the in-place update are closer to hand-written code than in 1.1.3.
- The NativeAOT executable of an app that uses only the generated extension methods is barely larger than the same
  app without a mapper: `Enumerable.Select` + `ToList` no longer pull in the NativeAOT type loader.
- The remaining Nested gap to hand-written code is the generated null checks: they keep the JIT from inlining
  `MapToAddressDto` into `MapToCustomerDto` (checked with `DisassemblyDiagnoser`; without the checks the gap almost
  disappears). The checks stay: they are the documented behavior.

`IMapper` on the generated registry:

- No reflection per call and no allocation of `IMapper` itself (1.1.3: `MethodInfo.Invoke` with an argument array).
  `Map<TSource, TDest>` reads a static field and calls a delegate; what remains over the extension method is the call
  of the generic interface method `IMapper.Map<TSource, TDest>` (checked with `DisassemblyDiagnoser`: called on
  `RuntimeMapper` directly, the call costs the same as the extension method).
- The registry is keyed by `Type` (nested dictionaries) instead of a `(Type, Type)` tuple and never clears a concurrent
  dictionary: the tuple key made the JIT compile the dictionary code at startup, and `Clear()` started the event
  source of the concurrent collections.
- An app with only the extension methods: the module initializer puts one delegate per assembly on a list (no
  registry, no dictionaries); in a trimmed or NativeAOT app without the DI package the registration is removed.

### Build

- Tests of the optional overloads: every combination of the three, set by MSBuild property, attribute and profile, run
  through the generated method and the registry (`GeneratedMethodsTests`); `IMapper` with the overloads turned off gives
  the same results as with all of them (`TurnedOffOverloadsTests`). The tests that use the process-wide registry run in
  one xUnit collection (one of them turns the registration's feature switch off). `eng/verify-package.ps1` checks the
  overloads and the class attributes against the packed package, with the defaults and with everything turned off.
- `tests/IronMapper.CodeFixes.Tests` (in `IronMapper.slnx`): code fix tests with `Microsoft.CodeAnalysis.Testing`
  (`CSharpCodeFixTest` with the generator attached). `eng/verify-package.ps1` builds a profile with an unmapped
  property against the packed package: without the property it builds; with `IronMapperReportUnmappedInProfiles=true`
  the build fails with IM0001 at the `CreateMap` line; `.editorconfig` severities `error` and `none` apply.
- Diagnostics are reported by their own generator output, which resolves the locations in the current syntax trees (so
  `.editorconfig` severities and code fixes apply); the generated files are produced from pipeline data without
  diagnostics and locations, so an edit that only moves a mapping leaves every source output cached (test
  `LinesAddedAboveMappings_SourceOutputsCached_DiagnosticLocationsUpdated`).
- Generator pipeline data is compared by value (`EquatableArray<T>` for every collection, diagnostics included). The
  result of each analysis step was an `ImmutableArray` (compared by reference), so it counted as modified on every
  edit; now an equal result is `Unchanged`. The strict incremental caching tests are enabled (no `Skip`); new tests
  check that diagnostics appear and disappear after an edit.
- `IronMapper` and `IronMapper.Extensions.DI` (net10.0) are built with `IsAotCompatible`: the trim and AOT analyzers
  fail the build on an unannotated reflection use inside the libraries.
- CI: the `aot` job (`eng/verify-aot.ps1`, ubuntu, net8.0 and net10.0) publishes two probes with `PublishAot` against
  the packed packages: `tests/IronMapper.AotProbe` (attribute and profile mappings, collections, in-place update, a
  converter) must have no trim/AOT warnings and its executable must check the mapped values;
  `tests/IronMapper.AotProbe.DI` (`IMapper`: `AddIronMapper`, mapping by declared and runtime type, in-place,
  `MapCollection`, `Map<List<T>>`/`Map<T[]>`/`Map<IEnumerable<T>>`, a profile mapping with a converter, the original
  exception) must have no trim/AOT warnings either, and its executable checks the values. The ILC map file shows that
  the generated registration is absent from `AotProbe` and present with `IronMapperRegistration=true`;
  `AotProbe.DI` with `IronMapperRegistration=false` must fail with the message that names the property; and
  `tests/IronMapper.AotProbe.Transitive` (the DI package only through `IronMapper.AotProbe.Transitive.Services`, the
  mappings in `IronMapper.AotProbe.Transitive.Maps` without it) must run with the JIT and with NativeAOT. `AotProbe` and
  `AotProbe.DI` are also published trimmed without NativeAOT (`PublishTrimmed`, self-contained; ILLink): no warnings,
  they run, and the trimmed probe assembly has no registration in the first and keeps it in the second.
- `eng/verify-package.ps1` builds `tests/IronMapper.PackageConsumer.ImplicitUsings` in sixteen variants against
  the packed package: no using with implicit usings, only `IronMapper.Extensions.DI` referenced, no mappings, the
  Quick Start extracted from `README.md` and `README.ru.md` (between `<!-- quickstart:begin -->` and
  `<!-- quickstart:end -->` comments), an explicit using next to the global one, implicit usings
  off, `LangVersion` 9, `IronMapperImplicitUsings=false` (each also checks whether the global using was generated),
  and netstandard2.0 / netstandard2.1 libraries (the generated throw helper instead of `ThrowIfNull`, with and
  without `[DoesNotReturn]`; the generated copy of `ModuleInitializerAttribute`; C# 8 without a module initializer;
  built only), and the feature switch of the `IMapper` registration in `runtimeconfig.json` (not set by default and
  with the DI package, `false` with `PublishTrimmed` and without it, `true` with `IronMapperRegistration=true`; a
  framework-dependent publish without the DI package finds the mapping in the registry). The executable variants are
  also run. The package layout check requires `buildTransitive/IronMapper.targets` and
  `buildTransitive/IronMapper.Extensions.DI.props`.
- Tests of `IMapper` on the registry (`tests/IronMapper.Integration.Tests/MapperRegistryTests.cs`): original
  exceptions, collections as the destination type, types with the same simple names in different namespaces, base
  and runtime types, `Map(object, Type, Type)`, and mappings in an assembly that nothing has used yet (profiles in
  `tests/IronMapper.Integration.Application`, entities in `tests/IronMapper.Integration.Domain`, the call from the
  test project; run outside the parallel tests), a registration after a lookup replaces the mapping found through a
  base type, the error with the switch off, and the lazy registration: nothing is registered until a mapper is created,
  mappers created in parallel run a registration once and all see it, an assembly first used after the mapper was
  created is found. Generator tests: the module initializer registers the mappings (nothing
  with the switch off), C# 8, no registration without mappings; every snapshot scenario has a
  `GeneratedMappers.Registration.g.cs`.
- Benchmarks: `scripts/run-aot.ps1` and `run-startup.ps1 -Mode Aot` work with `UseLocalIronMapper=true`: the NativeAOT
  publish passes `StartupProbeAot=true` and the probes set `PublishAot` themselves (`probes/StartupProbe.props`); a
  global `PublishAot` reached the netstandard2.0 projects in `src/` and failed with `NETSDK1207`. With the local
  sources `Directory.Build.targets` imports `src/IronMapper/buildTransitive/IronMapper.targets`, so the AOT size is
  measured as with the package.
- Benchmarks: `Manual_DI` in `StartupBenchmark` and `probes/StartupProbe.Manual_DI`: the hand-written mapping resolved
  from the same Microsoft.Extensions.DependencyInjection container as `IronMapper_IMapper`, so the cold start of
  `IronMapper_IMapper` minus `Manual_DI` is what IronMapper adds on top of the container. The descriptions of
  `IronMapper_IMapper` in `benchmarks/README.md` and the feature matrix notes cover 1.1.x and 1.2.0.
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

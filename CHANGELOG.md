# Changelog

## [Unreleased]

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

### Added

- Diagnostic `IM0013` (warning): a mapping method name was qualified because of a collision; the message has the name
  to call.
- Diagnostic `IM0014` (error): a method name collision cannot be resolved; the colliding mappings are not generated.

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

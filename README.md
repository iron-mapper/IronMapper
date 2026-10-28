# IronMapper

A compile-time object mapper for .NET: a Roslyn source generator writes the mapping code as plain C# when you build.

Русская версия: [README.ru.md](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md)

**Upgrading from 1.1.x?** 2.0.0 has breaking changes (the global using, the name of the generated class, `IMapper`
exceptions and `AddIronMapper`, `MapCollection`): see
[Upgrading to 2.0](https://github.com/iron-mapper/IronMapper/blob/main/docs/upgrading-to-2.0.md).

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Downloads](https://img.shields.io/nuget/dt/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![CI](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/iron-mapper/IronMapper/blob/main/LICENSE)
[![Targets](https://img.shields.io/badge/targets-netstandard2.0%20%7C%20net10.0-512BD4.svg)](https://www.nuget.org/packages/IronMapper)

---

## Why IronMapper

- **Mapping code you can read and debug.** For every type pair the generator emits `MapTo*` extension methods with
  explicit property assignments, so the mapping can be read and stepped through in the IDE. The generated code
  uses no reflection.
- **Configuration errors at build time, partly.** For `[MapTo]`/`[MapFrom]` an unmatched destination property is
  warning `IM0001`, a wrong `[MapProperty]` name is error `IM0006`, an inaccessible type is `IM0010`
  ([diagnostics](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.md)). Profile mappings get
  fewer checks (`IM0001` for them is opt-in from 2.0.0, [#22](https://github.com/iron-mapper/IronMapper/issues/22)).
  Incompatible property types, and nested objects or collection elements without a mapping for their type pair, are
  error `IM0002` at your code, with a code fix that adds the missing mapping (from 2.1.0; up to 2.0.0 a C# error in the
  generated file). Nested objects with a mapping are mapped by it; a cycle through them is warning `IM0004`.
- **Flat objects are as fast as hand-written code:** 11.4 ns vs 11.5 ns per object (.NET 10). Nested objects are a
  little slower (×1.07 on .NET 10, ×1.09 on .NET 8; 1.1.3: ×1.25), see
  [Performance](https://github.com/iron-mapper/IronMapper#performance).
- **NativeAOT checked in CI** (.NET 8 and .NET 10): the generated methods and, from 2.0.0, the optional `IMapper`
  service publish with no trim/AOT warnings and run correctly. Up to 1.1.4 `IMapper` used reflection and did not work
  under NativeAOT ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
- **Cold start of about 1 ms** (process start plus the first mapping, over a console app without a mapper: +1.3 ms;
  hand-written code and Mapperly are within 0.2 ms of the app without a mapper, IronMapper loads its assembly for the
  generated module initializer). AutoMapper and Mapster need 70–100 ms there to build their mapping configuration
  ([results](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md#cold-start)).
- **No runtime dependencies.** The `IronMapper` package has no NuGet dependencies and adds one assembly to the app
  (+47.7 KB to a framework-dependent publish of the benchmark console, +2.6 KB to its NativeAOT executable). The
  optional `IronMapper.Extensions.DI` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Quick Start

```bash
dotnet add package IronMapper
```

`Program.cs` of a console app (`dotnet new console`):

<!-- quickstart:begin -->

```csharp
using IronMapper.Attributes;

var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();   // generated at build time
Console.WriteLine(dto.Name);           // Alice

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

<!-- quickstart:end -->

No `IMapper` and no service registration are needed.

The generated `MapTo*` methods live in the namespace `IronMapper.Generated`. **With implicit usings**
(`<ImplicitUsings>enable</ImplicitUsings>`, the default in the .NET 6+ templates) the package adds
`global using IronMapper.Generated;` for you. **Without implicit usings, add `using IronMapper.Generated;`** to every
file that calls `MapTo*` methods; otherwise the call fails with `CS1061` ("does not contain a definition for
'MapToUserDto'"). To turn the global using off, set `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`
([Configuration](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md#global-using-of-ironmappergenerated)).

**Requirements.** The generator needs the C# compiler from **.NET SDK 8.0.400 or later** (Roslyn 4.11; Visual
Studio 2022 17.11+). On an older SDK the compiler reports warning `CS9057`, the generator does not run, and every
`MapTo*` call fails with `CS1061` as well. The `IronMapper` package targets `netstandard2.0` and `net10.0`; see
[Platforms](https://github.com/iron-mapper/IronMapper#platforms).

---

## Performance

IronMapper 2.0.0 against hand-written code (`Manual`), Mapperly 4.3.1, Mapster 10.0.13 and AutoMapper 16.2.0.
BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11, Intel Core i7-12650H. Full report (also .NET 8), method and the
comparison with 1.1.3 measured in the same session:
[benchmarks/results/RESULTS.md](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md).

| Library | Flat | Nested | Collection, 1 000 | Cold start, Δ ms | NativeAOT | Δ publish (JIT) | Δ AOT executable |
|---|---:|---:|---:|---:|---|---:|---:|
| Manual | 11.5 ns | 166.5 ns | 257.5 µs | −0.2 | works | +4.6 KB | +2.0 KB |
| IronMapper | 11.4 ns (×0.99) | 177.8 ns (×1.07) | 271.4 µs (×1.05) | 1.3 | works | +47.7 KB | +2.6 KB |
| IronMapper `IMapper` | 16.3 ns (×1.41) | 185.5 ns (×1.11) | 253.0 µs (×0.98) | 19.9 | works | +223.4 KB | +1.50 MB |
| Mapperly | 11.6 ns (×1.01) | 163.1 ns (×0.98) | 276.9 µs (×1.08) | 0.1 | works | +38.5 KB | +23.6 KB |
| Mapster | 15.2 ns (×1.32) | 185.8 ns (×1.12) | 287.0 µs (×1.11) | 98.2 | fails | +244.8 KB | +3.99 MB |
| AutoMapper | 34.0 ns (×2.95) | 264.0 ns (×1.59) | 344.6 µs (×1.34) | 72.1 | fails | +1.17 MB | +5.16 MB |

Against 1.1.3 (same session, same machine, ratio to `Manual`, .NET 10): Nested ×1.25 → ×1.07, Collection of 10 orders
×1.15 → ×0.98 (Mapperly ×1.17), in-place update ×1.10 → ×0.91, collections allocate as much as `Manual` (1.1.3:
+4 %); `IMapper` Flat 30.2 → 16.3 ns with no allocation of its own, NativeAOT fails → works; NativeAOT executable
+255.5 KB → +2.6 KB.

How to read it:

- Flat: one object with 15 properties. Nested: an order with a customer, an address and 5–20 lines. Collection:
  `List<Order>` → `List<OrderDto>` of 1 000 nested orders. Times are means; ×N is the ratio to `Manual`.
- **Nested objects are configured by hand in IronMapper 2.0.0** (`ForMember(... MapFrom(s => s.Customer.MapToCustomerDto()))`),
  because 2.0.0 does not map nested single objects automatically. The other libraries map them automatically. From
  2.1.0 IronMapper maps them too, and the suite uses that; not measured yet.
- Cold start: median wall-clock time of process start plus one mapping, minus the same console app without a mapper
  (30 runs). This metric is sensitive to noise; read it as an order of magnitude. Most of the `IMapper` cold start is
  the DI container itself: the same hand-written mapping resolved from the same container (`Manual_DI`) gives +15.3 ms,
  so IronMapper's own part is 4.5 ms (1.1.3: 6.4 ms); it grows with the number of mappings (registration).
- NativeAOT: "works" means the published executable ran and printed the correct result; from 2.0.0 this includes
  `IMapper` (checked in CI; the 1.1.3 executable failed with `MappingException`). Mapster and AutoMapper build their
  mappers at run time and fail.
- Sizes are the increase over the same console app without a mapper.
- Collection differences of a few percent are within the run-to-run spread and do not mean one library is faster: at
  1 000 orders this run has `IMapper` (×0.98) faster than the extension methods (×1.05) it calls, and Mapster at ×1.11
  against ×1.03 in the 1.1.3 run of the same session; a separate run of IronMapper alone gave ×1.01. At 100 000 orders
  the garbage collector dominates (full report).
- .NET 8 (same run): Flat ×1.01, Nested ×1.09, Collection 1 000 ×1.02, in-place ×0.93; `IMapper` Flat ×1.37.
- One machine. Mapster.Tool (Mapster's code generator) was not measured.
- To reproduce it on your machine: [Run it yourself](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/README.md#run-it-yourself)
  (`./bench.ps1` in `benchmarks/`).

---

## Key features

All examples below assume `using IronMapper.Attributes;` (and `using IronMapper.Generated;` without implicit
usings). Full reference:
[Configuration](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md).

**Attributes on the source or the destination type**

```csharp
[MapTo(typeof(ProductDto))]        // on the source type
public class Product { /* ... */ }

[MapFrom(typeof(Order))]           // on the destination type, same result
public class OrderDto { /* ... */ }
```

**Rename, ignore, convert a property**

```csharp
[MapTo(typeof(InvoiceDto))]
public class Invoice
{
    [MapProperty("CustomerName")]              // goes to InvoiceDto.CustomerName
    public string Customer { get; set; } = "";

    [Ignore]                                   // not mapped
    public string InternalNote { get; set; } = "";

    [MapConverter(typeof(PriceConverter))]     // decimal → string through a converter
    public decimal Total { get; set; }
}

public class PriceConverter : ITypeConverter<decimal, string>   // IronMapper.Interfaces
{
    public string Convert(decimal source) => source.ToString("F2");
}
```

**Profiles (fluent API, similar to AutoMapper)** for types you cannot annotate. The generator reads the profile at
build time; it is not executed for mapping.

```csharp
public class OrderProfile : MappingProfile   // IronMapper.Configuration
{
    public OrderProfile()
    {
        CreateMap<Order, OrderSummary>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Street + ", " + s.City))
            .ForMember(d => d.InternalId, o => o.Ignore());
    }
}
```

Profiles also support `ReverseMap`, `BeforeMap`/`AfterMap`, `When`, `ConvertUsing`, `IncludeMembers` and
`AddTransformer`; see [Migrating from AutoMapper](https://github.com/iron-mapper/IronMapper/blob/main/docs/migration-from-automapper.md).

**Collections and in-place updates**

```csharp
List<UserDto> list  = users.MapToUserDtoList();    // from any IEnumerable<UserEntity>
UserDto[]     array = users.MapToUserDtoArray();
entity.MapToUserDto(existingDto);                  // updates an existing object
```

`List<T>` properties (`List<Comment>` → `List<CommentDto>`) are mapped automatically when a mapping exists for the
element types.

**Code coverage and unused overloads.** From 2.0.0 the generated class is marked `[GeneratedCode]`, and
`<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>` adds `[ExcludeFromCodeCoverage]`, so unused
generated methods (e.g. `MapToXList` that nothing calls) no longer lower the coverage of your project. A coverage tool
that excludes `[GeneratedCode]` (coverlet with `ExcludeByAttribute=GeneratedCodeAttribute`) leaves the generated code out
of the report; to measure it, set `<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>`. Overloads you do
not use can be turned off for the project (`IronMapperGenerateInPlace`, `IronMapperGenerateList`,
`IronMapperGenerateArray`) or for one mapping (`GenerateList = false` on `[MapTo]`, `.GenerateMethods(list: false)` in a
profile); `IMapper` keeps mapping lists, arrays and in-place
([details](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md#generated-overloads-and-code-coverage)).

**Dependency injection (optional)**

```bash
dotnet add package IronMapper.Extensions.DI
```

```csharp
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;

builder.Services.AddIronMapper(typeof(Program).Assembly);   // registers IMapper and this assembly's mappings

app.MapGet("/users/{id}", (int id, IMapper mapper) => mapper.Map<UserDto>(LoadUser(id)));
app.MapGet("/users", (IMapper mapper) => mapper.Map<List<UserDto>>(LoadUsers()));
```

From 2.0.0 `IMapper` calls the generated methods through delegates that the generator registers for every mapping
(no reflection): it works with trimming and NativeAOT, and exceptions of your converters and hooks reach you as they
are. `Map<List<UserDto>>`, `Map<UserDto[]>` and `Map<IEnumerable<UserDto>>` map collections. Pass every assembly
that holds mappings to `AddIronMapper`; the mappings are registered when the first `IMapper` is created. An app that
does not use `IMapper` only loads the IronMapper assembly for the generated module initializer, and a trimmed or
NativeAOT app without `IronMapper.Extensions.DI` does not contain the registration at all
([details](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps)).
A call through `IMapper` still costs more than the extension method (the call of a generic interface method and a
delegate, and a type lookup for `Map<TDest>(object)`); prefer the extension methods when the types are known at
compile time (DI overhead benchmark, flat object: 15.4 ns through `IMapper` against 11.1 ns for the extension method;
AutoMapper's `IMapper` from the same container: 34.5 ns). Up to 1.1.4
`IMapper` used reflection, was ×2.7 slower on Flat and did not work under NativeAOT.

---

## Known limitations

- **Destinations created by a constructor or a factory** (positional records, classes without a parameterless
  constructor, `ConstructWith(D.Restore)` in a profile; from 2.1.0,
  [#24](https://github.com/iron-mapper/IronMapper/issues/24)) get no in-place overload: updating only the other
  properties would silently keep the old values of what only the constructor takes. See
  [Constructors and positional records](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md#constructors-and-positional-records)
  and [Factories](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md#factories-constructwith).
- **No flattening by convention** ([#25](https://github.com/iron-mapper/IronMapper/issues/25)): `CustomerAddressCity` is not filled from `Customer.Address.City`. Use
  `MapFrom`.
- **`IM0001` is off by default for profile mappings** ([#22](https://github.com/iron-mapper/IronMapper/issues/22)): an unmatched destination property in a `CreateMap`
  mapping keeps its default value without a warning. From 2.0.0 turn it on with
  `<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` (planned to be on by default in the
  next major version);
  the severity is set in `.editorconfig` (`dotnet_diagnostic.IM0001.severity = error`).
- **A `null` source throws `ArgumentNullException`**, also when it is declared nullable (`UserEntity?`) ([#27](https://github.com/iron-mapper/IronMapper/issues/27)).
  Mapperly, Mapster and AutoMapper return `null` there.
- **Property type conversions** work only where C# converts implicitly (`int` → `long`, `int` → `int?`, …).
  `int?` → `int`, `long` → `int`, enum ↔ enum/`string`, `Guid` ↔ `string` fail with error `IM0002` at your code (from
  2.1.0; up to 2.0.0 with a C# error in the generated file); use `MapFrom`, a converter, or (from 2.1.0) a mapping of
  the pair, `CreateMap<Guid, UserId>().ConvertUsing(UserId.From)`, which converts every property and parameter of that
  pair ([Value conversions](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md#value-conversions-pair-mappings)).
- **`ProjectTo` / `IQueryable` projection** is not supported ([#9](https://github.com/iron-mapper/IronMapper/issues/9)).

Also: up to 1.1.4 diagnostics have no source location (from 2.0.0 they point to the line); `IMapper` has no per-call context
([#7](https://github.com/iron-mapper/IronMapper/issues/7)); the in-place overload ignores `ConvertUsing` and, up to 2.0.0,
`When` and hooks (from 2.1.0 it applies them and updates existing nested objects; a nested record with `init` properties gets a new instance); a nested
property declared non-nullable that holds `null` (an EF navigation without `Include`) throws `InvalidOperationException`
naming both properties (from 2.1.0);
`ReverseMap()` maps by name only; converters are created by the generated code, not resolved from DI, and need a public
or internal parameterless constructor (`IM0015`); private/protected nested types
cannot be mapped (`IM0010`); the same type pair mapped in two projects makes `source.MapToX()` ambiguous (`CS0121`); the first
`IMapper` registers all mappings of the assembly, which adds 4.5 ms to the cold start over the DI container in the
benchmark (more with more mappings). Details:
[Advanced topics](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md),
[Configuration](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md),
[Diagnostics](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.md).

### Platforms

- Packages: `IronMapper` targets `netstandard2.0` and `net10.0`; `IronMapper.Extensions.DI` targets `netstandard2.1`
  and `net10.0`. The generator needs .NET SDK 8.0.400+.
- The library tests run on .NET 10 (Ubuntu and Windows in CI). The equivalence tests of the benchmark suite also run
  on .NET 8, and the runtime benchmarks of 2.0.0 were measured on .NET 8 and .NET 10. .NET 9 is not tested in CI ([#10](https://github.com/iron-mapper/IronMapper/issues/10)). The Quick Start
  above is compiled and run against the packed package in CI (.NET 10); 1.1.4 was checked by hand on .NET 8, 9
  and 10.

---

## Roadmap

Open issues:

- `ProjectTo<TDestination>()` for `IQueryable` — [#9](https://github.com/iron-mapper/IronMapper/issues/9)
- Context data in `Map` calls (like `ResolutionContext.Items`) — [#7](https://github.com/iron-mapper/IronMapper/issues/7)
- `net9.0` target — [#10](https://github.com/iron-mapper/IronMapper/issues/10)

2.0.0 (this release): generated collection code without LINQ, `IMapper` without reflection that works under NativeAOT
([#26](https://github.com/iron-mapper/IronMapper/issues/26)) and maps collections with `Map<List<T>>`
([#8](https://github.com/iron-mapper/IronMapper/issues/8)), stateless converters created once, diagnostics with source
locations and working code fixes, `IM0001` for profiles behind an opt-in switch
([#22](https://github.com/iron-mapper/IronMapper/issues/22)), optional overloads that can be turned off, a generated
class name of its own in every assembly. It changes behavior that 1.1.x code may rely on:
[Upgrading to 2.0](https://github.com/iron-mapper/IronMapper/blob/main/docs/upgrading-to-2.0.md).

Next, 2.1.0: nested single objects mapped automatically, with `null` checks by nullable annotations
([#23](https://github.com/iron-mapper/IronMapper/issues/23); also aimed at the remaining Nested gap), constructors and
positional records ([#24](https://github.com/iron-mapper/IronMapper/issues/24)), diagnostics `IM0002` (incompatible
types) and `IM0004` (cycles), no in-place overload for a type whose properties are all `init`. The next major version:
`null` → `null` for nullable sources ([#27](https://github.com/iron-mapper/IronMapper/issues/27)), `IM0001` for
profiles on by default, the "mapper class" mode. The working plan (in Russian) is
[IRONMAPPER_PLAN.md](https://github.com/iron-mapper/IronMapper/blob/main/IRONMAPPER_PLAN.md); it may change.

---

## Documentation

- [Getting started](https://github.com/iron-mapper/IronMapper/blob/main/docs/getting-started.md)
- [Configuration reference](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md)
- [Advanced topics](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md)
- [Migrating from AutoMapper](https://github.com/iron-mapper/IronMapper/blob/main/docs/migration-from-automapper.md)
- [Compiler diagnostics](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.md)
- [Upgrading to 2.0](https://github.com/iron-mapper/IronMapper/blob/main/docs/upgrading-to-2.0.md)
- [Upgrading to 2.1](https://github.com/iron-mapper/IronMapper/blob/main/docs/upgrading-to-2.1.md) (unreleased: `ConvertUsing` fixes)
- [Benchmarks](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/README.md)
- [Changelog](https://github.com/iron-mapper/IronMapper/blob/main/CHANGELOG.md)

**Demo.** [samples/IronMapper.Demo](https://github.com/iron-mapper/IronMapper/tree/main/samples/IronMapper.Demo) is
a console app with eight scenarios, most of them on data from the Open Library API (no API key needed): attributes, profiles, collections, nested objects mapped separately,
a custom converter, conditional mapping (`When`), hooks with `ReverseMap`, and `IncludeMembers`.

```bash
git clone https://github.com/iron-mapper/IronMapper
cd IronMapper
dotnet run --project samples/IronMapper.Demo
```

---

## Contributing

1. Fork the repository and create a branch from `main`.
2. Make the change with tests.
3. `dotnet build IronMapper.slnx` and `dotnet test IronMapper.slnx` must pass without warnings.
4. Open a pull request against `main`; add a line to `CHANGELOG.md` under `[Unreleased]`.

Code style: [.editorconfig](https://github.com/iron-mapper/IronMapper/blob/main/.editorconfig).

---

## License

[MIT](https://github.com/iron-mapper/IronMapper/blob/main/LICENSE) © 2026 Aleksey Mironov

# IronMapper

A compile-time object mapper for .NET: a Roslyn source generator writes the mapping code as plain C# when you build.

Русская версия: [README.ru.md](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md)

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
  fewer checks (`IM0001` is not reported for them, [#22](https://github.com/iron-mapper/IronMapper/issues/22)), and incompatible property types fail as a C# error in
  the generated file.
- **Flat objects are as fast as hand-written code:** 11.4 ns vs 11.6 ns per object. Nested objects are slower
  (×1.26), see [Performance](https://github.com/iron-mapper/IronMapper#performance).
- **NativeAOT verified for the generated methods.** They publish with no trim/AOT warnings and run correctly.
  The optional `IMapper` service does not work under NativeAOT yet ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
- **Cold start of about 2 ms** (process start plus the first mapping, over a console app without a mapper), the
  same order as hand-written code. AutoMapper and Mapster need tens of milliseconds there to build their mapping
  configuration ([results](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md#cold-start)).
- **No runtime dependencies.** The `IronMapper` package has no NuGet dependencies and adds one assembly to the app
  (+28.2 KB to a framework-dependent publish). The optional `IronMapper.Extensions.DI` package depends on
  `Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Quick Start

```bash
dotnet add package IronMapper
```

`Program.cs` of a console app (`dotnet new console`):

```csharp
using IronMapper.Attributes;
using IronMapper.Generated;   // required: the generated MapTo* methods live in this namespace

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

No `IMapper` and no service registration are needed.

**`using IronMapper.Generated;` is required** in every file that calls `MapTo*` methods. Without it the call fails
with `CS1061` ("does not contain a definition for 'MapToUserDto'").

**Requirements.** The generator needs the C# compiler from **.NET SDK 8.0.400 or later** (Roslyn 4.11; Visual
Studio 2022 17.11+). On an older SDK the compiler reports warning `CS9057`, the generator does not run, and every
`MapTo*` call fails with `CS1061` as well. The `IronMapper` package targets `netstandard2.0` and `net10.0`; see
[Platforms](https://github.com/iron-mapper/IronMapper#platforms).

---

## Performance

IronMapper 1.1.3 (1.1.4 generates the same code) against hand-written code (`Manual`), Mapperly 4.3.1, Mapster 10.0.13 and AutoMapper 16.2.0.
BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11, Intel Core i7-12650H. Full report, method and raw data:
[benchmarks/results/RESULTS.md](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md).

| Library | Flat | Nested | Collection, 1 000 | Cold start, Δ ms | NativeAOT | Δ publish (JIT) | Δ AOT executable |
|---|---:|---:|---:|---:|---|---:|---:|
| Manual | 11.6 ns | 165.9 ns | 255.0 µs | 1.1 | works | +4.6 KB | +2.0 KB |
| IronMapper | 11.4 ns (×0.98) | 209.8 ns (×1.26) | 258.1 µs (×1.01) | 2.2 | works | +28.2 KB | +255.5 KB |
| IronMapper `IMapper` | 31.0 ns (×2.66) | 227.6 ns (×1.37) | 282.4 µs (×1.11) | 23.1 | fails | +205.8 KB | +1.47 MB |
| Mapperly | 11.6 ns (×0.99) | 165.5 ns (×1.00) | 267.3 µs (×1.05) | -1.1 | works | +38.5 KB | +23.6 KB |
| Mapster | 15.6 ns (×1.34) | 192.3 ns (×1.16) | 263.5 µs (×1.03) | 99.1 | fails | +244.8 KB | +3.99 MB |
| AutoMapper | 34.6 ns (×2.97) | 270.7 ns (×1.63) | 344.6 µs (×1.35) | 73.5 | fails | +1.17 MB | +5.16 MB |

How to read it:

- Flat: one object with 15 properties. Nested: an order with a customer, an address and 5–20 lines. Collection:
  `List<Order>` → `List<OrderDto>` of 1 000 nested orders. Times are means; ×N is the ratio to `Manual`.
- **Nested objects are configured by hand in IronMapper** (`ForMember(... MapFrom(s => s.Customer.MapToCustomerDto()))`),
  because it does not map nested single objects automatically (see [Known limitations](https://github.com/iron-mapper/IronMapper#known-limitations)). The
  other libraries map them automatically.
- Cold start: median wall-clock time of process start plus one mapping, minus the same console app without a mapper
  (30 runs). This metric is sensitive to noise; read it as an order of magnitude.
- NativeAOT: "works" means the published executable ran and printed the correct result. The IronMapper `IMapper`
  executable fails with `MappingException`. Mapster and AutoMapper build their mappers at run time and fail as well.
- Sizes are the increase over the same console app without a mapper.
- Collection differences of a few percent (×1.01–×1.05 here; 100 000 items in the full report) are within the run-to-run
  spread and do not mean one library is faster.
- Measured only on .NET 10 and on one machine. Mapster.Tool (Mapster's code generator) was not measured.

---

## Key features

All examples below assume `using IronMapper.Attributes;` and `using IronMapper.Generated;`. Full reference:
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

**Dependency injection (optional)**

```bash
dotnet add package IronMapper.Extensions.DI
```

```csharp
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;

builder.Services.AddIronMapper(typeof(Program).Assembly);   // registers IMapper and the profiles

app.MapGet("/users/{id}", (int id, IMapper mapper) => mapper.Map<UserDto>(LoadUser(id)));
```

`IMapper` finds and calls the generated methods through reflection. It is slower than the extension methods (see
the table above) and does not work under NativeAOT; prefer the extension methods when the types are known at
compile time.

---

## Known limitations

- **Nested single objects are not mapped automatically** ([#23](https://github.com/iron-mapper/IronMapper/issues/23)). For `Customer.Address` → `CustomerDto.Address`
  the generator emits `Address = source.Address`, and the build fails with `CS0029` in the generated file.
  Workaround: `ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`. Nested collections are
  mapped.
- **Positional records as destination types do not compile** (`CS7036`, [#24](https://github.com/iron-mapper/IronMapper/issues/24)): the generator always uses an object
  initializer. Records and classes with `init` properties work.
- **No flattening by convention** ([#25](https://github.com/iron-mapper/IronMapper/issues/25)): `CustomerAddressCity` is not filled from `Customer.Address.City`. Use
  `MapFrom`.
- **`IM0001` is not reported for profile mappings** ([#22](https://github.com/iron-mapper/IronMapper/issues/22)): an unmatched destination property in a `CreateMap`
  mapping keeps its default value without a warning.
- **`IMapper` (IronMapper.Extensions.DI) uses reflection and does not work under NativeAOT** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)): trim warnings
  IL2026/IL2062/IL2072/IL2075 on publish, `MappingException` at run time. It also has no collection overload of `Map`
  ([#8](https://github.com/iron-mapper/IronMapper/issues/8)) and no per-call context
  ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).
- **A `null` source throws `ArgumentNullException`**, also when it is declared nullable (`UserEntity?`) ([#27](https://github.com/iron-mapper/IronMapper/issues/27)).
  Mapperly, Mapster and AutoMapper return `null` there.
- **Property type conversions** work only where C# converts implicitly (`int` → `long`, `int` → `int?`, …).
  `int?` → `int`, `long` → `int`, enum ↔ enum/`string`, `Guid` ↔ `string` fail with a C# error in the generated file;
  use `MapFrom` or a converter. Declared diagnostics `IM0002`–`IM0004` are not reported yet.
- **`ProjectTo` / `IQueryable` projection** is not supported ([#9](https://github.com/iron-mapper/IronMapper/issues/9)).

Also: diagnostics have no source location; the in-place overload ignores `When`, `ConvertUsing` and hooks;
`ReverseMap()` maps by name only; converters need a public parameterless constructor; private/protected nested types
cannot be mapped (`IM0010`); several projects in one solution have their own `GeneratedMappers` class. Details:
[Advanced topics](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md),
[Configuration](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md),
[Diagnostics](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.md).

### Platforms

- Packages: `IronMapper` targets `netstandard2.0` and `net10.0`; `IronMapper.Extensions.DI` targets `netstandard2.1`
  and `net10.0`. The generator needs .NET SDK 8.0.400+.
- The library tests run on .NET 10 (Ubuntu and Windows in CI). The equivalence tests of the benchmark suite also run
  on .NET 8. .NET 9 is not tested in CI ([#10](https://github.com/iron-mapper/IronMapper/issues/10)). The Quick Start
  above was checked by hand on .NET 8, 9 and 10.

---

## Roadmap

Open issues:

- `ProjectTo<TDestination>()` for `IQueryable` — [#9](https://github.com/iron-mapper/IronMapper/issues/9)
- `Map<List<T>>(source)` through `IMapper` — [#8](https://github.com/iron-mapper/IronMapper/issues/8)
- Context data in `Map` calls (like `ResolutionContext.Items`) — [#7](https://github.com/iron-mapper/IronMapper/issues/7)
- `net9.0` target — [#10](https://github.com/iron-mapper/IronMapper/issues/10)

Next release, 1.2.0: faster generated code for nested objects and collections, `IMapper` without reflection
(NativeAOT, [#26](https://github.com/iron-mapper/IronMapper/issues/26)), `IM0001` for profiles behind an opt-in switch ([#22](https://github.com/iron-mapper/IronMapper/issues/22)), diagnostics with source locations. The
working plan (in Russian) is [IRONMAPPER_PLAN.md](https://github.com/iron-mapper/IronMapper/blob/main/IRONMAPPER_PLAN.md);
it may change.

---

## Documentation

- [Getting started](https://github.com/iron-mapper/IronMapper/blob/main/docs/getting-started.md)
- [Configuration reference](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.md)
- [Advanced topics](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.md)
- [Migrating from AutoMapper](https://github.com/iron-mapper/IronMapper/blob/main/docs/migration-from-automapper.md)
- [Compiler diagnostics](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.md)
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

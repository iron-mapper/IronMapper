# IronMapper

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Build](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> 📖 **[Русская документация](README.ru.md)**

**Compile-time object mapper for .NET.**

IronMapper uses a Roslyn source generator to emit mapping code at build time. The generated `MapTo*` extension methods are plain C# property assignments: no reflection, no IL emit, no dynamic proxies, and you can step through them in the debugger.

The optional `IronMapper.Extensions.DI` package adds an `IMapper` service. It is **not** reflection-free: it finds the generated methods via reflection and calls them through `MethodInfo.Invoke` (see [Known limitations](#known-limitations)).

---

## vs AutoMapper

| | AutoMapper | IronMapper |
|---|---|---|
| Mapping strategy | Mapping plans built at runtime and compiled from expression trees ([docs](https://docs.automapper.io/en/latest/Configuration.html)) | C# generated at compile time |
| Configuration errors | Reported when you call `AssertConfigurationIsValid()`, typically in a unit test ([docs](https://docs.automapper.io/en/latest/Configuration-validation.html)) | Some at build time (IM00xx diagnostics, or C# errors in the generated code); see [Known limitations](#known-limitations) |
| License | 14.x and earlier: MIT. 15.0+: RPL-1.5 or commercial license, with a free Community license for eligible organizations ([automapper.io](https://automapper.io/)) | MIT |
| API style | Profile-based fluent API, `[AutoMap]` attribute | Attributes **or** profile-based fluent API |
| `ProjectTo` for `IQueryable` | Yes | Not yet supported (planned, [#9](https://github.com/iron-mapper/IronMapper/issues/9)) |

No performance or package-size numbers are published yet. Benchmarks exist in `tests/IronMapper.Benchmarks` (manual mapping vs. IronMapper); results will be added to the docs once they are measured and committed.

---

## Quick Start (5 minutes)

### 1. Install

```bash
dotnet add package IronMapper
```

### 2. Annotate your source type

```csharp
using IronMapper.Attributes;

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UserDto
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### 3. Map

```csharp
var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();  // generated extension method
```

That's it. No `IMapper` and no service registration are needed.

---

## Key Features

### Attribute-based mapping

```csharp
[MapTo(typeof(ProductDto))]      // source → dest
[MapFrom(typeof(ProductEntity))] // dest ← source (equivalent)
```

### Property renaming

```csharp
[MapProperty("FullName")]
public string Name { get; set; } = "";  // maps to FullName on destination
```

### Ignore properties

```csharp
[Ignore]
public string InternalSecret { get; set; } = "";
```

### Custom type converters

```csharp
public class PriceConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("F2");
}

[MapTo(typeof(InvoiceDto))]
public class Invoice
{
    [MapConverter(typeof(PriceConverter))]
    public decimal Total { get; set; }
}
```

### Profile-based fluent API

```csharp
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>()
            .ForMember(d => d.FullAddress, o => o.MapFrom(s => s.Street + ", " + s.City))
            .ForMember(d => d.InternalId,  o => o.Ignore());
    }
}
```

### Collection helpers

```csharp
List<UserEntity> entities = ...;
List<UserDto>    dtos = entities.MapToUserDtoList();
UserDto[]        arr  = entities.MapToUserDtoArray();
```

### In-place (void) mapping

```csharp
entity.MapToUserDto(existingDto);  // updates existing object without allocating a new one
```

### Nested collections

```csharp
[MapTo(typeof(BlogDto))]
public class BlogEntity
{
    public List<CommentEntity> Comments { get; set; } = new();
}
// generator emits: Comments = source.Comments is null ? null! : Enumerable.ToList(Enumerable.Select(source.Comments, x => x.MapToCommentDto()))
```

Requires a mapping `CommentEntity → CommentDto`. Only `List<T> → List<TDto>` is covered by tests. Nested *single* objects are not mapped automatically, see [Known limitations](#known-limitations).

### ASP.NET Core DI

```csharp
// Program.cs — registers IMapper and the MappingProfile types from the assembly
builder.Services.AddIronMapper(typeof(Program).Assembly);

// In a controller / service
public class OrdersController(IMapper mapper) : ControllerBase
{
    public OrderDto Get(int id) => mapper.Map<OrderDto>(_repo.GetById(id));
}
```

---

## Demo Application

A sample console app shows the main IronMapper features
using the real Open Library API (no API key required).

**What the demo covers:**
- Simple attribute-based mapping (`[MapTo]`)
- Fluent API with `MappingProfile`
- Collection mapping (`List<BookDoc>` → `List<BookSummary>`)
- Nested objects (book and author mapped separately, then combined in code)
- Custom type converters (HTML bio cleaner)
- Conditional mapping (living authors only)

**Run it:**
```bash
git clone https://github.com/iron-mapper/IronMapper
cd IronMapper
dotnet run --project samples/IronMapper.Demo
```

---

## Known limitations

Current gaps, confirmed by reading the code. Each one is either planned or tracked in an issue.

- **Nested single objects are not mapped automatically.** For `Customer.Address → CustomerDto.Address` with different types the generator emits `Address = source.Address`, and the build fails with `CS0029` in the generated file. Workaround: `ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`. Nested *collections* are handled (see above).
- **Positional records as destinations do not compile** (`CS7036`): the generator always uses an object initializer. Records and classes with `init` properties work.
- **Incompatible property types are not diagnosed.** `IM0002`, `IM0003`, `IM0004` and `IM0007` are declared but never reported; the build fails later with a C# error in the generated file. See [diagnostics](docs/diagnostics.md).
- **Defining the same pair twice** (e.g. `[MapTo]` and `CreateMap` for the same types) makes the generator fail with `CS8785`, and then no mapping code is generated for the project at all.
- **Profile mappings get fewer diagnostics.** `IM0001` and `IM0009` are reported only for `[MapTo]`/`[MapFrom]`; unmatched destination properties in profiles are skipped silently. All diagnostics are reported without a source location.
- **The in-place overload** `source.MapToX(existing)` ignores `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` and skips `init` properties.
- **`ReverseMap()`** maps by property name only; `ForMember`, `When`, hooks and `IncludeMembers` are not reversed.
- **Profile lambdas are copied into the generated code as text.** They can use the lambda parameter and accessible types/members, but not locals or fields of the profile.
- **Converters** are created with `new TConverter()` (a public parameterless constructor is required). `IronMapperOptions.AddConverter<T>()` only registers the type in DI; generated code does not use it.
- **`IMapper` (IronMapper.Extensions.DI) uses reflection**: it scans loaded assemblies, matches methods by destination type name and exact runtime source type, and calls them via `MethodInfo.Invoke`. `Map<List<TDto>>(list)` throws `MappingException` ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); there is no per-call context ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).
- **`ProjectTo` / `IQueryable` projection** is not supported yet (planned, [#9](https://github.com/iron-mapper/IronMapper/issues/9)).
- **NativeAOT / trimming has not been verified.** The generated extension methods contain no reflection; `IMapper` relies on reflection and should be treated as incompatible.
- **Platforms.** Packages target `netstandard2.0` + `net10.0` (IronMapper) and `netstandard2.1` + `net10.0` (IronMapper.Extensions.DI). Tests run only on .NET 10 (Ubuntu and Windows in CI); other runtimes are not tested. The generator is built against Microsoft.CodeAnalysis 4.11.
- **The IM0001/IM0006 code fixes have no tests.**

---

## Documentation

- [Getting Started](docs/getting-started.md)
- [Configuration Reference](docs/configuration.md)
- [Advanced Topics](docs/advanced.md)
- [Migrating from AutoMapper](docs/migration-from-automapper.md)
- [Compiler Diagnostics](docs/diagnostics.md)

---

## Contributing

Contributions are welcome! Please:

1. Fork the repo and create a feature branch (`feature/my-feature`).
2. Make your changes with tests.
3. Run `dotnet build` and `dotnet test` — both must be clean.
4. Open a pull request against `main`.

Please follow the existing code style (see [.editorconfig](.editorconfig)).

---

## License

[MIT](LICENSE) © 2026 Aleksey Mironov

# IronMapper

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Build](https://github.com/algmironov/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/algmironov/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Zero-reflection, compile-time object mapper for .NET.**

IronMapper generates all mapping code at build time using a Roslyn Source Generator — no runtime reflection, no IL emit, no dynamic proxies. Just plain, readable, debuggable C# that the compiler can optimize like any other code.

---

## vs AutoMapper

| Feature | AutoMapper | IronMapper |
|---|---|---|
| Mapping strategy | Reflection at runtime | Source-generated C# at compile time |
| Misconfiguration detected | At app startup | At `dotnet build` |
| NativeAOT / IL trimming | Requires annotations | Fully compatible |
| Performance | ~2–5 µs / object | ~0 ns overhead |
| License | MIT | MIT |
| API style | Profile-based fluent API | Attributes **or** Profile-based fluent API |
| Package size | ~500 KB | ~30 KB |

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

That's it. No `IMapper`, no service registration, no startup overhead.

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
// generator emits: Comments = Enumerable.ToList(Enumerable.Select(source.Comments, x => x.MapToCommentDto()))
```

### ASP.NET Core DI

```csharp
// Program.cs
builder.Services.AddIronMapper(typeof(Program).Assembly);

// In a controller / service
public class OrdersController(IMapper mapper) : ControllerBase
{
    public OrderDto Get(int id) => mapper.Map<OrderDto>(_repo.GetById(id));
}
```

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

[MIT](LICENSE) © Iron Programmer School

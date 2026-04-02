# Advanced Topics

> 📖 **[Русская версия](advanced.ru.md)**

## Nested objects

When a property's type also has a `[MapTo]` or `[MapFrom]` attribute (or a `MappingProfile` mapping), the generator automatically emits a nested call.

```csharp
[MapTo(typeof(AddressDto))]
public class Address
{
    public string Street { get; set; } = "";
    public string City   { get; set; } = "";
}

[MapTo(typeof(CustomerDto))]
public class Customer
{
    public string  Name    { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class AddressDto { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }
```

Generated code:

```csharp
// MapToCustomerDto
return new CustomerDto
{
    Name    = source.Name,
    Address = source.Address.MapToAddressDto(),
};
```

---

## Collections

### Collection extension helpers

For every `[MapTo]` mapping the generator emits two collection helpers:

```csharp
// Returns List<TDest> (empty list when source is null)
IEnumerable<UserEntity>? entities = ...;
List<UserDto> dtos = entities.MapToUserDtoList();

// Returns TDest[] (Array.Empty<TDest>() when source is null)
UserDto[] arr = entities.MapToUserDtoArray();
```

### Nested collection properties

When a source property is `List<T>` and the destination property is `List<TDest>` (with different element types), the generator emits a `Select + ToList` call automatically:

```csharp
[MapTo(typeof(BlogDto))]
public class BlogEntity
{
    public int Id { get; set; }
    public List<CommentEntity> Comments { get; set; } = new();
}
```

Generated:

```csharp
Comments = source.Comments is null ? null!
    : global::System.Linq.Enumerable.ToList(
        global::System.Linq.Enumerable.Select(source.Comments, x => x.MapToCommentDto())),
```

---

## Record types

Records with positional constructors are supported as mapping destinations. The generator emits a constructor call when the destination type has a primary constructor:

```csharp
[MapTo(typeof(PointDto))]
public class PointEntity { public double X { get; set; } public double Y { get; set; } }

public record PointDto(double X, double Y);
```

> **Note:** All constructor parameters must match source property names (case-insensitive). Unmatched parameters emit an `IM0009` warning.

---

## In-place mapping

Every generated mapping includes a void overload that updates an existing object:

```csharp
var entity = new WorkerEntity { Id = 7, Name = "Alice" };
var dto    = new WorkerDto();

entity.MapToWorkerDto(dto);  // populates dto in place

// equivalent via IMapper:
mapper.Map<WorkerEntity, WorkerDto>(entity, dto);
```

`init`-only destination properties are silently skipped by the in-place overload.

---

## Performance tips

- Prefer the generated extension methods (`source.MapToDto()`) over `IMapper.Map<>()` in hot paths — extension methods are plain compiled C# with no reflection.
- `IMapper` performs a one-time reflection lookup per `(TSource, TDest)` pair and caches the `MethodInfo`. Subsequent calls pay only a `ConcurrentDictionary` lookup.
- For bulk collection mapping, use `MapToDestTypeList()` / `MapToDestTypeArray()` instead of a manual `Select` loop — the generated code is equivalent but avoids an extra closure allocation.
- Because all mapping code is generated at compile time, IronMapper is fully compatible with **NativeAOT** and **IL trimming** without any additional annotations.

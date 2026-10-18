# Advanced Topics

> 📖 **[Русская версия](advanced.ru.md)**

## Nested objects

Nested **single** objects are **not mapped automatically yet**. When a source and a destination property have
the same name but different types, the generator emits a plain assignment:

```csharp
// generated for Customer → CustomerDto
Address = source.Address,   // error CS0029: cannot convert Address to AddressDto
```

The build then fails inside the generated file (no IronMapper diagnostic is reported, see
[IM0002](diagnostics.md#im0002--incompatible-property-types)).

**Workaround (profile):** map the nested property explicitly with `MapFrom`. The lambda is emitted into the
generated class, where the other generated `MapTo*` methods are in scope:

```csharp
public class CustomerProfile : MappingProfile
{
    public CustomerProfile()
    {
        CreateMap<Address, AddressDto>();
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()));
    }
}
```

For attribute-based mappings, put `[Ignore]` on the destination property and assign it after mapping.
The demo (`samples/IronMapper.Demo/Demos/Demo4_NestedObjects.cs`) does exactly that.

---

## Collections

### Collection extension helpers

For every mapping (attribute or profile) the generator emits two collection helpers:

```csharp
// Returns List<TDest> (empty list when source is null)
IEnumerable<UserEntity>? entities = ...;
List<UserDto> dtos = entities.MapToUserDtoList();

// Returns TDest[] (Array.Empty<TDest>() when source is null)
UserDto[] arr = entities.MapToUserDtoArray();
```

### Nested collection properties

When a source property is a collection and the destination property is a collection with a **different**
element type, the generator calls `MapTo{DestElementType}()` (or its qualified name, see
[Method names and collisions](configuration.md#method-names-and-collisions)) for every element:

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

Notes:
- A mapping for the element types (`CommentEntity → CommentDto`) must exist; otherwise the generated call does not compile.
- A `null` source collection becomes `null`, not an empty collection.
- Only `List<T> → List<TDto>` is covered by tests. The generator also has code paths for arrays and other collection types, but they are not tested.
- Collections with the same element type are assigned as-is (the reference is copied).

---

## Record types

The generator always creates the destination with an **object initializer** (`new TDest { ... }`); it never
calls a constructor with arguments.

- **Works:** records (and classes) with a parameterless constructor and `init` or `set` properties:

  ```csharp
  public record PointDto
  {
      public double X { get; init; }
      public double Y { get; init; }
  }
  ```

- **Does not compile:** positional records without a parameterless constructor, e.g.
  `public record PointDto(double X, double Y);` — the generated `new PointDto { ... }` fails with `CS7036`.

For attribute mappings, `IM0009` warns when a record's constructor parameter has no matching source property
(see [diagnostics](diagnostics.md#im0009--record-constructor-parameter-not-mapped)).

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

The in-place overload only assigns the mapped properties. It:
- skips `init`-only destination properties;
- does **not** apply `When`, `ConvertUsing`, `BeforeMap` or `AfterMap`.

---

## IMapper vs. generated extension methods

- The generated extension methods (`source.MapToDto()`) are plain compiled C# with no reflection.
- `IMapper` from `IronMapper.Extensions.DI` (`RuntimeMapper`) uses reflection. On the first call for a
  `(TSource, TDest)` pair it scans the loaded assemblies (skipping names that start with `System.` or
  `Microsoft.`) for `IronMapper.Generated.GeneratedMappers` (public or internal) and looks up a `MapTo*` method by
  signature (exact source type as the parameter, `TDest` as the return type), so qualified names (`IM0013`) are found
  too; the `MethodInfo` is cached. **Every call** is then made through `MethodInfo.Invoke`.
- `IMapper.Map<TDest>(object)` uses the runtime type of the source; a derived source type does not match a
  mapping declared for its base type.
- `IMapper.Map<List<TDto>>(list)` is not supported and throws `MappingException`
  ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); use `MapCollection<TSource, TDest>` or `list.MapToDtoList()`.

### Performance

No benchmark results are published yet. `tests/IronMapper.Benchmarks` contains BenchmarkDotNet benchmarks
(manual mapping vs. generated mapping, single object and 1000 items); they run in CI on `main`, and results
will be documented once they are committed.

### NativeAOT and trimming

**Not verified.** The generated extension methods contain no reflection, but no NativeAOT or trimmed publish
is tested. `IMapper` / `AddIronMapper` use reflection (`AppDomain` assembly scan, `Assembly.GetTypes()`,
`MethodInfo.Invoke`) without trimming annotations and should be treated as incompatible.

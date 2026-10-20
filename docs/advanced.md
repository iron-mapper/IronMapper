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

IronMapper 1.1.3, .NET 10.0.12, Intel Core i7-12650H; mean time and the ratio to hand-written code. Full report,
method and the other libraries: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md).

| Scenario | Extension method | `IMapper` |
|---|---:|---:|
| Flat (15 properties) | 11.4 ns (×0.98), 168 B | 31.0 ns (×2.66), 200 B |
| Nested (order, customer, address, 5–20 lines) | 209.8 ns (×1.26) | 227.6 ns (×1.37) |
| Collection, 1 000 orders | 258.1 µs (×1.01) | 282.4 µs (×1.11) |
| Cold start, Δ ms over a console app without a mapper | 2.2 | 23.1 |

- `IMapper` adds the reflection call (`MethodInfo.Invoke`) on every call and the assembly scan on the first call
  for a type pair.
- In the Nested scenario IronMapper maps the nested objects through `ForMember(... MapFrom(...))` (see
  [Nested objects](#nested-objects)); the generated collection code uses `Enumerable.Select` + `ToList`. Mapperly
  is at ×1.00 there.
- Cold start is sensitive to noise; read it as an order of magnitude (AutoMapper +73.5 ms, Mapster +99.1 ms).
- Only .NET 10 was measured. CI only checks that the benchmark suite builds and that every scenario runs one
  iteration on .NET 10 and .NET 8; it does not publish numbers. How to run the suite:
  [`benchmarks/README.md`](../benchmarks/README.md).

### NativeAOT and trimming

The generated mappings are checked in NativeAOT in CI on every change: `eng/verify-aot.ps1` publishes a console app
against the packed package with `PublishAot=true` for .NET 8 and .NET 10 (linux-x64) and runs it
(`tests/IronMapper.AotProbe`: attribute and profile mappings, collections, in-place update, a converter).

- **Generated extension methods: compatible.** No trim/AOT warnings (the CI check fails on any); the executable runs
  and produces the correct result. In the benchmark suite (win-x64, .NET 10,
  [`benchmarks/results/aot.md`](../benchmarks/results/aot.md)) startup is the same as without a mapper, and the
  executable is 255.5 KB larger than the app without a mapper (Mapperly: +23.6 KB), mostly because the generated
  collection code (`Enumerable.Select` + `ToList`) pulls in the NativeAOT type loader.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): not compatible yet** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
  `RuntimeMapper` finds the generated methods by name through reflection (`AppDomain` assembly scan,
  `Assembly.GetType`, `GetMethods`), which the trimmer cannot see, so their metadata is not kept, and the executable
  fails at the first call with `MappingException: No mapping registered`. `AddIronMapper` and `RuntimeMapper` are
  marked `[RequiresUnreferencedCode]`, so publishing with trimming or NativeAOT reports it at your call:

  ```text
  Program.cs(8,1): warning IL2026: Using member 'IronMapper.Extensions.DI.IronMapperServiceCollectionExtensions.AddIronMapper(IServiceCollection, params Assembly[])' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. IMapper finds and calls the generated mapping methods through reflection; trimming and NativeAOT remove them, and mapping fails at runtime with MappingException. Call the generated MapTo* extension methods (using IronMapper.Generated;) instead. https://github.com/iron-mapper/IronMapper/issues/26
  ```

  CI checks that this warning appears and that the IronMapper assemblies report nothing else
  (`tests/IronMapper.AotProbe.DI`). Up to 1.1.4 the warnings (IL2026, IL2062, IL2072, IL2075) pointed inside the
  IronMapper assembly, by default as one summary IL2104, not to your call. Use the extension methods in NativeAOT and
  trimmed apps.
- **`MappingValidator`** reads the properties and constructors of the given types through reflection and is marked
  `[RequiresUnreferencedCode]` as well: calling it in a trimmed or NativeAOT app reports IL2026. The generator reports
  these problems at compile time.

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

The parameter is `IEnumerable<T>`, but the generated code checks the runtime type first: an array or a `List<T>` is
read with a `for` loop and the result is created with the exact size; another `ICollection<T>` gives the size through
`Count` and is read with `foreach`; any other sequence is read with `foreach` into a growing list. No LINQ, no
delegates: each element is mapped by a direct call of `MapToUserDto`.

From 1.2.0 these loops are written once per project, in two generic methods of `GeneratedMappers`
(`MapElementsToList`, `MapElementsToArray`); `MapToUserDtoList` is a one-line call of them with a small private struct
of the mapping whose `Map` calls `MapToUserDto`. Because the struct is a type argument, the runtime compiles a separate
copy of the loop for each mapping, in which the call is direct and can be inlined: a collection method takes a few
lines per mapping instead of a few dozen, without an indirect call per element. The checks, capacities and results are the same.

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

Generated (a private helper per collection property):

```csharp
Comments = MapCollection_Comments_BlogEntity_BlogDto(source.Comments),
// ...
private static List<CommentDto> MapCollection_Comments_BlogEntity_BlogDto(List<CommentEntity>? source)
{
    if (source is null) return null!;
    var result = new List<CommentDto>(source.Count);
    for (var i = 0; i < source.Count; i++) result.Add(source[i].MapToCommentDto());
    return result;
}
```

How the source is read depends on its declared type: `T[]`, `List<T>`, `IList<T>` and `IReadOnlyList<T>` with a
`for` loop over the indexer; `ICollection<T>`, `IReadOnlyCollection<T>` and classes with a public `Count` (e.g.
`HashSet<T>`) with `foreach` over the declared type and a result of the exact size; anything else with `foreach`. The
destination may be `List<T>`, `T[]`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`
(a `List<T>` is assigned) or `IEnumerable<T>`.

Notes:
- A mapping for the element types (`CommentEntity → CommentDto`) must exist; otherwise the generated call does not compile.
- A `null` source collection becomes `null`, not an empty collection.
- An `IEnumerable<T>` destination is a **lazy** sequence (as in 1.1): the elements are mapped when it is enumerated,
  on every enumeration, and changes to the source collection made after the mapping are visible.
- Every combination of the source and destination kinds above is covered by runtime tests (`CollectionShapeTests`).
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
- `IMapper` from `IronMapper.Extensions.DI` (`RuntimeMapper`) calls the same generated methods through delegates,
  without reflection (from 1.2.0). For every assembly with mappings the generator emits a registration
  (`GeneratedMappers.Registration.g.cs`): a module initializer (`[ModuleInitializer]`) hands it to
  `IronMapper.Registration.MapperRegistry`, which registers the mappings when the first `IMapper` is created (or, for
  an assembly first used later, at the next mapping). The module initializer only puts one delegate per assembly on a
  list, so an app that does not use `IMapper` registers nothing; it still loads the IronMapper assembly, because the
  module initializer checks `MapperRegistry.IsSupported` there (see below). It works
  with trimming and NativeAOT, and exceptions of the generated code, your converters and hooks reach the caller as
  they are. Up to 1.1.4 `RuntimeMapper` scanned the loaded assemblies for `GeneratedMappers`, called the methods
  through `MethodInfo.Invoke` (exceptions wrapped in `TargetInvocationException`) and did not work in NativeAOT.
- **Which assemblies are registered.** A module initializer runs when the assembly's code is first used.
  `AddIronMapper(assembly)` (also `AddProfile<T>()` and `AddProfilesFromAssembly`) runs it
  (`MapperRegistry.EnsureModuleInitialized`), so pass every assembly
  that holds mappings, in particular one that the app does not otherwise use before the first mapping (profiles in
  `Application`, entities in `Domain`, the call from the web project). When a mapping is not found, `IMapper` runs the
  module initializers of all loaded assemblies (except names starting with `System.` and `Microsoft.`) and looks
  again. Profiles are not added to the DI container: up to 1.1.4 `AddIronMapper` scanned the assemblies with
  `GetTypes()` and added every `MappingProfile` as a singleton.
- **Trimmed and NativeAOT apps without the DI package.** The registration is behind the feature switch
  `IronMapper.Registration.IsSupported`: for a trimmed or NativeAOT publish in which no project references
  `IronMapper.Extensions.DI` the package turns it off and the trimmer removes the registration, so an app that only
  calls the extension methods does not carry it. See
  [Registration for IMapper](configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps) for the rules and the
  `IronMapperRegistration` property.
- **C# 8** (`LangVersion` below 9) has no module initializers: the registration is generated without
  `[ModuleInitializer]`, and `IMapper` does not find the mappings of that assembly until you call
  `IronMapper.Generated.GeneratedMappers.RegisterIronMapperMappings()` once at startup (the method is `internal`, so
  call it from that assembly). Targets without `ModuleInitializerAttribute` (`netstandard`, .NET Framework) get an
  internal copy of the attribute generated into the assembly.
- `Map<TSource, TDest>(source)` uses the mapping of the exact pair: a static field read and a delegate call. When
  there is none, and for `Map<TDest>(object)`, the mapping is looked up by type: the runtime type of the source, then
  its base types (a `DerivedEntity` without its own mapping uses the one of `Entity`). Interfaces of the source and
  base types of the destination are not considered. If two assemblies map the same type pair, the first registration
  wins.
- **Collections as the destination type** ([#8](https://github.com/iron-mapper/IronMapper/issues/8)): `Map<List<UserDto>>(users)`, `Map<UserDto[]>`,
  `Map<IEnumerable<UserDto>>` and `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` work for
  any `IEnumerable<User>` source (`List`, array, `HashSet`, …) when `User → UserDto` is mapped. They call the
  generated `MapToUserDtoList`/`MapToUserDtoArray`, so the result is a `List<UserDto>` (an array for `T[]`).
- `RuntimeMapper.Map(object source, Type sourceType, Type destinationType)` and `MapperRegistry.Map` map by `Type`.
  The method is not on the `IMapper` interface: a new interface member would break other implementations in 1.x.
- `IMapper.MapCollection<TSource, TDest>` maps the whole source during the call and returns a `List<TDest>` (typed
  as `IEnumerable<TDest>`); enumerating the result again does not map again. Up to 1.1.4 it returned a lazy sequence
  that mapped on every enumeration and threw for a `null` source only when enumerated.

### Performance

IronMapper 1.1.3, .NET 10.0.12, Intel Core i7-12650H; mean time and the ratio to hand-written code. Full report,
method and the other libraries: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md).

| Scenario | Extension method | `IMapper` |
|---|---:|---:|
| Flat (15 properties) | 11.4 ns (×0.98), 168 B | 31.0 ns (×2.66), 200 B |
| Nested (order, customer, address, 5–20 lines) | 209.8 ns (×1.26) | 227.6 ns (×1.37) |
| Collection, 1 000 orders | 258.1 µs (×1.01) | 282.4 µs (×1.11) |
| Cold start, Δ ms over a console app without a mapper | 2.2 | 23.1 |

- In 1.1.3 `IMapper` adds the reflection call (`MethodInfo.Invoke`) on every call and the assembly scan on the first
  call for a type pair. From 1.2.0 it calls the generated methods through delegates and allocates nothing of its own;
  the table is updated by the full run of the release.
- In the Nested scenario IronMapper maps the nested objects through `ForMember(... MapFrom(...))` (see
  [Nested objects](#nested-objects)); the generated collection code of 1.1.3 uses `Enumerable.Select` + `ToList`.
  Mapperly is at ×1.00 there.
- Unreleased (1.2.0): the generated code has no LINQ and no delegates (loops of a known size, see
  [Nested collection properties](#nested-collection-properties)). A preliminary run of the local sources on the same
  machine (not a full run; the table above is updated by the full run of the release): Nested ×1.09, Collection of 10
  orders ×0.97 (Mapperly ×1.18), in-place update ×0.90. The remaining Nested gap is the null checks of the generated
  methods: they keep the JIT from inlining `MapToAddressDto` into `MapToCustomerDto`; without them the same run gives
  ×1.02.
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
  executable of 1.1.3 is 255.5 KB larger than the app without a mapper (Mapperly: +23.6 KB), mostly because the
  generated collection code (`Enumerable.Select` + `ToList`) pulls in the NativeAOT type loader. Unreleased (1.2.0,
  no LINQ in the generated code): +2.6 KB, preliminary, measured with the local sources. The registration for
  `IMapper` is not in such an app: without `IronMapper.Extensions.DI` the trimmer removes it (see
  [Registration for IMapper](configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps)); CI checks that
  it is absent from `tests/IronMapper.AotProbe` and present with `IronMapperRegistration=true`.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): compatible from 1.2.0** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)). They use the
  generated registry (see [IMapper vs. generated extension methods](#imapper-vs-generated-extension-methods)), not
  reflection over the generated code. CI publishes `tests/IronMapper.AotProbe.DI` for .NET 8 and .NET 10 with zero
  trim/AOT warnings allowed and runs it: `AddIronMapper`, `Map` by declared and runtime type, in-place, `MapCollection`,
  `Map<List<T>>`/`Map<T[]>`/`Map<IEnumerable<T>>`, a profile mapping with a converter, the original
  `ArgumentNullException` for a `null` element. It also publishes `tests/IronMapper.AotProbe.Transitive` (the DI package
  only through a library, the mappings in a library without it), checks that with `IronMapperRegistration=false` the
  app fails with the message that names the property, and publishes the extension-method probe and the `IMapper` probe
  trimmed without NativeAOT (`PublishTrimmed`, self-contained; ILLink): no warnings, they run, and the registration is
  removed from the first and kept in the second. Up to 1.1.4 `RuntimeMapper` found the generated methods by name through
  reflection, which the trimmer cannot see: publishing reported IL2026, IL2062, IL2072 and IL2075 inside the
  IronMapper assembly (by default one summary IL2104), and the executable failed at the first call with
  `MappingException: No mapping registered`.
- **`MappingValidator`** reads the properties and constructors of the given types through reflection and is marked
  `[RequiresUnreferencedCode]` as well: calling it in a trimmed or NativeAOT app reports IL2026. The generator reports
  these problems at compile time.

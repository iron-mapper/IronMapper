# Advanced Topics

> 📖 **[Русская версия](advanced.ru.md)**

## Nested objects

From 2.1.0 a nested object is mapped by the mapping of its type pair. When a source and a destination property have
the same name and different types (`Customer.Address` → `CustomerDto.Address` of type `AddressDto`) and the project has
a mapping `Address` → `AddressDto` (an attribute or a profile, in this project or in a referenced one), the generated
code calls it. No `ForMember` is needed:

```csharp
public class CustomerProfile : MappingProfile
{
    public CustomerProfile()
    {
        CreateMap<Address, AddressDto>();
        CreateMap<Customer, CustomerDto>();   // Address and Billing use the mapping above
    }
}

// Generated (abridged):
private static CustomerDto MapToCustomerDtoCore(Customer source)
{
    return new CustomerDto
    {
        Address = MapToAddressDtoCore(source.Address ?? NestedObjectIsNull<Address>("…")), // Address  → AddressDto
        Billing = source.Billing is null ? null : MapToAddressDtoCore(source.Billing),     // Address? → AddressDto?
    };
}
```

Every mapping has a public `MapToX(source)`, which checks `source` for `null` as before, and a private `MapToXCore`
with the same body without the check. A nested object calls `MapToXCore` directly, after its own check: one comparison,
no second check and no throw inside the nested mapping, so the JIT can inline it.

The `null` handling follows the nullable annotations (no `null!` for a non-nullable destination):

| Source property | Destination property | Generated code |
| --- | --- | --- |
| `Address`, `Address?` or without annotations | `AddressDto?` or without annotations | `null` → `null` |
| `Address` or without annotations (`#nullable disable`) | `AddressDto` | `null` throws `InvalidOperationException` that names both properties ([below](#null-in-a-non-nullable-nested-object)) |
| `Address?` | `AddressDto` | error `IM0002`: declare the destination nullable or the source non-nullable, or configure the property (`MapFrom`, `Ignore`) |
| a struct | any | `MapToAddressDtoCore(source.Address)`, no check |

Which mapping is called: the one of the exact pair, otherwise the one of a base type or an interface of the source
type (as for collection elements); a qualified method name (`IM0013`) is followed. A mapping of a referenced assembly
is called as `source.Address.MapToAddressDto()`.

**No mapping for the pair:** error [IM0002](diagnostics.md#im0002--property-types-are-incompatible-or-have-no-mapping)
at the destination property (or at `CreateMap` in a profile) names the pair and what to add; the property is not
generated, the rest of the mapping is. A code fix adds the mapping: `[MapTo(typeof(AddressDto))]` on `Address` for an
attribute mapping, `CreateMap<Address, AddressDto>();` after the `CreateMap` call in a profile. The same fix is offered
for a collection whose elements have no mapping.

**Explicit configuration wins:** `ForMember` with `MapFrom`, `UseConverter` or `Ignore`, `[MapConverter]` and `[Ignore]`
(attribute mappings) — such a property is not mapped automatically. `IncludeMembers` does not map nested objects of the
included members (`IM0002`).

**Collections:** the elements of a nested collection are mapped by the public `MapToX` (a `null` element throws
`ArgumentNullException`, as since 2.0.0); the nested objects of each element are mapped as above.

### Null in a non-nullable nested object

A property declared non-nullable can still hold `null` at run time. The usual case is an EF Core navigation:

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; } = null!;   // filled by EF, only when loaded
}

var order = await db.Orders.SingleAsync(o => o.Id == id);   // no .Include(o => o.Customer)
var dto = order.MapToOrderDto();                             // order.Customer is null
```

The generated code checks the value before the nested mapping and throws `InvalidOperationException` with a message
that names both properties and the usual cause:

```text
OrderEntity.Customer is null, but OrderDto.Customer is not nullable. If it is an EF navigation, check Include;
otherwise declare the destination property nullable (null is then mapped to null) or configure the property explicitly.
```

The same check runs in the in-place overload and for a source without nullable annotations. The fixes:

- load the navigation (`Include`, or a projection that reads it);
- declare the destination property nullable (`CustomerDto? Customer`): `null` is then mapped to `null`;
- configure the property: `ForMember(d => d.Customer, o => o.MapFrom(...))`, `Ignore()`, or `[Ignore]` on an
  attribute mapping.

The exception is `InvalidOperationException`, not `ArgumentNullException`: the argument of `MapToOrderDto` is not `null`,
the object it refers to is in a state the mapping cannot handle. `IMapper` passes it on unchanged. The throw is in one
helper of the generated class (`NestedObjectIsNull`, not inlined), so the mapping that checks stays small. AutoMapper
maps such a `null` to `null` without an error; Mapperly emits no check, so the mapping fails with
`NullReferenceException`.

### In-place update of nested objects

`source.MapToOrderEntity(entity)` (and `IMapper.Map(source, destination)`) updates the nested objects that the
destination already holds instead of replacing them. This matters for EF Core: a tracked navigation replaced by a new
instance is inserted as a new row or fails with a tracking conflict; an owned type is deleted and inserted again.

```csharp
var order = await db.Orders.Include(o => o.Customer).ThenInclude(c => c.Address).SingleAsync(o => o.Id == id);
dto.MapToOrderEntity(order);     // order.Customer and order.Customer.Address are the same instances, with new values
await db.SaveChangesAsync();     // UPDATE, no INSERT
```

For each nested object of the mapping (`Customer`, then `Address` inside it, any depth):

| `source.Customer` | `destination.Customer` | Result |
| --- | --- | --- |
| an object | an object | the existing object is updated by the in-place mapping of the pair (of this project, or the public in-place `MapToX(source, destination)` of a referenced project); `ReferenceEquals` holds |
| an object | `null` | a new object is created and assigned |
| `null` | any | `null` for a nullable destination, otherwise the `InvalidOperationException` [above](#null-in-a-non-nullable-nested-object) |

A new object is assigned (as before 2.1.0) when the existing one cannot be updated. Every case, and what to do when
you need the update in place:

| Case | Why | To update in place |
| --- | --- | --- |
| The destination property is a struct | the getter returns a copy; updating the copy changes nothing | declare the destination type a class (a struct is a value: assigning it is the update) |
| The destination type of the pair has a mapped `init` property (a record with `init` properties) | the in-place overload skips `init` properties, so the updated object would keep their old values | make those properties settable (`set`), or `Ignore` them in the pair's mapping |
| The destination type of the pair is created by a constructor (a positional record, no parameterless constructor) or by a factory (`ConstructWith`, `ConstructUsing`; from 2.1.0) | it has no in-place mapping ([Constructors and positional records](#constructors-and-positional-records)) | give the type a parameterless constructor and settable properties |
| The mapping of the pair uses `ConvertUsing` | the converter makes the object | remove `ConvertUsing` and map the properties (`ForMember`), or update the object with your own code after the in-place call (the in-place overload does not call hooks) |
| The mapping of the pair is in a referenced project with its in-place overload turned off (`IronMapperGenerateInPlace=false`, `GenerateInPlace = false`, `GenerateMethods(inPlace: false)`) | there is no public in-place `MapToX(source, destination)` to call | turn the in-place overload on for that pair in the referenced project |
| The destination property has no getter the generated code can call (set-only, or a non-public getter) | the existing object cannot be read | add a public (or internal) getter |

A referenced project's in-place overload is called as it is: if that mapping has `ConvertUsing` or `init` properties,
its in-place overload ignores the converter and skips them (as for a top-level call). An `init`-only nested property
itself is skipped by the in-place overload, as every `init` property (decided together with `init` in the in-place
overload, item 2.15 of the plan). Properties of the
nested object that the mapping does not set (an `Id` with `[Ignore]` or `Ignore()`) keep their values. Like the in-place
overload itself, the update of a nested object ignores `When`, `ConvertUsing` and the hooks of the pair's mapping.

The update uses the private in-place `MapToXCore(source, destination)` of the pair's mapping. It is generated for every
mapping, also with the in-place overload turned off (`IronMapperGenerateInPlace=false`, `GenerateInPlace = false`,
`GenerateMethods(inPlace: false)`): then there is no public in-place `MapToX`, and `IMapper.Map(source, destination)`
still updates the nested objects. Collections are not merged: a nested collection is mapped to a new list or array, as
before.

### Cycles (IM0004)

A cycle of mappings through an automatically mapped nested object gets warning
[IM0004](diagnostics.md#im0004--nested-object-mapping-closes-a-cycle): `Employee.Manager` (an `Employee`),
`Order.Customer.Orders`. A cycle of types recurses forever only when the data has a cycle (an order whose customer lists
that order; then the mapping fails with `StackOverflowException`); a manager of a manager ends with `null` and is
mapped. The generated code does not track mapped objects. Break the cycle with `Ignore` or `MapFrom` on a property of the
chain, or suppress the warning for models that are always finite:

```csharp
public class EmployeeDto
{
#pragma warning disable IM0004 // a manager chain always ends
    public EmployeeDto? Manager { get; set; }
#pragma warning restore IM0004
}
```

(or `dotnet_diagnostic.IM0004.severity = none` in `.editorconfig`). Cycles only through collections (a tree such as
`Category.Children`) are not reported: they worked in 2.0.0 already.

**Up to 2.0.0** a nested object was not mapped: the generator emitted `Address = source.Address` and the build failed
with `CS0029` inside the generated file. The workaround of those versions,
`ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`, still works. The demo
(`samples/IronMapper.Demo/Demos/Demo4_NestedObjects.cs`) composes a book from two API responses, so it maps the author
separately and attaches it.

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

From 2.0.0 these loops are written once per project, in two generic methods of the generated class
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

## Constructors and positional records

How the destination is created (from 2.1.0, [#24](https://github.com/iron-mapper/IronMapper/issues/24)):

- **With an accessible parameterless constructor** (public or internal), or for a struct: an **object initializer**
  (`new TDest { ... }`), as before. Records and classes with `init` or `set` properties keep the same generated code.
- **Otherwise with a constructor:** a positional record or a class whose constructors all take parameters is created by
  a call of its constructor with named arguments; the destination properties the constructor does not set are filled
  by an object initializer after it.

```csharp
[MapTo(typeof(PendingChangeState))]
public record PendingChange(string NewAddress, DateTimeOffset ExpiresAt);
public record PendingChangeState(string? NewAddress, DateTimeOffset? ExpiresAt);

// generated:
// return new PendingChangeState(
//     NewAddress: source.NewAddress,
//     ExpiresAt: source.ExpiresAt);
```

**Parameters** get their values as properties do:

- from the source property with the same name, **ignoring case** (`id` ↔ `Id`); in attribute mappings also from
  `[MapProperty("parameterName")]` on a source property, and through `[MapConverter]` on it;
- with the same conversions as a property: implicit conversions (`string` → `string?`, `DateTimeOffset` →
  `DateTimeOffset?`, `int` → `long`), collections (`T[]` passed to `IReadOnlyList<T>` as is, other element types
  mapped one by one), nested objects through the mapping of their pair with the same `null` checks
  ([Nested objects](#nested-objects));
- a parameter **with a default value** and no source keeps its default value (`string Theme = "system"`);
- in a profile, `ForMember` of the destination property **with the same name** (ignoring case) configures the
  parameter: `MapFrom`, `UseConverter`, and `Ignore()`, which for a parameter means "use the default value":

  ```csharp
  public record Account(Guid Id, string Email, bool Blocked, string Theme = "system");

  CreateMap<UserRecord, Account>()
      .ForMember(d => d.Blocked, o => o.MapFrom(s => s.BlockedAt != null)); // computed parameter
  ```

- in a profile, `ForCtorParam("parameterName", o => ...)` configures a parameter that has no destination property with
  the same name, with the same options (see [Parameters without a property](#parameters-without-a-property-forctorparam));
- `[Ignore]` on the destination property with the same name, or on the source property, means the default value too.

A destination property whose name is a parameter name is treated as set by the constructor: the initializer does not
write it again, and `IM0001` is not reported for it (a `required` property is still set in the initializer unless the
constructor has `[SetsRequiredMembers]`). Extra source properties are ignored, as for an initializer.

**Which constructor.** Of the accessible constructors (no `ref`/`out` parameters, not the copy constructor of a record)
whose every parameter gets a value, the one with the most parameters mapped from the source, then the one with the
most parameters. Static factory methods are not used unless a profile names one with
[`ConstructWith`](#factories-constructwith): a type with a public constructor and a `Create` method is created by the
constructor. Nothing in the destination type selects a constructor: no IronMapper attribute is needed in domain types;
a profile chooses one with a lambda, `ConstructWith((int from, int to) => new Range(from, to))`. Diagnostics, all
errors that skip the mapping (other mappings are generated):

- a parameter that gets no value (no source property, a value that cannot be converted, `Ignore` without a default
  value) — [`IM0009`](diagnostics.md#im0009--constructor-parameter-not-mapped);
- two constructors that fit equally well — [`IM0017`](diagnostics.md#im0017--constructor-of-the-destination-is-ambiguous);
- no accessible constructor (only a private one, for example a domain type created by a factory method) —
  [`IM0018`](diagnostics.md#im0018--destination-has-no-accessible-constructor): name the factory with
  [`ConstructWith`](#factories-constructwith) in a profile.

**Not generated for such a destination: the in-place overload.** `source.MapToX(destination)` does not exist for a
destination created by a constructor with parameters or by a factory (`ConstructWith`). This is final: updating only the
properties outside the constructor would silently keep the old values of everything that only the constructor or the
factory takes. `IMapper.Map(source, destination)` throws `MappingException` that says so. In the in-place update of a
parent object, a nested object of such a type is replaced by a new instance (as for a record with `init` properties, see
[In-place update of nested objects](#in-place-update-of-nested-objects)).

Other details: an exception thrown by the constructor reaches the caller as it is; `ConvertUsing` creates the object
itself, so no constructor is needed; with `BeforeMap`/`AfterMap` the constructor runs first, then `BeforeMap`, the
assignments of the settable (`set`) properties and `AfterMap`. As for a property, a nullable source for a non-nullable reference parameter
(`string?` → `string`) compiles with a nullable warning in the generated file (`CS8604`; `CS8601` for a property) and
passes `null` on.

### Factories (`ConstructWith`)

A domain type is often created by a static factory method and has a private constructor. In a profile,
`ConstructWith` names what creates the destination (from 2.1.0):

```csharp
public sealed class Team
{
    private Team(MemberId owner, string name, IReadOnlyList<Guid> memberIds) { ... }

    public MemberId Owner { get; }
    public string Name { get; }
    public IReadOnlyList<Guid> MemberIds { get; }
    public string? Motto { get; set; }

    public static Team Restore(MemberId owner, string name, IReadOnlyList<Guid> memberIds) => new(owner, name, memberIds);
}

CreateMap<TeamRow, Team>()
    .ConstructWith(Team.Restore)                                          // a static method group
    .ForMember(d => d.Owner, o => o.MapFrom(s => MemberId.From(s.Owner))); // a value object from a Guid

// generated:
// var destination = Team.Restore(
//     owner: MemberId.From(source.Owner),
//     name: source.Name,
//     memberIds: source.MemberIds);    // Guid[] → IReadOnlyList<Guid> as is
// destination.Motto = source.Motto;    // a property with a public set, after the call
// return destination;
```

- **A static method group** (`ConstructWith(Team.Restore)`): a method of any type that returns the destination type or a
  type derived from it; the constructor of the type may be private. The method must be public or internal, because the
  generated class calls it.
- **A lambda with typed parameters** (`ConstructWith((string name, int age) => new Person(name, age))`): also chooses one
  of several constructors, or prepares a value. Its body is emitted as a private helper method (an expression or a
  block, default values of its parameters kept); like `MapFrom`, it cannot use locals or the profile instance (`IM0012`).
  The parameters need types: their names are matched to the source.
- The parameters of the method or the lambda get their values **as those of a constructor** (see above): the source
  property with the same name ignoring case, `ForCtorParam`, `ForMember` of the destination property with the same name,
  nested objects (also created by a factory: `CreateMap<SettingsRow, Settings>().ConstructWith(Settings.Create)`),
  collections, the default value of an optional parameter. A parameter without a value is
  [`IM0009`](diagnostics.md#im0009--constructor-parameter-not-mapped).
- **After the call**, the destination properties with a public `set` that no parameter sets are assigned. An `init`
  property cannot be set after a call: when the source has a value for it, the generator reports
  [`IM0021`](diagnostics.md#im0021--init-property-cannot-be-set-after-the-factory) (warning) and leaves it out.
- An exception of the factory (for example a value object that rejects `Guid.Empty`) reaches the caller as it is, also
  through `IMapper`.
- No in-place overload (see above); `ReverseMap()` maps the reverse direction by name, without the factory.
- A method group of an overloaded method has no delegate type in C#; choose the overload with a lambda that calls it,
  `ConstructWith((Guid id, string name) => Team.Restore(id, name))`. The short forms need C# 10; with older language
  versions write `ConstructWith(new Func<Guid, string, Team>(Team.Restore))`.
- An argument that the generated code cannot call (an instance method, a private method, a wrong or nullable return
  type, a `ref` parameter, a delegate in a variable) is
  [`IM0020`](diagnostics.md#im0020--factory-of-constructwith-cannot-be-used), not an error in the generated file.

### Parameters without a property (`ForCtorParam`)

`ForMember` selects a destination property, so it cannot configure a parameter whose name no property has.
`ForCtorParam` takes the parameter name and the same options as `ForMember` (`MapFrom`, `UseConverter`, `Ignore()` for the
default value), for the constructor the generator chooses and for the factory of `ConstructWith`:

```csharp
public class Author
{
    public Author(string display, string code) { Text = display; Id = code; }
    public string Text { get; }
    public string Id { get; }
}

CreateMap<AuthorRow, Author>()
    .ForCtorParam("display", o => o.MapFrom(s => s.First + " " + s.Last))
    .ForCtorParam(nameof(AuthorRow.Code), o => o.MapFrom(s => s.Code.ToUpperInvariant()));
```

The name is compared ignoring case and must be a constant: a literal, or `nameof` of something with the same name (C#
has no `nameof` of a parameter outside its method). A name that is no parameter of what creates the destination is
[`IM0019`](diagnostics.md#im0019--forctorparam-names-no-parameter) (error), not a silently ignored setting.

### `ConstructUsing` (as in AutoMapper)

`ConstructUsing(s => new OrderDto(s.Id))` creates the destination with the expression as written, without matching any
parameter: the same lambda rules as `MapFrom` (an expression tree, no locals of the profile). Then the destination
properties with a public `set` are mapped as usual and assigned after it; `init` properties are left to the expression.
The in-place overload ignores it, as in AutoMapper, and updates the existing object; for a destination without an
accessible parameterless constructor there is no in-place overload. Prefer `ConstructWith` for new code: it matches the
parameters and reports a parameter without a value at build time.

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
- is not generated for a destination created by a constructor with parameters or a factory (`ConstructWith`)
  ([Constructors and positional records](#constructors-and-positional-records));
- ignores `ConstructUsing` (the existing object is updated);
- skips `init`-only destination properties;
- does **not** apply `When`, `ConvertUsing`, `BeforeMap` or `AfterMap`.

---

## IMapper vs. generated extension methods

- The generated extension methods (`source.MapToDto()`) are plain compiled C# with no reflection.
- `IMapper` from `IronMapper.Extensions.DI` (`RuntimeMapper`) calls the same generated methods through delegates,
  without reflection (from 2.0.0). For every assembly with mappings the generator emits a registration
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
  `RegisterIronMapperMappings()` of the assembly's generated class once at startup, e.g.
  `IronMapper.Generated.GeneratedMappers_MyCompany_Mappings.RegisterIronMapperMappings()` for the assembly
  `MyCompany.Mappings` (the method is `internal`, so call it from that assembly; up to 1.1.4 the class was
  `GeneratedMappers`, see [Multi-project solutions](configuration.md#multi-project-solutions)). Targets without `ModuleInitializerAttribute` (`netstandard`, .NET Framework) get an
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
  The method is not on the `IMapper` interface: a new interface member would break other implementations of it.
- `IMapper.MapCollection<TSource, TDest>` maps the whole source during the call and returns a `List<TDest>` (typed
  as `IEnumerable<TDest>`); enumerating the result again does not map again. Up to 1.1.4 it returned a lazy sequence
  that mapped on every enumeration and threw for a `null` source only when enumerated.

### Performance

IronMapper 2.0.0, .NET 10.0.12 and .NET 8, Intel Core i7-12650H; mean time and the ratio to hand-written code. Full
report, method, the other libraries and the comparison with 1.1.3 measured in the same session:
[`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md).

| Scenario | Extension method, .NET 10 | `IMapper`, .NET 10 | Extension method, .NET 8 | `IMapper`, .NET 8 |
|---|---:|---:|---:|---:|
| Flat (15 properties) | 11.4 ns (×0.99), 168 B | 16.3 ns (×1.41), 168 B | 15.4 ns (×1.01) | 20.9 ns (×1.37) |
| Nested (order, customer, address, 5–20 lines) | 177.8 ns (×1.07) | 185.5 ns (×1.11) | 208.1 ns (×1.09) | 215.6 ns (×1.13) |
| Collection, 1 000 orders | 271.4 µs (×1.05) | 253.0 µs (×0.98) | 274.9 µs (×1.02) | 284.3 µs (×1.05) |
| In-place update | 8.0 ns (×0.91), 0 B | 14.0 ns (×1.59), 0 B | 8.3 ns (×0.93) | 14.9 ns (×1.68) |
| Cold start, Δ ms over a console app without a mapper | 1.3 | 19.9 | – | – |

- `IMapper` calls the generated methods through the delegates of the generated registry and allocates nothing of its
  own (the same bytes as the extension method). What remains over the extension method (+5 ns on Flat) is the call of
  the generic interface method `IMapper.Map<TSource, TDest>`. In the DI overhead benchmark (both resolved from the same
  container, flat object) IronMapper's `IMapper` takes 15.4 ns, AutoMapper's 34.5 ns. 1.1.3 called through reflection:
  30.2 ns on Flat, +32 B per call.
- `IMapper` cold start: the DI container alone (the same hand-written mapping resolved from it, `Manual_DI`) is
  +15.3 ms; IronMapper adds 4.5 ms on top of it (registration of the mappings when the first `IMapper` is created, and
  the first call), more with more mappings. 1.1.3: 6.4 ms.
- In the Nested scenario of these 2.0.0 numbers IronMapper maps the nested objects through `ForMember(... MapFrom(...))`
  (2.0.0 does not map them automatically); Mapperly maps them automatically and is at ×0.98 there. The generated code
  has no LINQ and no delegates (loops of a known size, see
  [Nested collection properties](#nested-collection-properties)); 1.1.3 used `Enumerable.Select` + `ToList` and was at
  ×1.25. The remaining Nested gap of 2.0.0 is the null checks of the generated methods: they keep the JIT from inlining
  `MapToAddressDto` into `MapToCustomerDto` (checked with `DisassemblyDiagnoser`). From 2.1.0 the suite maps the nested
  objects automatically, and a nested object calls the inner `MapToXCore` without the check (see
  [Nested objects](#nested-objects)); this is not measured yet.
- Collection differences of a few percent are within the run-to-run spread: at 1 000 orders this run has `IMapper`
  faster than the extension methods it calls, and a separate run of IronMapper alone gave ×1.01.
- Cold start is sensitive to noise; read it as an order of magnitude (AutoMapper +72.1 ms, Mapster +98.2 ms). The
  probes (cold start, NativeAOT, size, build time) run on .NET 10 only.
- One machine. CI only checks that the benchmark suite builds and that every scenario runs one iteration on .NET 10
  and .NET 8; it does not publish numbers. How to run the suite: [`benchmarks/README.md`](../benchmarks/README.md).

### NativeAOT and trimming

The generated mappings are checked in NativeAOT in CI on every change: `eng/verify-aot.ps1` publishes a console app
against the packed package with `PublishAot=true` for .NET 8 and .NET 10 (linux-x64) and runs it
(`tests/IronMapper.AotProbe`: attribute and profile mappings, collections, in-place update, a converter).

- **Generated extension methods: compatible.** No trim/AOT warnings (the CI check fails on any); the executable runs
  and produces the correct result. In the benchmark suite (win-x64, .NET 10,
  [`benchmarks/results/aot.md`](../benchmarks/results/aot.md)) startup is the same as without a mapper, and the
  executable is 2.6 KB larger than the app without a mapper (Mapperly: +23.6 KB). In 1.1.3 it was +255.5 KB, mostly
  because the generated collection code (`Enumerable.Select` + `ToList`) pulled in the NativeAOT type loader. The
  `IMapper` probe adds 1.50 MB, almost all of it Microsoft.Extensions.DependencyInjection (the same hand-written mapping
  behind the same container: +1.05 MB). The registration for
  `IMapper` is not in such an app: without `IronMapper.Extensions.DI` the trimmer removes it (see
  [Registration for IMapper](configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps)); CI checks that
  it is absent from `tests/IronMapper.AotProbe` and present with `IronMapperRegistration=true`.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): compatible from 2.0.0** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)). They use the
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

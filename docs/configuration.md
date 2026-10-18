# Configuration Reference

> 📖 **[Русская версия](configuration.ru.md)**

## Generated method accessibility

Applies to both `[MapTo]`/`[MapFrom]` and `MappingProfile` mappings.

Each generated method (`MapToX`, the in-place overload, `MapToXList`, `MapToXArray`) is as accessible as the
**less accessible** of the source and destination types. The *effective* accessibility is used: containing types of
nested types and generic type arguments count too.

| Source / destination types | Generated methods |
|---|---|
| both `public` (including all containing types and type arguments) | `public` |
| at least one `internal` or `protected internal` (or nested in an `internal` type) | `internal` |
| at least one `private`, `protected` or `private protected` nested type | not generated, error [`IM0010`](diagnostics.md#im0010--mapping-type-is-not-accessible-from-generated-code) |

`BeforeMap`/`AfterMap` and `AddTransformer` helpers are always `private`.

Internal mappings work through `IMapper` (`IronMapper.Extensions.DI`) as well: it also looks up non-public generated
methods. Code in other assemblies can use an internal mapping only through `[InternalsVisibleTo]`.

---

## Method names and collisions

Every mapping generates `MapTo{Name}` (new object), the in-place overload `MapTo{Name}(destination)`,
`MapTo{Name}List` and `MapTo{Name}Array`, where `{Name}` is the **simple name** of the destination type. Mappings to
different destination types from different source types are overloads and keep these names even when the simple names
are equal (`Shop.Item → Shop.ItemDto` and `Stock.Item → Stock.ItemDto` both use `MapToItemDto`).

### Same type pair defined twice

A type pair is generated **once**, and warning [`IM0007`](diagnostics.md#im0007--duplicate-mapping-definition)
lists all definitions. The definition that is generated:

1. `CreateMap` in a `MappingProfile`;
2. `ReverseMap()` in a profile;
3. `[MapTo]`;
4. `[MapFrom]`.

Profiles take precedence over attributes because a profile is the explicit configuration (`ForMember`, hooks, `When`),
as the IM0007 message has always stated; an explicit `CreateMap` beats a `ReverseMap()`, which maps by name only. Among
definitions of the same kind, the one from the profile (or type) whose full name sorts first (ordinal) wins, then the
first call in the constructor. All other mappings of the project are generated.

### One source type, several destination types with the same simple name

Two methods `MapToUserDto(this User)` cannot coexist (`CS0111`), so **all** mappings of that group get a qualified
name, and warning [`IM0013`](diagnostics.md#im0013--mapping-method-name-qualified-to-avoid-a-collision) gives the name
to call. The first rule that makes every name of the group unique (and different from the other method names of the
same source type) is used:

1. the last 1, 2, … segments of the destination's namespace and containing types, before the name;
2. the type arguments by simple names, after the name;
3. both;
4. the type arguments by full names.

| Mappings from `App.User` / `App.Item` | Generated names |
|---|---|
| `App.Admin.UserDto`, `App.Public.UserDto` | `MapToAdminUserDto`, `MapToPublicUserDto` (+ `List`, `Array`) |
| `App.Orders.Dto`, `App.Invoices.Dto` (nested types) | `MapToOrdersDto`, `MapToInvoicesDto` |
| `App.Box<int>`, `App.Box<string>` | `MapToBoxOfInt32`, `MapToBoxOfString` |
| `Global` namespace: `Dto` and `App.Dto` | `MapToGlobalDto`, `MapToAppDto` |
| `App.SummaryDto` (no collision) | `MapToSummaryDto`, unchanged |

Generated code that maps nested collection elements and `IMapper` (`IronMapper.Extensions.DI`, which matches methods
by signature) use the qualified names automatically. Code you write — including `MapFrom` lambdas — must call the
qualified name. If no rule gives unique names (they are all taken by other mappings), the mappings of that group are not
generated and error [`IM0014`](diagnostics.md#im0014--mapping-method-name-collision-cannot-be-resolved) is reported.

Names that compiled before do not change: the rules apply only to groups that used to fail with `CS0111`. Adding a
second destination type with the same simple name later does rename the existing mapping of that source type
(IM0013 reports the new name). To keep `MapToUserDto`, rename one of the destination types.

### Helper and file names

`BeforeMap`/`AfterMap` and `AddTransformer` helpers are named `BeforeMap_{Source}_{Dest}`,
`TransformValue_{Type}_{Source}_{Dest}` by simple names. When another mapping has the same simple names, the full names
are used instead (`TransformValue_String_Shop_Item_Shop_ItemDto`). These helpers are `private`. Generated files are
named after the full names of both types (`Shop.Item_MapTo_Shop.ItemDto.g.cs`).

---

## Attributes

### `[MapTo(typeof(TDest))]`

Applied to the **source** type. Tells the generator to emit a `MapToTDest()` extension method.

```csharp
[MapTo(typeof(ProductDto))]
public class ProductEntity { ... }
```

Multiple destinations are supported — apply the attribute more than once:

```csharp
[MapTo(typeof(ProductDto))]
[MapTo(typeof(ProductSummaryDto))]
public class ProductEntity { ... }
```

---

### `[MapFrom(typeof(TSource))]`

Applied to the **destination** type. Functionally identical to `[MapTo]` on the source — use whichever placement makes more sense in your codebase.

```csharp
[MapFrom(typeof(ProductEntity))]
public class ProductDto { ... }
```

---

### `[MapProperty("DestPropertyName")]`

Applied to a **source property**. Remaps the value to a destination property with a different name (matched case-insensitively). Attribute-based mappings only.

```csharp
[MapTo(typeof(PersonDto))]
public class Person
{
    [MapProperty("FullName")]
    public string Name { get; set; } = "";
}

public class PersonDto
{
    public string FullName { get; set; } = "";
}
```

---

### `[Ignore]`

Applied to a **source property** or a **destination property**. The property is excluded from both the creating and in-place mapping methods; on a destination property it also suppresses `IM0001`.

`[Ignore]` is read only for `[MapTo]`/`[MapFrom]` mappings. In profiles use `ForMember(d => d.X, o => o.Ignore())`.

```csharp
[MapTo(typeof(OrderDto))]
public class Order
{
    public int Id { get; set; }

    [Ignore]
    public string InternalToken { get; set; } = "";
}
```

---

### `[MapConverter(typeof(TConverter))]`

Applied to a **source property** when the source and destination property types differ and a custom conversion is needed. The generated code creates the converter with `new TConverter()`, so it needs a public parameterless constructor.

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

public class InvoiceDto
{
    public string Total { get; set; } = "";
}
```

---

## Fluent API (MappingProfile)

### `CreateMap<TSource, TDest>()`

Registers a mapping pair inside a `MappingProfile` constructor.

```csharp
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>();
    }
}
```

---

### `ForMember(dest => dest.Prop, opt => opt.MapFrom(...))`

Customises how a specific destination property is populated. The lambda is rewritten into the generated code with fully qualified names; see [Profile lambdas](#profile-lambdas) for what it may contain.

```csharp
CreateMap<Employee, EmployeeDto>()
    .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
```

---

### `ForMember(dest => dest.Prop, opt => opt.Ignore())`

Excludes a destination property from the mapping.

```csharp
CreateMap<User, UserDto>()
    .ForMember(d => d.PasswordHash, opt => opt.Ignore());
```

---

### `ForMember(dest => dest.Prop, opt => opt.UseConverter<TConverter>())`

Applies a type converter to a single property (created with `new TConverter()`). Covered by a generator test that checks the emitted code only; there is no runtime test yet.

```csharp
CreateMap<Product, ProductDto>()
    .ForMember(d => d.Price, opt => opt.UseConverter<DecimalToStringConverter>());
```

---

### `ConvertUsing<TConverter>()`

Replaces the entire object initializer with a single converter call. Not applied by the in-place overload `source.MapToX(existing)`. Covered by a generator test of the emitted code only.

```csharp
public class FullOrderConverter : ITypeConverter<OrderEntity, OrderDto>
{
    public OrderDto Convert(OrderEntity source) => new() { ... };
}

CreateMap<OrderEntity, OrderDto>()
    .ConvertUsing<FullOrderConverter>();
```

---

### `ConvertUsing(src => ...)`

Lambda-based whole-object conversion. The expression body is emitted verbatim by the generator. Not applied by the in-place overload. Covered by a generator test of the emitted code only.

```csharp
CreateMap<Point, PointDto>()
    .ConvertUsing(src => new PointDto { X = src.X, Y = src.Y });
```

---

### `When(src => condition)`

Adds a guard: the mapping only executes when the predicate is true; otherwise `default` (`null` for classes) is returned. Not applied by the in-place overload.

```csharp
CreateMap<UserEntity, UserDto>()
    .When(src => src.IsActive);
```

---

## Profile lambdas

Applies to every lambda in a `MappingProfile` constructor: `MapFrom`, `When`, `ConvertUsing(src => ...)`,
`BeforeMap`/`AfterMap` and `AddTransformer`.

The generator does not call these lambdas at runtime. It rewrites their code into the generated class
`IronMapper.Generated.GeneratedMappers` using the semantic model:

- lambda parameters are renamed to `source` / `destination` / `value` by symbol, so string literals and member names
  that happen to match the parameter name are left alone (`s => s.s + "s"` becomes `source.s + "s"`);
- namespaces, types, static members, constants and enum values get fully qualified names (`global::...`), so the
  `using` directives of the profile file are not needed in the generated file;
- extension method calls become static calls on their declaring class
  (`s.Title.ToSlug()` → `global::App.Text.SlugExtensions.ToSlug(source.Title)`).

**Can be used:** the lambda's own parameters; static methods, properties, fields and constants of any type the
generated code can access, including `public` and `internal` static members of the profile itself; enum values;
extension methods; locals and nested lambdas declared inside the lambda; `nameof(...)`.

**Cannot be used:**

| Reference | Diagnostic | Why / fix |
|---|---|---|
| `private` or `protected` members or types, e.g. a `private static` helper of the profile | [`IM0011`](diagnostics.md#im0011--profile-lambda-uses-a-member-that-generated-code-cannot-access) | The code runs in `GeneratedMappers`, a different class, where private and protected members are not visible. Make the member `internal`: internal members are visible to all code in the same assembly, including the generated class. |
| Instance members of the profile, `this`, `base` | [`IM0012`](diagnostics.md#im0012--profile-lambda-captures-state) | There is no profile instance when the mapping runs. Make the member `static` (or `const`). |
| Local variables, parameters and local functions of the constructor | [`IM0012`](diagnostics.md#im0012--profile-lambda-captures-state) | They do not exist in the generated code. Use a constant or a static member, or compute the value from the source. |

If a lambda has such a problem, that mapping (and its `ReverseMap`) is not generated and the error names the member
and the profile; the other mappings are still generated. A problem in an `AddTransformer` lambda drops that
transformer and reports the error.

Notes:
- `MapFrom` takes an expression tree (`Expression<Func<...>>`), so C# itself does not allow `?.`, block bodies and some
  other constructs there.
- Extension methods called after `?.` (possible in `When`, hooks and transformers) are emitted as written. They compile
  only if their namespace is imported for the whole project, for example with `global using`.
- The lambdas are not invoked as delegates on purpose: that would require creating the profile at runtime via
  reflection, and a delegate call cannot be translated to SQL, which would rule out a future `ProjectTo`.

---

## Custom Converters (`ITypeConverter<TSource, TDest>`)

Implement `ITypeConverter<TSource, TDest>` for reusable conversions:

```csharp
public class DateToStringConverter : ITypeConverter<DateTime, string>
{
    public string Convert(DateTime source) => source.ToString("yyyy-MM-dd");
}
```

`IronMapperOptions.AddConverter<T>()` registers the converter type in the DI container as a transient service.
This does **not** affect mapping: the generated code does not resolve converters from DI and always creates them with
`new TConverter()`.

```csharp
services.AddIronMapper(opt =>
{
    opt.AddProfilesFromAssembly(typeof(Program).Assembly);
    opt.AddConverter<DateToStringConverter>();
});
```

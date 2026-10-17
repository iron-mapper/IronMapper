# Configuration Reference

> 📖 **[Русская версия](configuration.ru.md)**

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

Customises how a specific destination property is populated. The lambda body is copied into the generated code as text (with the parameter renamed to `source`), so it may use the lambda parameter and accessible types and members, but not local variables or fields of the profile.

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

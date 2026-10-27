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

Internal mappings work through `IMapper` (`IronMapper.Extensions.DI`) as well: the generated registration of the
assembly registers them too. Code in other assemblies can call an internal mapping directly only through
`[InternalsVisibleTo]`.

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

Generated code that maps nested objects and nested collection elements and `IMapper` (`IronMapper.Extensions.DI`, which
uses the generated registration) use the qualified names automatically. Code you write — including `MapFrom` lambdas — must call the
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

## Global using of `IronMapper.Generated`

The generated `MapTo*` methods live in the namespace `IronMapper.Generated`. The `IronMapper` package (also when it
comes through `IronMapper.Extensions.DI`) adds `global using IronMapper.Generated;` to C# projects with implicit
usings (`<ImplicitUsings>enable</ImplicitUsings>` or `true`, the default in the .NET 6+ templates); the file is
`buildTransitive/IronMapper.props` in the package.

- **Without implicit usings** nothing is added: write `using IronMapper.Generated;` in the files that call `MapTo*`
  methods (C# 9 and earlier have no global usings at all).
- An explicit `using IronMapper.Generated;` next to the global one compiles without compiler warnings. With a strict
  code style in the build (`<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and
  `dotnet_diagnostic.IDE0005.severity = error` or `warning` with `TreatWarningsAsErrors`) it is reported as an
  unnecessary using: `Program.cs(7,1): error IDE0005: Using directive is unnecessary.` Remove the explicit usings, or
  turn the global using off (below). Checked by `eng/verify-package.ps1` against the packed package.
- A project without mappings gets an empty `IronMapper.Generated` namespace from the generator, so the global using
  compiles there too.
- **Turn it off** for a project (or for all projects in `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <IronMapperImplicitUsings>false</IronMapperImplicitUsings>
  </PropertyGroup>
  ```

The global using has the same effect as writing `using IronMapper.Generated;` in every file. In a file that did not
import the namespace before, a call `x.MapToY()` becomes ambiguous (`CS0121`) only if the file also imports, through
a `using` directive, another extension method with the same name and parameter type (for example a hand-written mapper
from the code you migrate): remove or rename it, or turn the global using off. For the same type pair mapped in two
projects see [Multi-project solutions](#multi-project-solutions).

## Multi-project solutions

Every project that references the `IronMapper` package gets its own class with the mappings of that project, in the
namespace `IronMapper.Generated`. From 2.0.0 the class name contains the assembly name:
`GeneratedMappers_{AssemblyName}`, every character of the assembly name that is not a letter, a digit or `_` replaced
with `_` (`Shop.Application` → `GeneratedMappers_Shop_Application`, `My-App.Web` → `GeneratedMappers_My_App_Web`). An
assembly name longer than 64 characters keeps its first 55 characters, followed by `_` and 8 hex digits of a hash of the
full name (FNV-1a; the same in every build). Up to 1.1.4 the class was `GeneratedMappers` in every assembly, and naming
it reported `CS0436` when a referenced project's class was visible. With the registration for `IMapper` every assembly
would also have members and nested types with the same full names (`RegisterIronMapperMappingsCore`, its lambdas in
`<>c`, `ConverterCache<T>`, …): tools that identify types by their full name, such as ArchUnitNET, attributed the
dependencies of one assembly's generated class to the other's, and a layer rule like "Application does not depend on
Infrastructure" failed (`GeneratedTypeNamesArchitectureTests`). Two assemblies whose names differ only in the replaced characters
(`My.App` and `My_App`) get the same class name; the compiler does not mind (the classes are in different assemblies),
only such tools would. The generated files keep their names (`GeneratedMappers.g.cs`,
`GeneratedMappers.Registration.g.cs`).

**Accessibility of the class.** The class is `public` if at least one generated method of the project is `public`,
and `internal` otherwise. A project with only internal mappings therefore exposes nothing: projects that reference it
do not see its class at all (unless it grants `[InternalsVisibleTo]`).

**Calling mappings of a referenced project.** Use the extension method syntax; the namespace is imported by the
[global using](#global-using-of-ironmappergenerated) or by `using IronMapper.Generated;`:

```csharp
using IronMapper.Generated;               // only without implicit usings

var dto = product.MapToProductDto();      // public mapping declared in a referenced project
var order = entity.MapToOrderDto();       // mapping declared in this project
```

Mappings of the current project and public mappings of referenced projects can be used side by side; this builds with
`TreatWarningsAsErrors` (covered by `GeneratedClassVisibilityTests` and by the package consumer in
`tests/IronMapper.PackageConsumer`, which references two libraries that use IronMapper).

Qualified names ([`IM0013`](#one-source-type-several-destination-types-with-the-same-simple-name)) of a referenced
project's public mappings are called the same way, by the name the warning reported in that project
(`member.MapToAdminMemberDto()`, `members.MapToAdminMemberDtoList()`).

**Naming the class** works with the full name of each project's class, e.g.
`GeneratedMappers_Shop_Application.MapToOrderDto(entity)` and `GeneratedMappers_Shop_Catalog.MapToProductDto(product)`
from the same file (`GeneratedClassVisibilityTests`). Prefer the extension method syntax: it does not change when an
assembly is renamed.

One limitation remains:

- **Define each mapping in one project.** If a referenced project already exposes a mapping for a type pair and the
  current project maps the same pair again, the current project still builds, but calling `source.MapToX()` in it is
  ambiguous (`CS0121`). The same happens inside the generated code when a nested collection's element type is mapped
  in both projects. Keep the mapping in the project that owns the types.

`IMapper` (`IronMapper.Extensions.DI`) uses the mappings of every assembly whose generated registration has run, whether
the class is public or internal: pass the assemblies with mappings to `AddIronMapper` (see
[IMapper vs. generated extension methods](advanced.md#imapper-vs-generated-extension-methods)). A library with mappings
does not need the DI package itself: it is enough that the application references it.

## Registration for `IMapper` in trimmed and NativeAOT apps

For `IMapper`, every assembly with mappings registers them through a generated module initializer (see
[IMapper vs. generated extension methods](advanced.md#imapper-vs-generated-extension-methods)). In a trimmed or
NativeAOT app that registration would keep the registry and every generated method of the assembly, also in an app
that only calls the extension methods. It is therefore behind the feature switch `IronMapper.Registration.IsSupported`
(`MapperRegistry.IsSupported`), which the `IronMapper` package sets from the MSBuild property `IronMapperRegistration`
of the application project (`buildTransitive/IronMapper.targets`):

| Application | Switch | Registration |
|---|---|---|
| `PublishAot` or `PublishTrimmed`, no project references `IronMapper.Extensions.DI` | `false` | removed by the trimmer |
| references `IronMapper.Extensions.DI`, directly or through one of its libraries | not set (on) | kept |
| not trimmed (`dotnet run`, a publish without trimming) | not set (on) | kept |

Set the property in the application project when the automatic choice is wrong, for example when a NativeAOT app uses
`MapperRegistry` without the DI package (`true`), or references the DI package but does not use `IMapper` (`false`):

```xml
<PropertyGroup>
  <IronMapperRegistration>true</IronMapperRegistration>
</PropertyGroup>
```

- With the switch off, creating `IMapper` (`RuntimeMapper`) throws `InvalidOperationException` that names the
  property, and the `MappingException` of `MapperRegistry` for a missing mapping says the same, instead of a plain "No
  mapping registered".
- The generated extension methods do not depend on the switch.
- The property matters only in the application project (it becomes a `RuntimeHostConfigurationOption`); in a library
  it has no effect. With project references to the IronMapper sources instead of the package nothing is set and the
  registration is kept.

---

## Generated overloads and code coverage

For every mapping the generator emits `MapToX(source)` and, by default, three more overloads: the in-place
`MapToX(source, destination)`, `MapToXList(...)` and `MapToXArray(...)`. The optional ones can be turned off (from 2.0.0)
when a project does not use them: less generated code, fewer methods in IntelliSense, and no uncovered methods in code
coverage reports. Nothing changes by default.

**For the whole project** (MSBuild properties, `true` by default; only `false`, case-insensitive, turns one off):

```xml
<PropertyGroup>
  <IronMapperGenerateInPlace>false</IronMapperGenerateInPlace>   <!-- MapToX(source, destination) -->
  <IronMapperGenerateList>false</IronMapperGenerateList>         <!-- MapToXList -->
  <IronMapperGenerateArray>false</IronMapperGenerateArray>       <!-- MapToXArray -->
</PropertyGroup>
```

**For one mapping** (overrides the project value in both directions):

```csharp
[MapTo(typeof(UserDto), GenerateList = false, GenerateArray = false)]
public class User { ... }

[MapFrom(typeof(Order), GenerateInPlace = true)]   // back on, although IronMapperGenerateInPlace is false
public class OrderDto { ... }

public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderSummaryDto>().GenerateMethods(inPlace: false, list: false);
    }
}
```

- A property of the attribute that is not set uses the project value. The getter of `GenerateInPlace`/`GenerateList`/
  `GenerateArray` returns `false` when the property is not set (that is what IntelliSense shows): a limitation of C#
  attributes, not the behavior; the generator reads whether the property is set.
- `GenerateMethods(inPlace, list, array)` is an extension method in `IronMapper.Configuration`; `null` or an omitted
  argument uses the project value. The arguments must be constants (`true`, `false`, `null`, a `const`): the generator
  reads them at compile time; anything else is warning
  [`IM0016`](diagnostics.md#im0016--generatemethods-argument-is-not-a-constant) and the project value applies. It applies
  to that mapping, not to the reverse mapping of `ReverseMap()`.
- `MapToX(source)` is always generated: nested collection properties call it for each element, so they keep working when
  the element mapping has no `MapToXList`/`MapToXArray`.
- Turning overloads off does not change the names of the remaining methods or the collision rules
  ([Method names and collisions](#method-names-and-collisions)).
- **`IMapper`** keeps mapping everything: `Map<UserDto>(user)`, `Map<List<UserDto>>(users)`, `Map<UserDto[]>(users)`,
  `MapCollection` and `Map(source, destination)` work for a mapping without the public overloads, with the same results.
  Without `MapToXList`/`MapToXArray` the registry fills the list or array itself, calling `MapToX` once per element
  through a delegate (`IMapper` calls delegates anyway; the extension methods are not involved).
- **In-place:** turning it off removes only the public method. Its body stays as a private method that `IMapper` calls for
  `Map(source, destination)`, so it hardly changes the amount of generated code or the coverage. The private in-place
  `MapToXCore(source, destination)` stays too: the in-place overloads of other mappings call it to update an existing
  nested object of this pair ([In-place update of nested objects](advanced.md#in-place-update-of-nested-objects)). For coverage, use
  `IronMapperExcludeFromCodeCoverage` (below).

### Attributes of the generated class

The generated class is marked `[GeneratedCode("IronMapper", "<package version>")]`, so code analyzers and tools treat it
as generated. With

```xml
<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>
```

it is also marked `[ExcludeFromCodeCoverage]` (coverlet, the Visual Studio code coverage and others skip it), `false` by
default. Both attributes are declared once on the class and apply to all its parts: the mapping methods, the
registration for `IMapper` and the converter cache. They are emitted only when the target framework has them (both exist
in .NET Standard 2.0 and later). The mapping code is still checked by your tests: the attribute only removes it from the
coverage percentage.

**Coverage tools that exclude `[GeneratedCode]`.** coverlet with `ExcludeByAttribute=GeneratedCodeAttribute` (in
`coverlet.runsettings`, `<ExcludeByAttribute>` of `coverlet.msbuild` or `--exclude-by-attribute`) and other tools set up
the same way leave the generated mappings out of the report from 2.0.0, also without
`IronMapperExcludeFromCodeCoverage`. To measure them, leave `[GeneratedCode]` off:

```xml
<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>
```

`true` by default; only `false` (case-insensitive) turns it off. It does not affect `IronMapperExcludeFromCodeCoverage`.

---

## Unmapped properties in profiles

For `[MapTo]`/`[MapFrom]` an unmatched destination property is warning
[`IM0001`](diagnostics.md#im0001--unmapped-destination-property). For profile (`CreateMap`) mappings it is off by
default, as in 1.1 (an unmatched property keeps its default value without a warning; issue
[#22](https://github.com/iron-mapper/IronMapper/issues/22)), and turned on with an MSBuild property (from 2.0.0):

```xml
<PropertyGroup>
  <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>
</PropertyGroup>
```

Put it in the project or in `Directory.Build.props`. The package passes it to the generator
(`CompilerVisibleProperty` in `buildTransitive/IronMapper.props`); any value other than `true` (case-insensitive)
keeps it off. It is planned to be on by default in the next major version.

The warning points to the `CreateMap` call. A destination property counts as mapped when:

- `ForMember` configures it: `opt.MapFrom(...)`, `opt.Ignore()`, `opt.UseConverter<T>()` (also the standalone
  `.Ignore(d => d.X)`);
- `IncludeMembers` or a source property with the same name (case-insensitive) provides it;
- it has `[Ignore]` (as in attribute mappings).

A mapping with `ConvertUsing<T>()` or `ConvertUsing(src => ...)` gets no IM0001: the converter creates the whole
destination. The reverse mapping of `ReverseMap()` is not checked: it maps by name only and `ForMember` does not apply
to it (AutoMapper does not validate reverse maps either; they use `MemberList.None`). Declare it with its own
`CreateMap` to have it checked. The code fix "Ignore '...' in this mapping (ForMember)" adds
`.ForMember(dest => dest.X, opt => opt.Ignore())` after `CreateMap`.

### Severity

There is no IronMapper property for the severity: use the standard settings for any diagnostic. In `.editorconfig`
(applies to the files it covers) or a `.globalconfig`:

```ini
[*.cs]
dotnet_diagnostic.IM0001.severity = error
```

`<WarningsAsErrors>$(WarningsAsErrors);IM0001</WarningsAsErrors>` and `<NoWarn>` in the project file work too. The
severity applies to attribute and profile mappings alike (it is one diagnostic ID).

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

The type in `typeof(...)` must be a class, struct or record; a generic type must be closed (`typeof(Box<int>)`). For an
open generic type (`typeof(Box<>)`) or an array the mapping is not generated and
[`IM0003`](diagnostics.md#im0003--type-in-maptomapfrom-cannot-be-mapped) is reported. A type that does not resolve
(`typeof(Missing)`) is reported by the compiler (`CS0246`) at the attribute, and that mapping is skipped, so the
generated code adds no errors of its own; the same applies to `[MapFrom]`, `CreateMap<,>` and converter types.

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

Applied to a **source property** when the source and destination property types differ and a custom conversion is needed. The generated code creates the converter itself, so it needs a public or internal parameterless constructor (otherwise [`IM0015`](diagnostics.md#im0015--converter-cannot-be-created-by-generated-code)); a converter without state is created once and reused, see [Converter instances](#converter-instances).

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

For a destination created by a constructor (a positional record, no parameterless constructor; from 2.1.0), the
`ForMember` calls below configure the constructor parameter with the same name as the property (ignoring case);
`Ignore()` means its default value. See [Constructors and positional records](advanced.md#constructors-and-positional-records).

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

Applies a type converter to a single property. The converter is created by the generated code, like `[MapConverter]` (see [Converter instances](#converter-instances)). Runtime test: `ConverterCacheTests.UseConverter_StatelessCachedAndStatefulNew`.

```csharp
CreateMap<Product, ProductDto>()
    .ForMember(d => d.Price, opt => opt.UseConverter<DecimalToStringConverter>());
```

---

### `ConvertUsing<TConverter>()`

Replaces the entire object initializer with a single converter call. Not applied by the in-place overload `source.MapToX(existing)`. The converter is created by the generated code, like `[MapConverter]` (see [Converter instances](#converter-instances)). Runtime test: `ConverterCacheTests.ConvertUsing_StatelessCachedAndStatefulNew`.

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
`IronMapper.Generated.GeneratedMappers_{AssemblyName}` using the semantic model:

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
| `private` or `protected` members or types, e.g. a `private static` helper of the profile | [`IM0011`](diagnostics.md#im0011--profile-lambda-uses-a-member-that-generated-code-cannot-access) | The code runs in the generated class, a different class, where private and protected members are not visible. Make the member `internal`: internal members are visible to all code in the same assembly, including the generated class. |
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

### Converter instances

The generated code creates converters itself; it never resolves them from DI. How depends on the converter:

- **Without state — one instance for all calls.** A class with a public parameterless constructor, no instance fields
  anywhere in its class hierarchy except `object` (auto-property backing fields and field-like events count as fields),
  no finalizer, and constructors that run no code (implicit, or an empty body whose `base()`/`this()` call has no
  arguments). The instance is created on first use, once per converter type for the assembly (`ConverterCache<T>` in
  `GeneratedMappers.g.cs`); the runtime runs that initialization once even when several threads map at the same time.
  Static fields do not count as state.
- **Anything else — a new instance on every call**, as up to 1.1.4: a converter with instance fields (in the class or in
  a base class), a constructor with code or arguments in its `base(...)` call, a primary constructor with parameters, a
  struct, a non-public parameterless constructor (`internal`, or only one with optional parameters), and any class whose
  hierarchy contains a type from another assembly (the generator cannot see its private fields or constructor bodies).
- **Cannot be created — the mapping is not generated** and
  [`IM0015`](diagnostics.md#im0015--converter-cannot-be-created-by-generated-code) is reported: no parameterless
  constructor (only constructors with parameters, e.g. dependencies), a `private`/`protected` one, an abstract class, an
  interface, a static class, an open generic type, or a converter type the generated class cannot access. Other mappings
  of the project are generated.

A cached converter must not rely on being new: it is the same object for every mapping and every thread. Since it has no
fields, this only matters if it keeps state elsewhere, for example in a static field. Tests:
`tests/IronMapper.Tests/ConverterCacheTests.cs` (one instance, a new instance per call with a field in the class, in a
base class or an auto-property, a constructor with code, first use from 16 threads).

### `AddConverter<T>()` does not affect mapping

`IronMapperOptions.AddConverter<T>()` registers the converter type in the DI container as a transient service, and that
is all it does: the generated code does not resolve converters from DI (see [Converter instances](#converter-instances)),
so a converter registered this way, with its dependencies, is **not** the one used for mapping. A converter with
constructors that all take parameters gets `IM0015` whether it is registered or not. `AddProfilesFromAssembly` and `AddProfile<T>()`
register the generated mappings of the assembly for `IMapper`; the profiles themselves are not added to the container
(up to 1.1.4 they were, as singletons).

```csharp
services.AddIronMapper(opt =>
{
    opt.AddProfilesFromAssembly(typeof(Program).Assembly);
    opt.AddConverter<DateToStringConverter>();
});
```

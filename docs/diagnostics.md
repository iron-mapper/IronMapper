# Compiler Diagnostics

> 📖 **[Русская версия](diagnostics.ru.md)**

IronMapper's source generator reports diagnostics with IDs `IM0001`–`IM0023`. They are declared in
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Current status** (`main`, unreleased 2.1.0):

| ID | Severity | Title | Reported? |
|---|---|---|---|
| IM0001 | Warning | Unmapped destination property | Yes — `[MapTo]`/`[MapFrom]`; profiles with `IronMapperReportUnmappedInProfiles` |
| IM0002 | Error | Property types are incompatible or have no mapping | Yes — attributes and profiles (from 2.1.0) |
| IM0003 | Error | Type in `[MapTo]`/`[MapFrom]` cannot be mapped | Yes — open generic types and arrays; an unresolved type is reported by the compiler only |
| IM0004 | Warning | Nested object mapping closes a cycle | Yes — cycles through an automatically mapped nested object (from 2.1.0) |
| IM0005 | Warning | Empty MappingProfile | Yes |
| IM0006 | Error | `[MapProperty]` destination property not found | Yes |
| IM0007 | Warning | Duplicate mapping definition | Yes — attributes and profiles |
| IM0008 | Error | Abstract or interface destination type | Yes — attributes and profiles |
| IM0009 | Error | Constructor parameter not mapped | Yes — attributes and profiles (an error from 2.1.0; up to 2.0.0 a warning for records in `[MapTo]`/`[MapFrom]`) |
| IM0010 | Error | Mapping type is not accessible from generated code | Yes — attributes and profiles |
| IM0011 | Error | Profile lambda uses a member that generated code cannot access | Yes — profiles |
| IM0012 | Error | Profile lambda captures state | Yes — profiles |
| IM0013 | Warning | Mapping method name qualified to avoid a collision | Yes — attributes and profiles |
| IM0014 | Error | Mapping method name collision cannot be resolved | Yes — attributes and profiles |
| IM0015 | Error | Converter cannot be created by generated code | Yes — `[MapConverter]`, `UseConverter`, `ConvertUsing<T>` |
| IM0016 | Warning | `GenerateMethods` argument is not a constant | Yes — profiles |
| IM0017 | Error | Constructor of the destination is ambiguous | Yes — attributes and profiles (from 2.1.0) |
| IM0018 | Error | Destination has no accessible constructor | Yes — attributes and profiles (from 2.1.0) |
| IM0019 | Error | `ForCtorParam` names no parameter | Yes — profiles (from 2.1.0) |
| IM0020 | Error | Factory of `ConstructWith` cannot be used | Yes — profiles (from 2.1.0) |
| IM0021 | Warning | Init property cannot be set after the factory | Yes — profiles (from 2.1.0) |
| IM0022 | Error | Converter of `ConvertUsing` cannot be used | Yes — profiles (from 2.1.0) |
| IM0023 | Warning | Nullable value mapped to a non-nullable member | Yes — attributes and profiles (from 2.1.0) |

Each diagnostic points to a place in your code (**Points to** below): the IDE underlines it and the build output
shows its file and line. The severity is set the standard way, in `.editorconfig` (or a `.globalconfig`):

```ini
[*.cs]
dotnet_diagnostic.IM0001.severity = error   # or warning, suggestion, none
```

`<NoWarn>` and `<WarningsAsErrors>` in the project file work as well. Up to 1.1.4 every diagnostic was reported
without a location (only in the Error List), so neither the per-file `.editorconfig` severities nor the code fixes
applied to them.

When a problem has no working diagnostic, the build fails with a regular C# error inside the generated `*.g.cs` file
instead. Up to 2.0.0 that was the case for a positional record as the destination (`CS7036`); from 2.1.0 it is created by
its constructor ([Constructors and positional records](advanced.md#constructors-and-positional-records)), with
`IM0009`, `IM0017` or `IM0018` when no constructor can be called; a factory of `ConstructWith` that the generated code
cannot call is `IM0020`.

A type that does not resolve (`typeof(Missing)` in `[MapTo]`/`[MapFrom]`, a type argument of `CreateMap`, a converter
type) is reported by the compiler at your code (`CS0246`, `CS0234`, `CS0122`, …). The generator skips that mapping, so
there is no second error inside a generated file; other mappings are generated.

---

## IM0001 — Unmapped destination property

**Severity:** Warning

**Points to:** the destination property (its name), when it is declared in your project: the code fix adds `[Ignore]` there. When the destination type comes from another assembly, the `[MapTo]`/`[MapFrom]` attribute. In a profile mapping, the `CreateMap` call (the code fix adds `.ForMember(dest => dest.X, opt => opt.Ignore())`).

**Message:** `Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value`

**Cause:** A public settable property of the destination type has no source property with the same name
(case-insensitive) and no `[MapProperty]` pointing to it.

**Reported for:** `[MapTo]` / `[MapFrom]` mappings. For `MappingProfile` mappings only with
`<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` (off by default, planned to be on by default
in the next major version; see [Unmapped properties in profiles](configuration.md#unmapped-properties-in-profiles)). In a profile a
destination property counts as mapped when `ForMember` configures it (`MapFrom`, `Ignore`, `UseConverter`), when
`IncludeMembers` or a source property with the same name provides it, or when it has `[Ignore]`. A mapping with
`ConvertUsing` gets no IM0001 (the converter creates the whole object). The reverse mapping of `ReverseMap()` is not
checked: it maps by name only and `ForMember` does not apply to it (AutoMapper does not validate reverse maps either;
they use `MemberList.None`); declare it with `CreateMap` to have it checked.

**Fix:**
- Add the matching property to the source type.
- Use `[MapProperty("DestPropertyName")]` on a source property.
- Put `[Ignore]` on the destination property to suppress the warning (attribute mappings).
- In a profile: `.ForMember(dest => dest.X, opt => opt.Ignore())` or `opt.MapFrom(...)`.
- Code fixes: "Add [Ignore] to '...'" (attribute mappings; also adds `using IronMapper.Attributes;` when needed, and
  `[property: Ignore]` on a positional record parameter) and "Ignore '...' in this mapping (ForMember)" (profiles).
  Both support Fix All; covered by `tests/IronMapper.CodeFixes.Tests`.

---

## IM0002 — Property types are incompatible or have no mapping

**Severity:** Error (from 2.1.0; up to 2.0.0 declared, never reported)

**Points to:** the destination property (`[MapTo]`/`[MapFrom]`; the attribute when the destination type is in another
assembly), the `CreateMap` call (a profile, matched by name or `IncludeMembers`), the `ReverseMap()` call, or the
expression of `MapFrom`.

**Message:** `Property '{0}' of '{1}' is not mapped: {2}` — `{2}` names the source and destination types and says what
to add, for example:

- `'Order.Count' is 'int?', which cannot be assigned to 'int'. Add [MapConverter(typeof(...))] (an ITypeConverter<int?, int>) to the source property, map it in a profile with ForMember(..., o => o.MapFrom(...)), or mark the destination property [Ignore].`
- `the elements of 'Order.Lines' are 'App.Line' and the elements of the destination are 'App.LineDto', and there is no mapping from 'App.Line' to 'App.LineDto'. Add [MapTo(typeof(App.LineDto))] to 'App.Line', [MapFrom(typeof(App.Line))] to 'App.LineDto', or CreateMap<App.Line, App.LineDto>() in a profile.`
- `'Customer.Address' is 'App.Address' and the destination is 'App.AddressDto', and there is no mapping from 'App.Address' to 'App.AddressDto'. Add [MapTo(typeof(App.AddressDto))] to 'App.Address', [MapFrom(typeof(App.Address))] to 'App.AddressDto', or CreateMap<App.Address, App.AddressDto>() in a profile.`
- `'Customer.Address' is 'App.Address?', which can be null, and the destination is 'App.AddressDto', which cannot. Declare the destination property nullable or the source property non-nullable, or mark the destination property [Ignore].`
- `the MapFrom expression is 'int?', which cannot be assigned to 'int'. …`

**Cause:** the generated assignment `destination.X = source.X` would not compile:

- the types have no implicit conversion (`int?` → `int`, `long` → `int`, `string` → `Guid`, an enum → another enum,
  `List<string>` → `string[]`); checked like the compiler does (`Compilation.ClassifyConversion`), so everything that
  converts implicitly is accepted (`int` → `long`, `int` → `int?`, a derived type → its base type, user-defined
  implicit operators, `dynamic`). From 2.1.0 a mapping of the pair converts such a value
  (`CreateMap<string, Guid>().ConvertUsing(Guid.Parse)`, [Value conversions](advanced.md#value-conversions-pair-mappings));
  the diagnostic is then reported only when no mapping of the pair is generated (for `Guid?` → `UserId?` also not the
  one of `Guid` → `UserId`), with the same message;
- a nested object of another type (`Address` → `AddressDto`) whose type pair has no mapping: none in the project (also
  through a base type or an interface of the source type), and no accessible `MapTo…` extension method of a referenced
  assembly or a hand-written one. With a mapping the nested object is mapped by it (from 2.1.0,
  [Nested objects](advanced.md#nested-objects));
- a nested object declared nullable in the source (`Address?`) and non-nullable in the destination (`AddressDto`): the
  generated code does not turn `null` into `null!`;
- a collection whose element types differ and no mapping maps them: none in the project (also through a base type or
  an interface of the source element), and no accessible `MapTo…` extension method of a referenced assembly or a
  hand-written one that the generated call binds to;
- a `MapFrom` expression of another type than the destination property.

A constructor or factory parameter is checked the same way: a value that a pair mapping could convert is `IM0002`
when there is none (from 2.1.0; before, `IM0009`), and the argument is `default!`, so the generated file compiles.

Up to 2.0.0 these failed inside the generated file (`CS0029`, `CS0266`, `CS1061`). Now the property is not generated
(the destination keeps its default value), the rest of the mapping is, and the build fails with `IM0002` at your code.
A mapping with `ConvertUsing` is checked too: `ConvertUsing` replaces `MapToX(source)`, but the in-place overload still
assigns the properties.

**Fix:** convert the value (`[MapConverter]`, `opt.UseConverter<T>()`, a `MapFrom` that converts), add the missing
mapping (for a value, a mapping of the pair: `CreateMap<Guid, UserId>().ConvertUsing(UserId.From)`), or ignore the property (`[Ignore]`, `opt.Ignore()`). For `ReverseMap()`, declare the reverse mapping with
`CreateMap` and configure the property there.

**Code fix** (a nested object or a collection element without a mapping): adds the mapping of the pair —
`[MapTo(typeof(AddressDto))]` on the source type of an attribute mapping (`[MapFrom(typeof(Address))]` on the destination
type when the source type is not in your code), `CreateMap<Address, AddressDto>();` after the statement of the
`CreateMap`/`ReverseMap()` call in a profile. The other cases have no code fix, a value without a pair mapping neither
(`[MapTo]` cannot be added to `Guid`, and a property-by-property mapping would not convert it).

---

## IM0003 — Type in [MapTo]/[MapFrom] cannot be mapped

**Severity:** Error

**Points to:** the `[MapTo]`/`[MapFrom]` attribute.

**Message:** `Mapping for [{0}(typeof({1}))] on '{2}' was not generated: {3}`

**Cause:** the type in `typeof(...)` compiles but is not a type that can be mapped: an open generic type
(`typeof(Box<>)`) or an array (`typeof(Item[])`). The compiler accepts both; before this diagnostic the mapping was
dropped without any message, and the first error was `CS1061` at the call of the missing `MapTo...` method.

**Fix:** use a class, struct or record; close the generic type (`typeof(Box<int>)`). To map collections, map the
element type: `MapTo{Name}List`/`MapTo{Name}Array` are generated for every mapping. Other mappings are generated.

**Not reported for a type that does not resolve** (`typeof(Missing)`, an inaccessible or ambiguous type). The
compiler reports it at the `typeof` (`CS0246`, `CS0234`, `CS0122`, `CS0104`), and the generator skips that mapping, so
this is the only error you see. IM0003 would only repeat it (and, until diagnostics get a source location, without
pointing to the line). Up to 1.1.4 the generator still emitted the mapping, which added `CS0400`/`CS0246` errors
inside the generated file.

---

## IM0004 — Nested object mapping closes a cycle

**Severity:** Warning (from 2.1.0; up to 2.0.0 declared as an error, never reported)

**Points to:** the destination property of the nested object (`[MapTo]`/`[MapFrom]`), or the `CreateMap`/`ReverseMap()`
call (a profile). Reported at every automatically mapped nested object of the cycle.

**Message:** `Property '{0}' of '{1}' is mapped automatically and closes a cycle of mappings: {2}. The mapping recurses without end only if the objects form a cycle at run time; break the cycle with Ignore or MapFrom on a property of the chain, or suppress IM0004 for models that are always finite (#pragma warning disable IM0004, or dotnet_diagnostic.IM0004.severity = none in .editorconfig).`
— `{2}` is the chain of mappings, for example
`App.Order → App.OrderDto, Customer: App.Customer → App.CustomerDto, Orders (elements): App.Order → App.OrderDto`.

**Cause:** a nested object mapped automatically ([Nested objects](advanced.md#nested-objects)) leads back to its own
mapping, through other nested objects or collection elements: `Employee.Manager` of type `Employee`,
`Order.Customer.Orders`. A cycle of types is not an endless recursion by itself: a manager of a manager ends with
`null` and is mapped. The mapping recurses forever, and fails with `StackOverflowException`, only when the data has a
cycle (an order whose customer lists that order). The generated code does not track mapped objects.

**Not reported:** cycles only through collections (a tree such as `Category.Children`, which works since 2.0.0), and
cycles broken by a property configured explicitly (`MapFrom`, `Ignore`, a converter) or by a mapping with
`ConvertUsing` (it calls the converter, not the mappings of its properties). No build that compiles with 2.0.0 gets
IM0004: a nested object of another type did not compile there.

**Fix:** break the cycle with `Ignore` or `MapFrom` on a property of the chain (`ForMember(d => d.Customer, o => o.Ignore())`,
`[Ignore]`), or suppress the warning where the data is always finite: `#pragma warning disable IM0004` around the
property (or the `CreateMap` call), `dotnet_diagnostic.IM0004.severity = none` in `.editorconfig`, or `<NoWarn>`.

---

## IM0005 — Empty MappingProfile

**Severity:** Warning

**Points to:** the name of the profile class.

**Message:** `MappingProfile '{0}' has no mappings defined`

**Cause:** a class deriving from `MappingProfile` has no `CreateMap<TSource, TDest>()` call chain in the
statements of its constructor(s).

**Fix:** add at least one `CreateMap<TSource, TDest>()` call, or remove the empty profile class.

---

## IM0006 — `[MapProperty]` destination property not found

**Severity:** Error

**Points to:** the `[MapProperty]` attribute (the code fix replaces the name with the closest destination property).

**Message:** `[MapProperty] destination '{0}' does not exist in type '{1}'`

**Cause:** the name passed to `[MapProperty]` does not match any public settable property of the destination
type. The comparison is **case-insensitive**.

**Fix:** correct the name, or add the property to the destination type. The code fix "Did you mean '...'?" offers the
destination properties of the mapped type within Levenshtein distance 2 (case-insensitive); covered by
`tests/IronMapper.CodeFixes.Tests`.

---

## IM0007 — Duplicate mapping definition

**Severity:** Warning

**Points to:** the definition that is not generated (the one to remove); the other definitions are additional locations.

**Message:** `Mapping from '{0}' to '{1}' is defined more than once: {2}. Only the definition from {3} is generated; remove the others.`

**Cause:** the same source/destination pair is defined more than once: `[MapTo]` and `CreateMap`, `[MapTo]` and
`[MapFrom]`, two profiles, or `CreateMap` and the `ReverseMap()` of another map.

**Behavior:** the pair is generated once. Precedence: `CreateMap` in a profile, then `ReverseMap()`, then `[MapTo]`,
then `[MapFrom]`; among definitions of the same kind, the profile (or type) whose full name sorts first, then the
first call in the constructor. All other mappings of the project are generated. See
[Method names and collisions](configuration.md#same-type-pair-defined-twice).

Before this was implemented, a duplicate pair made the generator fail with `CS8785` ("HintName ... must be unique"),
and no mapping code was generated for the project at all.

**Fix:** keep one definition.

---

## IM0008 — Abstract or interface destination type

**Severity:** Error

**Points to:** the `[MapTo]`/`[MapFrom]` attribute or the `CreateMap` call.

**Message:** `Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination`

**Cause:** the destination type of `[MapTo]`, `[MapFrom]` or `CreateMap` is abstract or an interface.

**Fix:** map to a concrete type. For profile mappings, `ConvertUsing` cannot be used as a workaround,
because the generator reports IM0008 before reading the rest of the mapping.

---

## IM0009 — Constructor parameter not mapped

**Severity:** Error (from 2.1.0; up to 2.0.0 a warning, see below)

**Points to:** in attribute mappings, the constructor parameter (its name) when the destination type is declared in
your project, otherwise the attribute; in profiles, the `CreateMap` or `ReverseMap()` call.

**Message:** `Mapping to '{1}' was not generated: parameter '{0}' of {3} gets no value: {2}` (`{3}` is `the constructor`,
`factory 'Team.Restore'` or `the lambda of ConstructWith`)

**Cause:** the destination has no accessible parameterless constructor, so it is created by a constructor
([Constructors and positional records](advanced.md#constructors-and-positional-records)), and no accessible
constructor can be called: a parameter of the one that comes closest (the most parameters mapped from the source)
gets no value. Or the factory of `ConstructWith` has such a parameter
([Factories](advanced.md#factories-constructwith)). The last part of the message says why:

- the source has no property with the parameter's name (ignoring case), and the parameter has no default value;
- the source value cannot be passed (the reasons of [`IM0002`](#im0002--property-types-are-incompatible-or-have-no-mapping):
  a nullable nested object or value for a non-nullable parameter); a default value does not help then. A value of
  another type that a pair mapping could convert counts as mapped (from 2.1.0): without the mapping it is `IM0002`. A
  constructor that needs such a conversion is chosen only when no other one can be called;
- the parameter is ignored (`Ignore()` of the property with the same name, `[Ignore]`) and has no default value.

**Behavior:** the mapping is not generated; other mappings in the project are.

**Fix:** add the source property, or map another one to the parameter (`[MapProperty("parameterName")]` on the source
property; in a profile `ForMember(d => d.Property, o => o.MapFrom(...))` of the property with the same name, or
`ForCtorParam("parameterName", o => o.MapFrom(...))` for a parameter without one), convert the value (a converter,
`MapFrom`), give the parameter a default value, or choose another constructor or a factory with `ConstructWith`.

**Up to 2.0.0** `IM0009` was a warning for records in attribute mappings, reported for every declared constructor,
while the generated object initializer did not compile anyway when the record had no parameterless constructor
(`CS7036`). A record with a parameterless constructor is created with an object initializer, and the parameters of its
other constructors are no longer checked.

---

## IM0010 — Mapping type is not accessible from generated code

**Severity:** Error

**Points to:** the `[MapTo]`/`[MapFrom]` attribute or the `CreateMap` call.

**Message:** `Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class in namespace IronMapper.Generated cannot access it. Make the type and all types that contain it public or internal.`

**Cause:** the source or destination type, one of the types containing it, or one of its generic type arguments is a
`private`, `protected` or `private protected` nested type. Generated methods live in the top-level class
`IronMapper.Generated.GeneratedMappers_{AssemblyName}` and cannot reference such types.

**Reported for:** `[MapTo]` / `[MapFrom]` and `MappingProfile` mappings. Only the affected mapping is skipped; all other
mappings in the project are still generated.

**Fix:** make the type (and every type that contains it) `public` or `internal`. Internal types are supported; the
generated methods are then `internal` (see [Generated method accessibility](configuration.md#generated-method-accessibility)).

---

## IM0011 — Profile lambda uses a member that generated code cannot access

**Severity:** Error

**Points to:** the member or type in the lambda.

**Message:** `A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class in namespace IronMapper.Generated, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.`

**Cause:** a lambda in `MapFrom`, `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` or `AddTransformer` references a
`private`, `protected` or `private protected` member or type (or a member of such a type) — typically a
`private static` helper method or constant of the profile. Lambdas are rewritten into the generated class, not called
on the profile, so such members are not visible there.

**Fix:** make the member (or the type that contains it) `internal`. See [Profile lambdas](configuration.md#profile-lambdas).

The affected mapping is not generated (for `AddTransformer`, the transformer is dropped); other mappings are.

---

## IM0012 — Profile lambda captures state

**Severity:** Error

**Points to:** the member, variable or `this`/`base` in the lambda.

**Message:** `A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.`

**Cause:** the lambda uses an instance field, property or method of the profile, `this`/`base`, or a local variable,
parameter or local function of the constructor. None of these exist when the generated code runs.

**Fix:** make the member `static` (or `const`), or compute the value from the source object.
See [Profile lambdas](configuration.md#profile-lambdas).

The affected mapping is not generated (for `AddTransformer`, the transformer is dropped); other mappings are.

---

## IM0013 — Mapping method name qualified to avoid a collision

**Severity:** Warning

**Points to:** the definition of the mapping that got the qualified name (attribute, `CreateMap` or `ReverseMap()` call).

**Message:** `Several mappings from '{0}' have a destination type named '{1}', so the mapping to '{2}' is generated as '{3}' instead of '{4}'. Call it as source.{3}() or IronMapper.Generated.{5}.{3}(source); the collection methods are {3}List and {3}Array.`

**Cause:** one source type is mapped to several destination types with the same simple name (different namespaces,
nested in different types, or different type arguments). `MapTo{Name}` would be declared twice (`CS0111`), so these
mappings get qualified names, e.g. `MapToAdminUserDto` and `MapToPublicUserDto`, or `MapToBoxOfInt32`. The rule is
described in [Method names and collisions](configuration.md#one-source-type-several-destination-types-with-the-same-simple-name).

**Fix:** call the method by the name in the message, as an extension method or on the generated class of the project
(`{5}` in the message, `GeneratedMappers_{AssemblyName}`), or rename one of the destination types to get `MapTo{Name}` back.
If the qualified names are intended, suppress the warning with `<NoWarn>$(NoWarn);IM0013</NoWarn>`.

---

## IM0014 — Mapping method name collision cannot be resolved

**Severity:** Error

**Points to:** the definition of the mapping that was not generated.

**Message:** `Mapping from '{0}' to '{1}' was not generated: another mapping from '{0}' has a destination type named '{2}', and every qualified method name (for example '{3}') is already used by another mapping. Rename one of the destination types.`

**Cause:** the qualified names from IM0013 are all taken by other mappings of the same source type (for example,
`N1.Dto` and `N2.Dto` would become `MapToN1Dto`/`MapToN2Dto`, but types named `N1Dto` and `N2Dto` are mapped too).

**Fix:** rename one of the destination types. Only the mappings of the colliding group are skipped; other mappings are
generated.

---

## IM0015 — Converter cannot be created by generated code

**Severity:** Error

**Points to:** the converter type: the type in `typeof(...)` of `[MapConverter]`, or the type argument of `UseConverter<T>()`/`ConvertUsing<T>()`.

**Message:** `Mapping from '{0}' to '{1}' was not generated: converter '{2}' ({3}) cannot be created with 'new {2}()' because {4}`

**Cause:** the generated code creates converters itself (it does not resolve them from DI), and the converter of
`[MapConverter]`, `UseConverter<T>()` or `ConvertUsing<T>()` cannot be created that way:

- it has no parameterless constructor (for example, only a constructor with dependencies, or a primary constructor
  with parameters);
- its parameterless constructor is `private` or `protected`;
- it is abstract, an interface, a static class or an open generic type (`typeof(Converter<>)`);
- the generated class `IronMapper.Generated.GeneratedMappers_{AssemblyName}` cannot access it (a `private` nested type).

Before this diagnostic the build failed with `CS7036`, `CS1729`, `CS0122` or `CS0144` inside the generated file.

**Fix:** give the converter a public or internal parameterless constructor and make it a non-abstract class or a
struct the generated class can access. A converter that needs services from DI cannot be used yet:
`AddConverter<T>()` does not change how converters are created (see
[Converter instances](configuration.md#converter-instances)). Only the affected mapping is skipped; other mappings are
generated. A property marked `[Ignore]` does not use its converter, so its converter is not checked.

---

## IM0016 — GenerateMethods argument is not a constant

**Severity:** Warning

**Points to:** the argument.

**Message:** `Argument '{0}' of GenerateMethods in MappingProfile '{1}' is not a constant ('{2}'); the generator reads it at compile time, so the value for the assembly applies. Use true, false or null.`

**Cause:** an argument of `GenerateMethods(inPlace, list, array)` in a profile is a variable, a method call or another
expression that is not a compile-time constant. Profiles are read by the source generator, not executed, so the value is
unknown when the code is generated.

**Behavior:** that overload uses the project value (`IronMapperGenerateInPlace`/`List`/`Array`); the mapping and the other
arguments are generated as written. See [Generated overloads](configuration.md#generated-overloads-and-code-coverage).

**Fix:** pass `true`, `false` or `null` (or a `const`).

---

## IM0017 — Constructor of the destination is ambiguous

**Severity:** Error (from 2.1.0)

**Points to:** the `[MapTo]`/`[MapFrom]` attribute or the `CreateMap` call.

**Message:** `Mapping from '{0}' to '{1}' was not generated: the constructors {2} fit equally well (each maps {3} parameter(s) from the source). Choose one in a profile with a lambda that calls it, such as ConstructWith({4}), or remove or change one of the constructors.`
(`{4}` is a lambda with the parameters of the first of them: `(int from, int to) => new Range(from, to)`)

**Cause:** the destination is created by a constructor, and two or more of its constructors can be called with the
same number of parameters mapped from the source and the same number of parameters, for example `Range(int, int)` and
`Range(long, long)` for a source with `int` properties. The order of declaration does not decide: reordering the code
would change the mapping silently.

**Behavior:** the mapping is not generated; other mappings in the project are.

**Fix:** choose the constructor in a profile, `CreateMap<RangeRow, Range>().ConstructWith((int from, int to) => new Range(from, to))`
([Factories](advanced.md#factories-constructwith)), or remove or change one of the constructors. An attribute in the
destination type is deliberately not offered: domain types should not depend on IronMapper.

---

## IM0018 — Destination has no accessible constructor

**Severity:** Error (from 2.1.0)

**Points to:** the `[MapTo]`/`[MapFrom]` attribute or the `CreateMap` call.

**Message:** `Mapping from '{0}' to '{1}' was not generated: '{1}' has no public or internal constructor the generated code can call. Create it with its static factory method in a profile: CreateMap<{2}, {3}>().ConstructWith({3}.Create), where Create is the factory method.`

**Cause:** every constructor of the destination is `private` or `protected` (or has `ref`/`out` parameters), as in a
domain type created by a static factory method (`Project.Restore(...)`). Up to 2.0.0 the build failed inside the
generated file (`CS0122`, `CS7036`).

**Behavior:** the mapping is not generated; other mappings in the project are.

**Fix:** name the factory in a profile, `CreateMap<ProjectRow, Project>().ConstructWith(Project.Restore)`: its parameters
are matched to the source as those of a constructor ([Factories](advanced.md#factories-constructwith)). Or make a
constructor public or internal.

---

## IM0019 — `ForCtorParam` names no parameter

**Severity:** Error (from 2.1.0)

**Points to:** the name in `ForCtorParam`.

**Message:** `Mapping from '{0}' to '{1}' was not generated: ForCtorParam({2}) {3}`, for example
`ForCtorParam("nmae") names no parameter of the constructor of 'Author' that the generator chose. Its parameters: name.`

**Cause:** `ForCtorParam` configures a parameter of the constructor the generator chooses, or of the factory of
`ConstructWith` ([Parameters without a property](advanced.md#parameters-without-a-property-forctorparam)); names are
compared ignoring case. The name is none of its parameters (a typo, a renamed parameter), the destination is created
with an object initializer (it has an accessible parameterless constructor) or by `ConvertUsing`/`ConstructUsing`, which
match no parameters, or the name is not a constant.

**Behavior:** the mapping is not generated (a setting that would be ignored silently); other mappings in the project
are.

**Fix:** use a name of the parameters listed in the message (a literal, or `nameof` of something with the same name);
for a property of an object initializer use `ForMember`.

---

## IM0020 — Factory of `ConstructWith` cannot be used

**Severity:** Error (from 2.1.0)

**Points to:** the argument of `ConstructWith` (or the call that conflicts, or the argument of `ConstructUsing`).

**Message:** `Mapping from '{0}' to '{1}' was not generated: {2}`; `{2}` says what is wrong and what to write.

**Cause:** the generated code cannot call what `ConstructWith` names
([Factories](advanced.md#factories-constructwith)):

- an instance method or a local function (the generated class has no instance to call it on);
- a private or protected method, or a method of a private type;
- the method returns another type than the destination or a type derived from it, or a nullable one (`Team?`);
- a `ref`/`out` parameter;
- a method group of an overloaded method, or a lambda without parameter types: no delegate type can be inferred (the
  compiler reports `CS8917` at the same place);
- not a method group or a lambda written in the call (a delegate in a variable or a field cannot be read at compile
  time);
- `ConstructWith` together with `ConstructUsing` or `ConvertUsing`, or twice.

**Behavior:** the mapping is not generated; other mappings in the project are.

**Fix:** pass a public or internal static method, or a lambda with typed parameters that calls the factory or the
constructor: `ConstructWith((Guid id, string name) => Team.Restore(id, name))`.

---

## IM0021 — Init property cannot be set after the factory

**Severity:** Warning (from 2.1.0)

**Points to:** the `CreateMap` call.

**Message:** `Property '{1}.{0}' is not mapped: it is init-only, and the destination is created by {2}, so it cannot be set after the call. Pass the value to the factory, or ignore the property with ForMember(d => d.{0}, o => o.Ignore()).`

**Cause:** the destination is created by the factory of `ConstructWith`, and the source has a value for an `init`
property that no parameter of the factory sets. An `init` property can be set only in an object initializer of `new`;
after the call the generated code assigns the properties with a public `set`.

**Behavior:** the mapping is generated without that property.

**Fix:** add a parameter for it to the factory (or set it in the lambda of `ConstructWith`), or ignore it with
`ForMember(d => d.Property, o => o.Ignore())`.

---

## IM0022 — Converter of `ConvertUsing` cannot be used

**Severity:** Error (from 2.1.0)

**Points to:** the argument of `ConvertUsing` (or of `ReverseMap(converter)`, the converter of the reverse mapping).

**Message:** `Mapping from '{0}' to '{1}' was not generated: {2}`; `{2}` says what is wrong and what to write.

**Cause:** the generated method calls the converter with the source, so it must be a lambda (`ConvertUsing(s => …)`) or a
static method group (`ConvertUsing(UserId.From)`) written in the call
([Value conversions](advanced.md#value-conversions-pair-mappings)). Reported for:

- an instance method or a local function (the generated class has no instance to call it on);
- a private or protected method, or a method of a private type;
- a delegate in a variable, a field or a property: it cannot be read at compile time.

Up to 2.0.0 such an argument, and a static method group too, was silently ignored: the mapping was generated property
by property, without the converter. A static method group is called from 2.1.0.

**Behavior:** the mapping is not generated; other mappings in the project are.

**Fix:** pass a public or internal static method, or a lambda that does not use locals or the profile instance:
`ConvertUsing(v => Converters.ToUserId(v))`.

---

## IM0023 — Nullable value mapped to a non-nullable member

**Severity:** Warning (from 2.1.0; issue [#54](https://github.com/iron-mapper/IronMapper/issues/54))

**Points to:** as `IM0002`: the destination property (attribute mappings), the `CreateMap` or `ReverseMap()` call
(profiles), or the expression of `MapFrom`.

**Message:** `{0} of '{1}' can receive null: {2}` — `{0}` is `Property 'Email'` or `Parameter 'email'`; `{2}` names the
source and the destination and what to write, for example:

- `'UserRow.Email' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Email, o => o.MapFrom(s => s.Email!)) or s.Email ?? a default, declare the destination nullable, or ignore it with ForMember(d => d.Email, o => o.Ignore()).`
- `the elements of 'Order.Lines' are 'App.Line?', and the mapping of the elements (MapToLineDto) takes 'App.Line': a null element throws ArgumentNullException. …`

**Cause:** a value declared nullable (a reference type with `?`) goes where the generated code needs a non-nullable
one:

- a non-nullable destination property or constructor/factory parameter (`string?` → `string`); for a parameter
  without a property of the same name the hint is `ForCtorParam("name", …)`;
- collection elements that narrow the same way, assigned as is (`List<string?>` → `List<string>`; up to now `IM0002`);
- the expression of `MapFrom` (`MapFrom(s => s.Email)`; `s.Email!` and `s.Email ?? ""` are not reported);
- a converter whose `ITypeConverter<string, …>` takes a non-nullable value, or a profile transformer
  (`AddTransformer<string>`) applied to a nullable property;
- the elements of a collection mapped one by one (`List<Line?>` → `List<LineDto>` or `List<LineDto?>`): the element
  mapping takes a non-nullable value and throws `ArgumentNullException` for a `null` element, as documented in 2.0.0.

Not reported when nullable annotations are disabled in the project (oblivious types: nothing narrows), nor for a value
that is mapped through a nested object or a pair mapping (those report `IM0002` for a nullable source and a
non-nullable destination).

**Behavior:** the value is mapped as before: `null` reaches the non-nullable member, a `null` element throws. Up to
2.0.0 the compiler reported the same place inside the generated file (`CS8601` for an assignment, `CS8604` for an
argument, `CS8619`/`CS8620` for collection elements), where it could not be fixed or suppressed per mapping. Now the
generated code marks exactly that value with `!` (only together with this warning; the generator never adds `!`
silently), so the generated file has no nullable warning and the one warning is at your code. IM0023 takes the
configuration of the compiler warning it replaces when it is set for every file (`NoWarn`, `WarningsAsErrors`, a
`.globalconfig`): suppressed (`<NoWarn>CS8601</NoWarn>`), it is not reported; an error
(`<WarningsAsErrors>nullable</WarningsAsErrors>`), it is reported as an error. A build keeps its result.

**Fix:** map a non-null value (`ForMember(d => d.Email, o => o.MapFrom(s => s.Email!))` when it is never `null`, or
`s.Email ?? ""`), declare the destination nullable, or ignore the member. The severity is set the standard way:
`dotnet_diagnostic.IM0023.severity` in `.editorconfig`, `#pragma warning disable IM0023` around the `CreateMap` line,
`<NoWarn>`.

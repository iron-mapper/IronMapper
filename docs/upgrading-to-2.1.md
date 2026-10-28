# Upgrading to IronMapper 2.1

Русская версия: [upgrading-to-2.1.ru.md](upgrading-to-2.1.ru.md)

2.1.0 follows 2.0.0. Fixes of configuration that profiles silently ignored (`ConvertUsing`, `MapFrom`, `When`,
`BeforeMap` and `AfterMap` with a method group, `[Ignore]` on a property) can change what a mapping that built with 2.0.0 does or whether it builds,
nullable warnings move from the generated file to your code, in-place overloads that do nothing are marked `[Obsolete]`,
the in-place overload applies `When` and hooks, `init` properties of mappings with hooks are mapped,
`IronMapperRegistration=false` now also applies to the project that sets it, and `AddConverter<T>()` is `[Obsolete]`. Everything else in
2.1.0 is new functionality or a fix that only affects code that did not build with 2.0.0
([CHANGELOG](../CHANGELOG.md)).

| Change | Who is affected |
|---|---|
| [`ConvertUsing` with a static method group is called](#1-convertusing-with-a-static-method-group-is-called) | Profiles with `ConvertUsing(SomeType.Method)` |
| [`ConvertUsing` that cannot be called is `IM0022`](#2-convertusing-that-cannot-be-called-is-im0022) | Profiles with `ConvertUsing(instance.Method)`, a local function, a private method or a delegate in a variable |
| [Nullable warnings move from the generated file to `IM0023`](#3-nullable-warnings-move-from-the-generated-file-to-im0023) | Projects with nullable annotations whose mappings had `CS8601`/`CS8604` in a `*.g.cs` file |
| [In-place overloads that do nothing are `[Obsolete]`](#4-in-place-overloads-that-do-nothing-are-obsolete) | Calls of `source.MapToX(existing)` for a struct destination, a `ConvertUsing` mapping or a destination with only `init` properties, and `IMapper.Map(source, destination)` for such a pair |
| [`IronMapperRegistration=false` generates no registration](#5-ironmapperregistrationfalse-generates-no-registration) | Projects that set `<IronMapperRegistration>false</IronMapperRegistration>` |
| [`MapFrom`, `When` and hooks with a static method group are called](#6-mapfrom-when-and-hooks-with-a-static-method-group-are-called) | Profiles with `MapFrom(SomeType.Method)` in `ForMember`/`ForCtorParam`, `.When(SomeType.Method)`, `.BeforeMap(SomeType.Method)` or `.AfterMap(SomeType.Method)` |
| [`[Ignore]` applies in profiles](#7-ignore-applies-in-profiles) | Profile mappings between types with `[Ignore]` on a property that the other type also has |
| [`null` source can return `null` (opt-in)](#8-null-source-can-return-null-opt-in) | Projects that set `<IronMapperNullSourceReturnsNull>true</IronMapperNullSourceReturnsNull>` (nothing changes without it) |
| [`init`-only properties in place: nested objects updated, warning `IM0025`](#9-init-only-properties-in-place-nested-objects-updated-warning-im0025) | Mappings with in-place overloads to types that have `init` properties (warning `IM0025`; a build error with `TreatWarningsAsErrors`) |
| [Hooks and `When` in the in-place overload](#10-hooks-and-when-in-the-in-place-overload) | In-place calls (`source.MapToX(existing)`, `IMapper.Map(source, existing)`) of profile mappings with `When`, `BeforeMap` or `AfterMap` |
| [`required` and `init` properties with `BeforeMap`/`AfterMap`](#11-required-and-init-properties-with-beforemapaftermap) | Profile mappings with hooks to types with `init` properties; `required` properties without a value (`IM0026` instead of `CS9035`) |
| [`AddConverter<T>()` is `[Obsolete]`](#12-addconvertert-is-obsolete) | Calls of `opt.AddConverter<T>()` in `AddIronMapper(...)` (warning `CS0618`; a build error with `TreatWarningsAsErrors`) |

To find the first two, search the profiles for `ConvertUsing(` whose argument is not a lambda (`s => …`) and not a type
argument (`ConvertUsing<TConverter>()`); sections 6 and 7 say how to find theirs. For all of them at once, compare the
generated code of 2.0.0 and 2.1.0: build with `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` on each
version and diff `obj/<configuration>/<tfm>/generated/IronMapper.Generator/` (a mapping these fixes change has a different
assignment, or no assignment, of that property).

---

## 1. `ConvertUsing` with a static method group is called

**Was.** Up to 2.0.0 the generator read only a lambda in `ConvertUsing(...)`. A method group was silently ignored: the
mapping was generated property by property, as if there were no `ConvertUsing`.

```csharp
CreateMap<Guid, UserId>().ConvertUsing(UserId.From);
// 2.0.0: return new UserId { };   (UserId.From is never called; a struct gets its default value)
// 2.1.0: return UserId.From(source);
```

**Now.** The method is called with the source.

**What to do.** **The result at run time changes.** Check every such mapping: the method now runs (and its exceptions
reach the caller), and the properties that the property-by-property mapping used to set are no longer set by it. If you
relied on the old result, remove the `ConvertUsing` call.

---

## 2. `ConvertUsing` that cannot be called is `IM0022`

**Was.** An argument that the generated class cannot call — an instance method (`ConvertUsing(converter.Convert)`), a
local function, a private method, a delegate in a variable or a field — was silently ignored, like a method group: the
mapping was generated property by property, without the converter.

**Now.** The build fails with error
[`IM0022`](diagnostics.md#im0022--converter-of-convertusing-cannot-be-used) at the argument, and the mapping is not
generated.

**What to do.** Pass a public or internal static method or a lambda that does not use locals or the profile instance:

```csharp
CreateMap<Guid, UserId>().ConvertUsing(v => UserIds.Create(v));   // or ConvertUsing(UserIds.Create) if it is static
```

---

## 3. Nullable warnings move from the generated file to `IM0023`

**Was.** A nullable source value for a non-nullable destination (`string?` → `string`, nullable collection elements, a
converter or transformer that takes a non-nullable value) compiled with `CS8601`/`CS8604` inside the generated `*.g.cs`
file, where it could not be fixed or suppressed per mapping.

**Now.** The same place is reported once, as warning
[`IM0023`](diagnostics.md#im0023--nullable-value-mapped-to-a-non-nullable-member) at your mapping, and the generated file
has no nullable warning ([#54](https://github.com/iron-mapper/IronMapper/issues/54)). The mapped values do not change.

**What you may see.** A build that was clean stays clean, and one that failed on these warnings still fails, now with
`IM0023`; checked against the published 2.0.0 package:

| Project | 2.0.0 | 2.1.0 |
|---|---|---|
| `<Nullable>enable</Nullable>` | warnings `CS8601`, `CS8604` | warning `IM0023` |
| … with `TreatWarningsAsErrors` | errors `CS8601`, `CS8604` | error `IM0023` |
| `<Nullable>disable</Nullable>` (with or without `TreatWarningsAsErrors`) | no warning | no warning |
| `<NoWarn>CS8601;CS8604</NoWarn>` or a `.globalconfig` with `severity = none`, with `TreatWarningsAsErrors` | no warning | no warning |
| `<WarningsAsErrors>nullable</WarningsAsErrors>` (or `CS8601;CS8604`) | errors `CS8601`, `CS8604` | error `IM0023` |
| `<WarningsNotAsErrors>CS8601;CS8604</WarningsNotAsErrors>` with `TreatWarningsAsErrors` | warnings | **error `IM0023`** |

**What to do.** Fix the mapping as the message says (`MapFrom(s => s.Email!)`, `?? ""`, a nullable destination,
`Ignore()`), or set the severity of `IM0023` the way you set it for the compiler warnings (IM0023 takes the configuration of the compiler warning it replaces when it is set for the whole project: suppressed, it is not reported; an error, it is an error) (in the last case of the table,
add `IM0023` to `WarningsNotAsErrors`). A collection whose elements differ only by `?` (`List<string?>` →
`List<string>`) is now assigned as is with `IM0023`; before 2.1 it did not compile.

---

## 4. In-place overloads that do nothing are `[Obsolete]`

**Was.** Every mapping got the in-place overload `source.MapToX(existing)`, also where it cannot change anything: a
destination that is a value type (a `struct`, a `readonly record struct` value object, `Guid`; the method gets a copy),
a mapping with `ConvertUsing` (the in-place overload does not apply the converter) and a destination whose mapped
properties are all `init` (the in-place overload skips them). The call compiled and silently did nothing, and so did
`IMapper.Map(source, destination)` for such a pair.

**Now.** Removing a public method would break the code that calls it, so in 2.x these overloads stay and are marked
`[Obsolete]` with the reason and what to do; they are removed in 3.0.0. `IMapper.Map(source, destination)` for such a
pair throws `MappingException` with the same reason instead of doing nothing:

```csharp
CreateMap<Guid, UserId>().ConvertUsing(UserId.From);

guid.MapToUserId(existing);
// warning CS0618: 'MapToUserId(Guid, UserId)' is obsolete: 'MapToUserId(source, destination) does nothing: UserId is a
// value type, so the destination is passed by copy and the caller's value does not change. Map to a new object instead
// (destination = source.MapToUserId()), or turn this overload off (...). It will be removed in IronMapper 3.0.0.'

mapper.Map(guid, existing);
// MappingException: No in-place mapping from Guid to UserId: UserId is a value type, ...
```

The mapped values of every other call do not change. A nested object whose mapping is in a referenced project built
with 2.1.0 and has such an overload is assigned a new instance by the in-place update of its parent, as a nested object
of such a mapping of the same project already was (before, the referenced overload was called and kept the old values).

**What you may see.** `CS0618` at each call of such an overload: with `TreatWarningsAsErrors` it is an error.

**What to do.** Map to a new object (`existing = guid.MapToUserId();`), or turn the overload off for the mapping
(`GenerateInPlace = false`, `.GenerateMethods(inPlace: false)`) or the project (`IronMapperGenerateInPlace=false`) if
nothing calls it. To keep a call for now, suppress `CS0618` around it (`#pragma warning disable CS0618`).

---

## 5. `IronMapperRegistration=false` generates no registration

**Was.** `IronMapperRegistration` only set the feature switch `IronMapper.Registration.IsSupported` of an application;
the registration for `IMapper` (`GeneratedMappers.Registration.g.cs`, the module initializer, the private in-place
methods it calls) was generated anyway and removed only by the trimmer. In a library the property had no effect.

**Now.** `<IronMapperRegistration>false</IronMapperRegistration>` set in a project (application or library) also stops
generating the registration for the mappings of that project ([Registration for `IMapper`](configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps)).
The value that the package picks automatically for a trimmed or NativeAOT publish without the DI package goes only into
the feature switch, as before, and changes nothing in the generated code.

**What you may see.** `IMapper` finds no mapping of such a project: the `MappingException` mentions the property.

**What to do.** Remove the property from a project whose mappings `IMapper` must use; keep it where only the generated
`MapTo…` methods are called.

---

## 6. `MapFrom`, `When` and hooks with a static method group are called

**Was.** `MapFrom` has an overload that takes `Func<TSource, object?>`, so a method group compiles. The generator read
only a lambda there; a method group was silently ignored, and the member was mapped as if it had no `ForMember`
([#59](https://github.com/iron-mapper/IronMapper/issues/59)):

```csharp
public static class Helper { public static string Compute(Row r) => r.First + "!"; }

CreateMap<Row, Dto>()
    .ForMember(d => d.Title, o => o.MapFrom(Helper.Compute))   // 2.0.0: Title = source.Title (Compute is never called)
    .ForMember(d => d.Label, o => o.MapFrom(Helper.Compute));  // 2.0.0: Row has no Label — CS1061 in the generated file
// 2.1.0: Title = Helper.Compute(source), Label = Helper.Compute(source), also in place
```

The same with `When`, `BeforeMap` and `AfterMap` (`Func<TSource, bool>`, `Action<TSource, TDest>`):

```csharp
CreateMap<Row, Dto>().When(Rules.IsActive).AfterMap(Audit.Stamp);
// 2.0.0: no condition (every row is mapped), Audit.Stamp is never called
// 2.1.0: if (!Rules.IsActive(source)) return default!; … Audit.Stamp(source, destination);
```

**Now.** A public or internal static method is called with the source (a hook: with the source and the destination).
`MapFrom` does so in a new object and in place (also in `ForCtorParam`); `When` and the hooks only in a new object, as a
lambda there (the in-place overload applies neither). Anything the generated class cannot call — an instance method
(`MapFrom(formats.Title)`), a local function, a private method, a delegate in a variable or a field, an anonymous method
— is error [`IM0024`](diagnostics.md#im0024--method-of-a-profile-setting-cannot-be-used) at the argument, and the mapping
is not generated.

**What to do.** **The result at run time changes.** `MapFrom`: where the source has a property of the member's name, the
method now runs (and its exceptions reach the caller) instead of copying that property. `When`: a source for which the
method returns `false` now gives `null` (`default`) instead of a mapped object. Hooks: the method now runs. Find such
mappings by searching the profiles for these calls whose argument is not a lambda — for example
`(MapFrom|When|BeforeMap|AfterMap)\(\s*[A-Za-z_][\w.<>]*\s*\)` as a regular expression. If you relied on the old
result, remove the call. For `IM0024`, pass a static method or a lambda that does not use locals or the profile
instance.

---

## 7. `[Ignore]` applies in profiles

**Was.** `[Ignore]` on a property worked in `[MapTo]`/`[MapFrom]` mappings. A profile mapping did not read it: on a
destination property it only skipped its `IM0001` and still copied the source property of the same name
([#61](https://github.com/iron-mapper/IronMapper/issues/61)); on a source property it was still read:

```csharp
public class Dto { public string Title { get; set; } = ""; [Ignore] public string Secret { get; set; } = ""; }

public class Row { public string Title { get; set; } = ""; public string Secret { get; set; } = ""; [Ignore] public string Draft { get; set; } = ""; }

CreateMap<Row, Dto>();   // Dto also has Draft
// 2.0.0: Secret = source.Secret, Draft = source.Draft (also in place)
// 2.1.0: neither is set
```

**Now.** A profile mapping leaves such a property out, as an attribute mapping does: on the destination it is not set,
on the source it is not read (also a property of an `IncludeMembers` member). A new object keeps the default value, the
in-place overload (and `IMapper.Map(source, destination)`) keeps the current value, and a constructor parameter of the
same name gets its default value (no default value: `IM0009`). `ForMember` of the destination property (`MapFrom`,
`UseConverter`) wins over `[Ignore]`. With `ReverseMap()` a property with `[Ignore]` on either type is left out of both
directions.

**What to do.** **The result at run time changes:** a property with `[Ignore]` that was copied up to now keeps its default
(in place: its current) value. Find such mappings: the types with `[Ignore]` on a property that appear in a
`CreateMap<TSource, TDest>()` (either type, also with `.ReverseMap()` or `IncludeMembers`) whose other type has a property
of the same name. If you relied on the copy, remove `[Ignore]` or map the property with
`ForMember(d => d.Secret, o => o.MapFrom(s => s.Secret))`.

---

## 8. `null` source can return `null` (opt-in)

**Was.** `MapToX(source)` threw `ArgumentNullException` for a `null` source
([#27](https://github.com/iron-mapper/IronMapper/issues/27)); the workaround was `source?.MapToX()` or a check before the
call.

**Now.** Nothing changes by default. With `<IronMapperNullSourceReturnsNull>true</IronMapperNullSourceReturnsNull>` the
method of a class source and a class destination becomes `MapToX(this S? source)` → `D?` with
`[return: NotNullIfNotNull("source")]` and returns `null` for `null`
([`null` source](configuration.md#null-source-ironmappernullsourcereturnsnull)). It is planned to be the default in 3.0.0.

**What you may see with the property.** Code that maps a non-null source gets no new nullable warning: the result is
non-null there. Struct sources and destinations, the in-place overload, `null` collection elements and nested objects
behave as without it, and `IMapper.Map<TDest>(null)` still throws `ArgumentNullException`. On netstandard2.0 with
PolySharp set `<IronMapperEmitPolyfills>false</IronMapperEmitPolyfills>` (both generators would declare
`NotNullIfNotNullAttribute`, error `CS0101`). `catch (ArgumentNullException)` around a top-level `MapToX(source)` no longer runs: the
result is `null` instead.

**What to do.** Nothing, unless you want `null` → `null`: then set the property and remove the `?.`/`null` checks around
the calls (optional), or prepare for 3.0.0.

---

## 9. `init`-only properties in place: nested objects updated, warning `IM0025`

**Was.** The in-place overload `source.MapToX(destination)` (and `IMapper.Map(source, destination)`) skipped every
`init`-only property silently, also an `init`-only nested object
([#58](https://github.com/iron-mapper/IronMapper/issues/58)). A destination with only `init` properties is already
covered by section 4.

```csharp
public class OrderDto
{
    public string Name { get; set; } = "";
    public string Code { get; init; } = "";
    public CustomerDto? Customer { get; init; }
}

order.MapToOrderDto(existing);
// 2.0.0: Name updated; Code and Customer unchanged, no warning
// 2.1.0: Name updated; Code unchanged; an existing existing.Customer updated in place; warning IM0025 at the mapping
```

**Now.**

- An existing object in an `init`-only nested property is updated in place through the getter (the same object, new
  values), as a settable nested property is. A `null` there stays `null`, and a `null` source value is not assigned.
- Warning [`IM0025`](diagnostics.md#im0025--in-place-overload-does-not-update-init-only-properties) at the mapping lists
  the mapped `init`-only properties (also `required init`) that the in-place overload does not update, and the nested
  ones it updates only when they exist. Not for ignored properties, an in-place overload that is turned off, or one that
  is `[Obsolete]` (section 4).
- A type that has only `init` properties but an `init`-only nested object that can be updated is no longer
  `[Obsolete]`: its in-place overload updates that object.

**What you may see.** **The result at run time changes** only for `init`-only nested objects: an existing one now gets
the source's values (up to 2.0.0 it kept its old values). The other `init` properties are skipped as before. With
`TreatWarningsAsErrors` the new warning is a build error.

**What to do.** Check the mappings that `IM0025` points to. Then one of:

- the properties need not be mapped: ignore them (`[Ignore]`, `ForMember(d => d.Code, o => o.Ignore())`; a new object
  then does not get them either);
- the in-place update is not needed: turn it off (`GenerateInPlace = false`, `.GenerateMethods(inPlace: false)`,
  `IronMapperGenerateInPlace=false`). The warning then goes, but `IMapper.Map(source, destination)` still updates such
  a type in part, without a warning (the registration keeps a private in-place method with the same body): if you call
  it for the pair, keep that in mind;
- the properties should be updated: make them settable (`set`);
- keep it as it is: `#pragma warning disable IM0025` around the attribute or the `CreateMap` line, or
  `dotnet_diagnostic.IM0025.severity = none` in `.editorconfig` (`<NoWarn>IM0025</NoWarn>` for the whole project).


---

## 10. Hooks and `When` in the in-place overload

**Was.** The in-place overload `source.MapToX(destination)` (and `IMapper.Map(source, destination)`) assigned the
mapped properties only: it did not evaluate `When` and did not call `BeforeMap`/`AfterMap`
([#62](https://github.com/iron-mapper/IronMapper/issues/62)). AutoMapper's `Map(source, destination)` applies them, so
a migrated call silently skipped that logic.

```csharp
CreateMap<Row, Dto>().When(s => s.Active).AfterMap((s, d) => d.Version++);

row.MapToDto(existing);
// 2.0.0: properties assigned whatever Active is; Version unchanged
// 2.1.0: Active == false: existing unchanged; otherwise BeforeMap, the properties, AfterMap (Version + 1)
```

**Now.** The in-place body runs as a new object does: `When` first (`false`: the destination is left as it is, no hook
runs), then `BeforeMap`, the properties and `AfterMap`. Also for an existing nested object updated in place: the `When`
and hooks of its pair's mapping apply to it. `ConvertUsing` is not applied in place (that overload is `[Obsolete]`,
section 4). A destination with only `init` properties and a hook is no longer `[Obsolete]`: the hook may change it (the
`init` properties are listed in `IM0025`, section 9). Mappings without hooks and `When` get the same code as before.

**What you may see.** **The result at run time changes** for in-place calls of mappings with `When`, `BeforeMap` or
`AfterMap`: a destination for which `When` is false is no longer updated, and the hooks run (once per call). With
`TreatWarningsAsErrors`, a mapping to a type with only `init` properties and a hook gets `IM0025` instead of `CS0618` at
its calls.

**What to do.** Find the in-place calls (`source.MapToX(existing)`, `mapper.Map(source, existing)`) of mappings with
`When`, `BeforeMap` or `AfterMap`, and check that the hooks can run on an existing object (for example, a counter or a
timestamp that a hook sets). If the old behavior is needed, update the object without the mapping's logic yourself, or
declare a second mapping without the hooks for the in-place update.

---

## 11. `required` and `init` properties with `BeforeMap`/`AfterMap`

**Was.** A mapping with `BeforeMap` or `AfterMap` created the destination with `new D()` and assigned the properties one
by one ([#60](https://github.com/iron-mapper/IronMapper/issues/60)): a `required` property failed the build inside the
generated file (`CS9035`), and an `init` property was silently left at its default value. A `required` property that the
mapping gives no value (ignored, nothing maps it) failed with `CS9035` there too, also without hooks.

```csharp
public class Dto { public required string Code { get; set; } public string Name { get; init; } = ""; public int Id { get; set; } }

CreateMap<Row, Dto>().AfterMap((s, d) => d.Id++);
// 2.0.0: CS9035 in the generated file (Code); without required: Name not mapped
// 2.1.0: new Dto { Code = …, Name = … }, then BeforeMap, Id, AfterMap
```

**Now.** The object initializer sets the mapped `required` and `init` properties, then `BeforeMap` runs, the other
properties are assigned, and `AfterMap` runs; with a constructor, the initializer follows its arguments. After a factory
(`ConstructWith`, `ConstructUsing`) the properties are assigned as before (an `init` property there is `IM0021`). A
`required` property without a value is the new error
[`IM0026`](diagnostics.md#im0026--required-property-is-not-mapped) at the mapping, and the mapping is not generated,
instead of `CS9035` inside the generated file (not with a constructor that has `[SetsRequiredMembers]`).

**What you may see.** **The result at run time changes** for `init` properties of mappings with hooks: they now get the
source's values (up to 2.0.0 they kept their defaults). `BeforeMap` sees the `required` and `init` properties already set
(the other properties not yet). `IM0026` only appears where the build failed with `CS9035`.

**What to do.** Check the `BeforeMap` hooks of such mappings that read or set a `required` or `init` property: the
initializer has set it before the hook runs, and a hook that sets it overrides the mapped value. For `IM0026`, map the
property, remove `required`, or create the destination with a `[SetsRequiredMembers]` constructor or a factory.

## 12. `AddConverter<T>()` is `[Obsolete]`

**Was.** `opt.AddConverter<T>()` in `AddIronMapper(...)` registered the converter in the DI container as a transient
service, and nothing more. The generated code creates converters itself and never takes them from the container, so the
call did nothing for mapping, and a converter with dependencies in its constructor did not build (`IM0015`).

**Now.** The method is `[Obsolete]` and will be removed in 3.0.0. It still registers the type, as before.

**What you may see.** Warning `CS0618` at the call, a build error with `TreatWarningsAsErrors`. Nothing changes at run
time.

**What to do.** Remove the call. If your own code resolves the converter from the container, register it yourself
(`services.AddTransient<MyConverter>()`). A converter that needs a service is not supported: compute the value before
mapping or set it after ([Converters from DI are not supported](configuration.md#converters-from-di-are-not-supported-addconvertert-is-obsolete)).

# Upgrading to IronMapper 2.1

Русская версия: [upgrading-to-2.1.ru.md](upgrading-to-2.1.ru.md)

2.1.0 follows 2.0.0. 

whether it builds.
Two fixes of `ConvertUsing` in profiles can change what a mapping that built with 2.0.0 does or Everything else in 2.1.0 is new functionality or a fix that only affects code that did not build
with 2.0.0 ([CHANGELOG](../CHANGELOG.md)).

| Change | Who is affected |
|---|---|
| [`ConvertUsing` with a static method group is called](#1-convertusing-with-a-static-method-group-is-called) | Profiles with `ConvertUsing(SomeType.Method)` |
| [`ConvertUsing` that cannot be called is `IM0022`](#2-convertusing-that-cannot-be-called-is-im0022) | Profiles with `ConvertUsing(instance.Method)`, a local function, a private method or a delegate in a variable |

To find them, search the profiles for `ConvertUsing(` whose argument is not a lambda (`s => …`) and not a type argument
(`ConvertUsing<TConverter>()`).

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

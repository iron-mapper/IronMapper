# Upgrading to IronMapper 2.1

Русская версия: [upgrading-to-2.1.ru.md](upgrading-to-2.1.ru.md)

2.1.0 follows 2.0.0. Two fixes of `ConvertUsing` in profiles can change what a mapping that built with 2.0.0 does or
whether it builds. Everything else in 2.1.0 is new functionality or a fix that only affects code that did not build
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

# Compiler Diagnostics

> 📖 **[Русская версия](diagnostics.ru.md)**

IronMapper's source generator reports diagnostics with IDs `IM0001`–`IM0009`. They are declared in
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Status as of 1.1.1:**

| ID | Severity | Title | Reported? |
|---|---|---|---|
| IM0001 | Warning | Unmapped destination property | Yes — `[MapTo]`/`[MapFrom]` only |
| IM0002 | Error | Incompatible property types | **Not reported yet** |
| IM0003 | Error | Unresolvable mapping target type | **Not reported yet** |
| IM0004 | Error | Cyclic mapping detected | **Not reported yet** |
| IM0005 | Warning | Empty MappingProfile | Yes |
| IM0006 | Error | `[MapProperty]` destination property not found | Yes |
| IM0007 | Warning | Duplicate mapping definition | **Not reported yet** |
| IM0008 | Error | Abstract or interface destination type | Yes — attributes and profiles |
| IM0009 | Warning | Record constructor parameter not mapped | Yes — `[MapTo]`/`[MapFrom]` only |

All diagnostics are currently reported **without a source location**, so the IDE lists them in the
Error List but does not point to a line in your code.

When a problem has no working diagnostic (for example, a property type mismatch), the build fails with
a regular C# error (`CS0029`, `CS7036`, …) inside the generated `*.g.cs` file instead.

---

## IM0001 — Unmapped destination property

**Severity:** Warning

**Message:** `Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value`

**Cause:** A public settable property of the destination type has no source property with the same name
(case-insensitive) and no `[MapProperty]` pointing to it.

**Reported for:** `[MapTo]` / `[MapFrom]` mappings only. In `MappingProfile` mappings unmatched destination
properties are skipped silently.

**Fix:**
- Add the matching property to the source type.
- Use `[MapProperty("DestPropertyName")]` on a source property.
- Put `[Ignore]` on the destination property to suppress the warning.
- A code fix ("Add [Ignore] to '...'") exists in `IronMapper.CodeFixes`; it is not covered by tests.

---

## IM0002 — Incompatible property types

**Severity:** Error · **Not reported yet**

**Message (declared):** `Property '{0}': source type '{1}' cannot be assigned to destination type '{2}'. Add [MapConverter] or implement ITypeConverter<{1}, {2}>.`

**Current behavior:** the generator does not check property types. A mismatch (including a nested object
of a different type, e.g. `Address` → `AddressDto`) produces a C# compile error such as `CS0029` in the
generated file.

**Fix:** use `[MapConverter]`, `opt.UseConverter<T>()` or `opt.MapFrom(...)` for that property.

---

## IM0003 — Unresolvable mapping target type

**Severity:** Error · **Not reported yet**

**Message (declared):** `The type '{0}' referenced in [{1}] could not be resolved in the current compilation`

**Current behavior:** if the type passed to `[MapTo]`/`[MapFrom]` cannot be resolved, the C# compiler
reports `CS0246` for the `typeof(...)` expression, and the generator still emits a mapping file that
references the missing type, which adds `CS0400` errors in the generated code.

---

## IM0004 — Cyclic mapping detected

**Severity:** Error · **Not reported yet**

**Message (declared):** `Cyclic mapping detected: '{0}' maps to '{1}' which directly or indirectly maps back to '{0}'`

**Current behavior:** no cycle detection is implemented.

---

## IM0005 — Empty MappingProfile

**Severity:** Warning

**Message:** `MappingProfile '{0}' has no mappings defined`

**Cause:** a class deriving from `MappingProfile` has no `CreateMap<TSource, TDest>()` call chain in the
statements of its constructor(s).

**Fix:** add at least one `CreateMap<TSource, TDest>()` call, or remove the empty profile class.

---

## IM0006 — `[MapProperty]` destination property not found

**Severity:** Error

**Message:** `[MapProperty] destination '{0}' does not exist in type '{1}'`

**Cause:** the name passed to `[MapProperty]` does not match any public settable property of the destination
type. The comparison is **case-insensitive**.

**Fix:** correct the name, or add the property to the destination type. A code fix ("Did you mean '...'?",
Levenshtein distance ≤ 2) exists in `IronMapper.CodeFixes`; it is not covered by tests.

---

## IM0007 — Duplicate mapping definition

**Severity:** Warning · **Not reported yet**

**Message (declared):** `Both an attribute and a MappingProfile define a mapping from '{0}' to '{1}'. The profile-based mapping takes precedence.`

**Current behavior:** no duplicate detection is implemented, and the "profile takes precedence" rule is not
implemented either. If the same source/destination pair is defined twice (for example by `[MapTo]` and by
`CreateMap` in a profile), the generator fails with warning `CS8785` ("HintName ... must be unique") and
**generates no mapping code at all** for the project. Define each pair only once.

---

## IM0008 — Abstract or interface destination type

**Severity:** Error

**Message:** `Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination`

**Cause:** the destination type of `[MapTo]`, `[MapFrom]` or `CreateMap` is abstract or an interface.

**Fix:** map to a concrete type. For profile mappings, `ConvertUsing` cannot be used as a workaround,
because the generator reports IM0008 before reading the rest of the mapping.

---

## IM0009 — Record constructor parameter not mapped

**Severity:** Warning

**Message:** `Constructor parameter '{0}' in record '{1}' has no matching source property`

**Cause:** a record destination has an explicitly declared constructor (including a primary constructor)
with a parameter that has no source property of the same name (case-insensitive).

**Reported for:** `[MapTo]` / `[MapFrom]` mappings only.

**Note:** the generator creates destinations with an object initializer, never with a constructor call.
A positional record such as `record PointDto(double X, double Y)` has no parameterless constructor, so the
generated code does not compile (`CS7036`) even when this warning is not reported. Use records with
`init` properties as destinations.

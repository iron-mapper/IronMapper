# Compiler Diagnostics

> 📖 **[Русская версия](diagnostics.ru.md)**

IronMapper's source generator reports diagnostics with IDs `IM0001`–`IM0012`. They are declared in
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Current status** (`main`, after 1.1.1):

| ID | Severity | Title | Reported? |
|---|---|---|---|
| IM0001 | Warning | Unmapped destination property | Yes — `[MapTo]`/`[MapFrom]` only |
| IM0002 | Error | Incompatible property types | **Not reported yet** |
| IM0003 | Error | Unresolvable mapping target type | **Not reported yet** |
| IM0004 | Error | Cyclic mapping detected | **Not reported yet** |
| IM0005 | Warning | Empty MappingProfile | Yes |
| IM0006 | Error | `[MapProperty]` destination property not found | Yes |
| IM0007 | Warning | Duplicate mapping definition | Yes — attributes and profiles |
| IM0008 | Error | Abstract or interface destination type | Yes — attributes and profiles |
| IM0009 | Warning | Record constructor parameter not mapped | Yes — `[MapTo]`/`[MapFrom]` only |
| IM0010 | Error | Mapping type is not accessible from generated code | Yes — attributes and profiles |
| IM0011 | Error | Profile lambda uses a member that generated code cannot access | Yes — profiles |
| IM0012 | Error | Profile lambda captures state | Yes — profiles |
| IM0013 | Warning | Mapping method name qualified to avoid a collision | Yes — attributes and profiles |
| IM0014 | Error | Mapping method name collision cannot be resolved | Yes — attributes and profiles |

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

**Severity:** Warning

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

---

## IM0010 — Mapping type is not accessible from generated code

**Severity:** Error

**Message:** `Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class IronMapper.Generated.GeneratedMappers cannot access it. Make the type and all types that contain it public or internal.`

**Cause:** the source or destination type, one of the types containing it, or one of its generic type arguments is a
`private`, `protected` or `private protected` nested type. Generated methods live in the top-level class
`IronMapper.Generated.GeneratedMappers` and cannot reference such types.

**Reported for:** `[MapTo]` / `[MapFrom]` and `MappingProfile` mappings. Only the affected mapping is skipped; all other
mappings in the project are still generated.

**Fix:** make the type (and every type that contains it) `public` or `internal`. Internal types are supported; the
generated methods are then `internal` (see [Generated method accessibility](configuration.md#generated-method-accessibility)).

---

## IM0011 — Profile lambda uses a member that generated code cannot access

**Severity:** Error

**Message:** `A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class IronMapper.Generated.GeneratedMappers, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.`

**Cause:** a lambda in `MapFrom`, `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` or `AddTransformer` references a
`private`, `protected` or `private protected` member or type (or a member of such a type) — typically a
`private static` helper method or constant of the profile. Lambdas are rewritten into the generated class, not called
on the profile, so such members are not visible there.

**Fix:** make the member (or the type that contains it) `internal`. See [Profile lambdas](configuration.md#profile-lambdas).

The affected mapping is not generated (for `AddTransformer`, the transformer is dropped); other mappings are.

---

## IM0012 — Profile lambda captures state

**Severity:** Error

**Message:** `A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.`

**Cause:** the lambda uses an instance field, property or method of the profile, `this`/`base`, or a local variable,
parameter or local function of the constructor. None of these exist when the generated code runs.

**Fix:** make the member `static` (or `const`), or compute the value from the source object.
See [Profile lambdas](configuration.md#profile-lambdas).

The affected mapping is not generated (for `AddTransformer`, the transformer is dropped); other mappings are.

---

## IM0013 — Mapping method name qualified to avoid a collision

**Severity:** Warning

**Message:** `Several mappings from '{0}' have a destination type named '{1}', so the mapping to '{2}' is generated as '{3}' instead of '{4}'. Call it as source.{3}(); the collection methods are {3}List and {3}Array.`

**Cause:** one source type is mapped to several destination types with the same simple name (different namespaces,
nested in different types, or different type arguments). `MapTo{Name}` would be declared twice (`CS0111`), so these
mappings get qualified names, e.g. `MapToAdminUserDto` and `MapToPublicUserDto`, or `MapToBoxOfInt32`. The rule is
described in [Method names and collisions](configuration.md#one-source-type-several-destination-types-with-the-same-simple-name).

**Fix:** call the method by the name in the message, or rename one of the destination types to get `MapTo{Name}` back.
If the qualified names are intended, suppress the warning with `<NoWarn>$(NoWarn);IM0013</NoWarn>`.

---

## IM0014 — Mapping method name collision cannot be resolved

**Severity:** Error

**Message:** `Mapping from '{0}' to '{1}' was not generated: another mapping from '{0}' has a destination type named '{2}', and every qualified method name (for example '{3}') is already used by another mapping. Rename one of the destination types.`

**Cause:** the qualified names from IM0013 are all taken by other mappings of the same source type (for example,
`N1.Dto` and `N2.Dto` would become `MapToN1Dto`/`MapToN2Dto`, but types named `N1Dto` and `N2Dto` are mapped too).

**Fix:** rename one of the destination types. Only the mappings of the colliding group are skipped; other mappings are
generated.

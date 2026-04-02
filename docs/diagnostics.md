# Compiler Diagnostics

IronMapper emits compiler diagnostics (errors and warnings) when it detects mapping configuration issues at build time.

---

## IM0001 — Unmapped destination property

**Severity:** Warning

**Message:** `Property '{PropertyName}' on '{DestType}' has no matching source property and will not be mapped.`

**Cause:** The destination type has a property that cannot be matched to any source property (by name, `[MapProperty]`, or `ForMember`).

**Fix:**
- Add the matching property to the source type.
- Use `[MapProperty("DestPropertyName")]` on the source property to remap it.
- Use `ForMember(d => d.Prop, o => o.MapFrom(...))` in a profile.
- Use `ForMember(d => d.Prop, o => o.Ignore())` if the property should be intentionally skipped.

---

## IM0002 — Source type is not accessible

**Severity:** Error

**Message:** `Source type '{TypeName}' is not accessible (must be public or internal).`

**Cause:** The source type decorated with `[MapTo]` is private or nested in a way the generator cannot access.

**Fix:** Make the source type `public` or `internal`.

---

## IM0003 — Destination type is not accessible

**Severity:** Error

**Message:** `Destination type '{TypeName}' is not accessible (must be public or internal).`

**Cause:** The destination type referenced in `[MapTo]` or `[MapFrom]` is private or otherwise inaccessible.

**Fix:** Make the destination type `public` or `internal`.

---

## IM0004 — [MapProperty] destination property not found

**Severity:** Error  
*(see also IM0006)*

**Cause:** `[MapProperty("X")]` was applied to a source property but `X` does not exist on the destination type.

**Fix:** Correct the property name in the attribute argument, or add the property to the destination type.

---

## IM0005 — Empty mapping profile

**Severity:** Warning

**Message:** `MappingProfile '{ProfileName}' contains no mappings.`

**Cause:** A `MappingProfile` subclass was found but its constructor calls no `CreateMap` or `ForMember`.

**Fix:** Add at least one `CreateMap<TSource, TDest>()` call, or remove the empty profile class.

---

## IM0006 — [MapProperty] destination not found

**Severity:** Error

**Message:** `[MapProperty] destination property '{Name}' not found on '{DestType}'.`

**Cause:** The name supplied to `[MapProperty]` does not match any writable property on the destination type.

**Fix:** Check spelling and casing; the match is case-sensitive.

---

## IM0007 — Converter type does not implement ITypeConverter

**Severity:** Error

**Message:** `Type '{ConverterType}' used in [MapConverter] does not implement ITypeConverter<TSource, TDest>.`

**Cause:** The type provided to `[MapConverter(typeof(X))]` does not implement the required generic interface.

**Fix:** Implement `ITypeConverter<TSourceProp, TDestProp>` on the converter class.

---

## IM0008 — Destination type is abstract or an interface

**Severity:** Error

**Message:** `Destination type '{TypeName}' cannot be instantiated (abstract class or interface).`

**Cause:** A `[MapTo]` / `[MapFrom]` / `CreateMap` points to a type that cannot be `new`-ed.

**Fix:** Use a concrete destination type, or supply a `ConvertUsing` factory that handles construction manually.

---

## IM0009 — Record destination has unmapped constructor parameters

**Severity:** Warning

**Message:** `Record destination '{TypeName}' has constructor parameter '{ParameterName}' with no matching source property.`

**Cause:** A destination record's primary constructor has a parameter that cannot be matched to any source property.

**Fix:**
- Add the matching property to the source type.
- Use `ForMember` to supply a value for that constructor parameter.
- If intentional, acknowledge the warning with `#pragma warning disable IM0009`.

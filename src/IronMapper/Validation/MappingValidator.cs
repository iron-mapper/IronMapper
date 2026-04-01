using System;
using System.Collections.Generic;
using System.Reflection;
using IronMapper.Exceptions;

namespace IronMapper.Validation;

/// <summary>
/// Runtime utility that inspects a source / destination type pair and reports
/// potential mapping problems before a mapping is executed.
///
/// Under normal usage the Source Generator catches all of these issues at compile time.
/// <see cref="MappingValidator"/> is intended for:
/// <list type="bullet">
///   <item>Early validation in test or diagnostic utilities.</item>
///   <item>Applications that build type pairs dynamically and want descriptive errors.</item>
///   <item>Situations where the generator was not run (e.g. reflection-only runtime mapping).</item>
/// </list>
/// </summary>
public static class MappingValidator
{
    /// <summary>
    /// Validates that <paramref name="sourceType"/> can be mapped to <paramref name="destType"/>
    /// and returns a <see cref="ValidationResult"/> describing any errors or warnings.
    /// </summary>
    /// <param name="sourceType">The source type to map from.</param>
    /// <param name="destType">The destination type to map to.</param>
    /// <returns>A <see cref="ValidationResult"/> with all detected issues.</returns>
    public static ValidationResult Validate(Type sourceType, Type destType)
    {
        if (sourceType is null) throw new ArgumentNullException(nameof(sourceType));
        if (destType is null) throw new ArgumentNullException(nameof(destType));

        var errors = new List<string>();
        var warnings = new List<string>();

        // IM0008 equivalent: abstract / interface destination.
        if (destType.IsInterface)
        {
            errors.Add(
                $"Destination type '{destType.Name}' is an interface and cannot be instantiated. " +
                "Map to a concrete class or provide a custom ITypeConverter.");
        }
        else if (destType.IsAbstract)
        {
            errors.Add(
                $"Destination type '{destType.Name}' is abstract and cannot be instantiated. " +
                "Map to a concrete class or provide a custom ITypeConverter.");
        }

        // Destination must have a public parameterless constructor (for object-initializer mapping).
        if (!destType.IsInterface && !destType.IsAbstract && !destType.IsValueType)
        {
            var ctor = destType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            if (ctor is null)
            {
                errors.Add(
                    $"Destination type '{destType.Name}' has no public parameterless constructor. " +
                    "Add a default constructor so the generated mapper can use object-initializer syntax.");
            }
        }

        // IM0001 equivalent: destination properties with no source match (warnings only).
        var sourceProps = GetPublicReadableProperties(sourceType);
        var destProps = GetPublicWritableProperties(destType);

        var sourcePropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in sourceProps) sourcePropNames.Add(p.Name);

        foreach (var destProp in destProps)
        {
            if (!sourcePropNames.Contains(destProp.Name))
            {
                warnings.Add(
                    $"Property '{destProp.Name}' on '{destType.Name}' has no matching source property " +
                    $"in '{sourceType.Name}' and will be left at its default value. " +
                    "Use [Ignore] on the destination property or add a matching source property.");
            }
        }

        // IM0002 equivalent: incompatible property types (error when types are not assignable).
        foreach (var destProp in destProps)
        {
            var sourceProp = FindProperty(sourceProps, destProp.Name);
            if (sourceProp is null) continue;

            if (!destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
            {
                errors.Add(
                    $"Property '{destProp.Name}': source type '{sourceProp.PropertyType.Name}' " +
                    $"cannot be assigned to destination type '{destProp.PropertyType.Name}'. " +
                    "Add [MapConverter] or implement ITypeConverter to convert between the types.");
            }
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>
    /// Validates the mapping and throws a <see cref="MappingException"/> if any errors are found.
    /// Warnings are included in the exception message but do not prevent the throw when combined with errors.
    /// </summary>
    /// <param name="sourceType">The source type to map from.</param>
    /// <param name="destType">The destination type to map to.</param>
    /// <exception cref="MappingException">Thrown when the validation finds at least one error.</exception>
    public static void ValidateAndThrow(Type sourceType, Type destType)
    {
        var result = Validate(sourceType, destType);
        if (result.HasErrors)
        {
            throw new MappingException(
                $"Mapping from '{sourceType.Name}' to '{destType.Name}' has configuration errors:\n" +
                result.FormatMessages());
        }
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static PropertyInfo[] GetPublicReadableProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

    private static PropertyInfo[] GetPublicWritableProperties(Type type)
    {
        var all = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        var result = new List<PropertyInfo>(all.Length);
        foreach (var p in all)
            if (p.CanWrite && p.GetSetMethod(nonPublic: false) is not null)
                result.Add(p);
        return result.ToArray();
    }

    private static PropertyInfo? FindProperty(PropertyInfo[] props, string name)
    {
        foreach (var p in props)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}

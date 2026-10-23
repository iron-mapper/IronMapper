using System.Text;

namespace IronMapper.Generator.Analysis;

/// <summary>
/// Name of the generated class of an assembly: <c>GeneratedMappers_{AssemblyName}</c> in the namespace
/// <c>IronMapper.Generated</c>. Unique per assembly, so the generated types of two assemblies never have the same full
/// name: tools that identify types by full name (ArchUnitNET merged the nested types <c>&lt;&gt;c</c>, <c>&lt;&gt;O</c>,
/// <c>ConverterCache`1</c> and <c>IElementMapper`2</c> of two assemblies, with their dependencies) and explicit calls
/// <c>GeneratedMappers_X.MapToY(...)</c> (one class name in every assembly gave CS0436) see the right class.
/// </summary>
internal static class GeneratedClassName
{
    /// <summary>The class name without an assembly name (a compilation without one, e.g. in tests).</summary>
    public const string Prefix = "GeneratedMappers";

    /// <summary>Longest assembly part kept as is; a longer one is shortened and gets a hash of the full name.</summary>
    public const int MaxAssemblyPartLength = 64;

    private const int HashLength = 8;

    /// <summary>
    /// <c>GeneratedMappers_{name}</c>: every character of <paramref name="assemblyName"/> that is not a letter, a digit or
    /// <c>_</c> becomes <c>_</c> (<c>Shop.Application</c> → <c>GeneratedMappers_Shop_Application</c>). A name longer than
    /// <see cref="MaxAssemblyPartLength"/> keeps its first characters and ends with <c>_</c> and 8 hex digits of a stable
    /// hash (FNV-1a) of the full assembly name, so two long names with the same beginning stay different.
    /// </summary>
    public static string For(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName)) return Prefix;

        var part = new StringBuilder(assemblyName!.Length);
        foreach (var c in assemblyName)
            part.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        if (part.Length > MaxAssemblyPartLength)
        {
            part.Length = MaxAssemblyPartLength - HashLength - 1;
            part.Append('_').Append(Fnv1a(assemblyName).ToString("X8"));
        }

        return Prefix + "_" + part;
    }

    // Stable across processes and runtimes (string.GetHashCode is randomized), so the name does not change between builds.
    private static uint Fnv1a(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }
}

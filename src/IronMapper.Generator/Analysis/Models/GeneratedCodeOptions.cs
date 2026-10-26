using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// What the generated code may use in the user's compilation (rule "only APIs available in the user's compilation"):
/// <c>ArgumentNullException.ThrowIfNull</c> (.NET 6+), <c>DoesNotReturnAttribute</c>, module initializers (C# 9) and
/// <c>ModuleInitializerAttribute</c> (.NET 5+); and the name of the generated class, which depends on the assembly name
/// (<see cref="GeneratedClassName"/>). Compared by value, so it is recomputed for every compilation but changes the output
/// only when the target framework, the language version or the assembly name changes.
/// </summary>
internal sealed class GeneratedCodeOptions : IEquatable<GeneratedCodeOptions>
{
    /// <summary>Name of the throw helper declared in <c>GeneratedMappers.g.cs</c> when <see cref="UseThrowIfNull"/> is false.</summary>
    public const string ThrowHelperName = "ThrowArgumentNullException";

    public GeneratedCodeOptions(bool useThrowIfNull, bool hasDoesNotReturn, bool supportsModuleInitializers,
        bool hasModuleInitializerAttribute, bool hasGeneratedCodeAttribute = true, bool hasExcludeFromCodeCoverage = true,
        string className = GeneratedClassName.Prefix)
    {
        ClassName = className;
        UseThrowIfNull = useThrowIfNull;
        HasDoesNotReturn = hasDoesNotReturn;
        SupportsModuleInitializers = supportsModuleInitializers;
        HasModuleInitializerAttribute = hasModuleInitializerAttribute;
        HasGeneratedCodeAttribute = hasGeneratedCodeAttribute;
        HasExcludeFromCodeCoverage = hasExcludeFromCodeCoverage;
    }

    /// <summary>
    /// Name of the generated class in <c>IronMapper.Generated</c>: <c>GeneratedMappers_{AssemblyName}</c>
    /// (<see cref="GeneratedClassName.For"/>), declared by every generated file of the assembly.
    /// </summary>
    public string ClassName { get; }

    /// <summary><c>ArgumentNullException.ThrowIfNull(object, string)</c> exists and is accessible.</summary>
    public bool UseThrowIfNull { get; }

    /// <summary><c>System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute</c> exists and is accessible.</summary>
    public bool HasDoesNotReturn { get; }

    /// <summary>
    /// The language version is C# 9 or later, so <c>[ModuleInitializer]</c> is honored (with C# 8 it is an error). Without
    /// it the registration for <c>IMapper</c> is emitted without the attribute and must be called by the user.
    /// </summary>
    public bool SupportsModuleInitializers { get; }

    /// <summary>
    /// <c>System.Runtime.CompilerServices.ModuleInitializerAttribute</c> exists and is accessible; otherwise the
    /// generator declares an internal copy (the compiler recognizes it by its full name).
    /// </summary>
    public bool HasModuleInitializerAttribute { get; }

    /// <summary><c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> exists and is accessible (.NET Standard 2.0+).</summary>
    public bool HasGeneratedCodeAttribute { get; }

    /// <summary>
    /// <c>System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute</c> exists and is accessible (.NET Standard 2.0+).
    /// </summary>
    public bool HasExcludeFromCodeCoverage { get; }

    /// <summary>Reads the options from <paramref name="compilation"/>.</summary>
    public static GeneratedCodeOptions From(Compilation compilation)
    {
        bool useThrowIfNull = false;
        var argumentNull = compilation.GetTypeByMetadataName("System.ArgumentNullException");
        if (argumentNull is not null)
        {
            foreach (var member in argumentNull.GetMembers("ThrowIfNull"))
            {
                if (member is IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public, Parameters.Length: 2 } method
                    && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                    && method.Parameters[1].Type.SpecialType == SpecialType.System_String)
                {
                    useThrowIfNull = true;
                    break;
                }
            }
        }

        var doesNotReturn = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute");
        bool hasDoesNotReturn = doesNotReturn is not null && compilation.IsSymbolAccessibleWithin(doesNotReturn, compilation.Assembly);

        bool supportsModuleInitializers = compilation is not CSharpCompilation csharp
                                         || csharp.LanguageVersion >= LanguageVersion.CSharp9;

        bool hasModuleInitializerAttribute = false;
        foreach (var attribute in compilation.GetTypesByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute"))
        {
            if (compilation.IsSymbolAccessibleWithin(attribute, compilation.Assembly))
            {
                hasModuleInitializerAttribute = true;
                break;
            }
        }

        return new GeneratedCodeOptions(useThrowIfNull, hasDoesNotReturn, supportsModuleInitializers, hasModuleInitializerAttribute,
            IsAccessible(compilation, "System.CodeDom.Compiler.GeneratedCodeAttribute"),
            IsAccessible(compilation, "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"),
            GeneratedClassName.For(compilation.AssemblyName));
    }

    private static bool IsAccessible(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
    }

    /// <summary>
    /// The statement that throws <c>ArgumentNullException</c> when <paramref name="parameter"/> is null. Without
    /// <c>[DoesNotReturn]</c> on the throw helper the compiler's null-state analysis would treat the parameter as maybe
    /// null after the check (CS8602), so the unreachable <paramref name="returnStatement"/> follows the call.
    /// </summary>
    public string NullCheck(string parameter, string returnStatement)
        => UseThrowIfNull
            ? $"global::System.ArgumentNullException.ThrowIfNull({parameter}, nameof({parameter}));"
            : HasDoesNotReturn
                ? $"if ({parameter} is null) {ThrowHelperName}(nameof({parameter}));"
                : $"if ({parameter} is null) {{ {ThrowHelperName}(nameof({parameter})); {returnStatement} }}";

    /// <inheritdoc/>
    public bool Equals(GeneratedCodeOptions? other)
        => other is not null
           && UseThrowIfNull == other.UseThrowIfNull
           && HasDoesNotReturn == other.HasDoesNotReturn
           && SupportsModuleInitializers == other.SupportsModuleInitializers
           && HasModuleInitializerAttribute == other.HasModuleInitializerAttribute
           && HasGeneratedCodeAttribute == other.HasGeneratedCodeAttribute
           && HasExcludeFromCodeCoverage == other.HasExcludeFromCodeCoverage
           && ClassName == other.ClassName;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratedCodeOptions);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (UseThrowIfNull ? 1 : 0) | (HasDoesNotReturn ? 2 : 0) | (SupportsModuleInitializers ? 4 : 0)
           | (HasModuleInitializerAttribute ? 8 : 0) | (HasGeneratedCodeAttribute ? 16 : 0) | (HasExcludeFromCodeCoverage ? 32 : 0)
           | (StringComparer.Ordinal.GetHashCode(ClassName) << 6);
}

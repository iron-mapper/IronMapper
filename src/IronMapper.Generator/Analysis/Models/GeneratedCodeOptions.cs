using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// What the generated code may use in the user's compilation (rule "only APIs available in the user's compilation"):
/// <c>ArgumentNullException.ThrowIfNull</c> (.NET 6+), <c>DoesNotReturnAttribute</c>, module initializers (C# 9) and
/// <c>ModuleInitializerAttribute</c> (.NET 5+). Compared by value, so it is recomputed for every compilation but changes
/// the output only when the target framework or the language version changes.
/// </summary>
internal sealed class GeneratedCodeOptions : IEquatable<GeneratedCodeOptions>
{
    /// <summary>Name of the throw helper declared in <c>GeneratedMappers.g.cs</c> when <see cref="UseThrowIfNull"/> is false.</summary>
    public const string ThrowHelperName = "ThrowArgumentNullException";

    public GeneratedCodeOptions(bool useThrowIfNull, bool hasDoesNotReturn, bool supportsModuleInitializers,
        bool hasModuleInitializerAttribute)
    {
        UseThrowIfNull = useThrowIfNull;
        HasDoesNotReturn = hasDoesNotReturn;
        SupportsModuleInitializers = supportsModuleInitializers;
        HasModuleInitializerAttribute = hasModuleInitializerAttribute;
    }

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

    /// <summary>Reads the options from <paramref name="compilation"/>.</summary>
    public static GeneratedCodeOptions From(Compilation compilation)
    {
        var useThrowIfNull = false;
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
        var hasDoesNotReturn = doesNotReturn is not null && compilation.IsSymbolAccessibleWithin(doesNotReturn, compilation.Assembly);

        var supportsModuleInitializers = compilation is not CSharpCompilation csharp
                                         || csharp.LanguageVersion >= LanguageVersion.CSharp9;

        var hasModuleInitializerAttribute = false;
        foreach (var attribute in compilation.GetTypesByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute"))
        {
            if (compilation.IsSymbolAccessibleWithin(attribute, compilation.Assembly))
            {
                hasModuleInitializerAttribute = true;
                break;
            }
        }

        return new GeneratedCodeOptions(useThrowIfNull, hasDoesNotReturn, supportsModuleInitializers, hasModuleInitializerAttribute);
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
           && HasModuleInitializerAttribute == other.HasModuleInitializerAttribute;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratedCodeOptions);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (UseThrowIfNull ? 1 : 0) | (HasDoesNotReturn ? 2 : 0) | (SupportsModuleInitializers ? 4 : 0)
           | (HasModuleInitializerAttribute ? 8 : 0);
}

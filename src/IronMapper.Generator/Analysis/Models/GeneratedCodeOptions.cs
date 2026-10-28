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
        string className = GeneratedClassName.Prefix, bool hasNotNullIfNotNull = true, bool nullSourceReturnsNull = false,
        bool emitPolyfills = true)
    {
        EmitPolyfills = emitPolyfills;
        HasNotNullIfNotNull = hasNotNullIfNotNull;
        NullSourceReturnsNull = nullSourceReturnsNull;
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

    /// <summary>
    /// <c>System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute</c> exists and is accessible (.NET Core 3.0+,
    /// .NET Standard 2.1); otherwise, with <see cref="NullSourceReturnsNull"/>, the generator declares an internal copy (the
    /// compiler recognizes the nullable attributes by their full names).
    /// </summary>
    public bool HasNotNullIfNotNull { get; }

    /// <summary>
    /// <c>IronMapperNullSourceReturnsNull</c> (<see cref="GeneratorSettings.NullSourceReturnsNull"/>): an MSBuild property,
    /// not an API of the compilation; carried here because every output that depends on it also takes these options.
    /// </summary>
    public bool NullSourceReturnsNull { get; }

    /// <summary>
    /// <c>IronMapperEmitPolyfills</c> (<see cref="GeneratorSettings.EmitPolyfills"/>): declare the internal copies of
    /// <c>ModuleInitializerAttribute</c> and <c>NotNullIfNotNullAttribute</c> when the target has none.
    /// </summary>
    public bool EmitPolyfills { get; }

    /// <summary>These options with the MSBuild properties that change the generated code like an API of the target.</summary>
    public GeneratedCodeOptions WithSettings(bool nullSourceReturnsNull, bool emitPolyfills)
        => nullSourceReturnsNull == NullSourceReturnsNull && emitPolyfills == EmitPolyfills
            ? this
            : new GeneratedCodeOptions(UseThrowIfNull, HasDoesNotReturn, SupportsModuleInitializers, HasModuleInitializerAttribute,
                HasGeneratedCodeAttribute, HasExcludeFromCodeCoverage, ClassName, HasNotNullIfNotNull, nullSourceReturnsNull,
                emitPolyfills);

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

        return new GeneratedCodeOptions(useThrowIfNull, hasDoesNotReturn, supportsModuleInitializers,
            AnyAccessible(compilation, "System.Runtime.CompilerServices.ModuleInitializerAttribute"),
            IsAccessible(compilation, "System.CodeDom.Compiler.GeneratedCodeAttribute"),
            IsAccessible(compilation, "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"),
            GeneratedClassName.For(compilation.AssemblyName),
            AnyAccessible(compilation, "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute"));
    }

    // Also a copy declared by a referenced assembly and visible here (InternalsVisibleTo): declaring another one would be
    // CS0436 at every use.
    private static bool AnyAccessible(Compilation compilation, string metadataName)
    {
        foreach (var type in compilation.GetTypesByMetadataName(metadataName))
        {
            if (compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                return true;
            }
        }

        return false;
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
           && HasNotNullIfNotNull == other.HasNotNullIfNotNull
           && NullSourceReturnsNull == other.NullSourceReturnsNull
           && EmitPolyfills == other.EmitPolyfills
           && ClassName == other.ClassName;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratedCodeOptions);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (UseThrowIfNull ? 1 : 0) | (HasDoesNotReturn ? 2 : 0) | (SupportsModuleInitializers ? 4 : 0)
           | (HasModuleInitializerAttribute ? 8 : 0) | (HasGeneratedCodeAttribute ? 16 : 0) | (HasExcludeFromCodeCoverage ? 32 : 0)
           | (HasNotNullIfNotNull ? 64 : 0) | (NullSourceReturnsNull ? 128 : 0) | (EmitPolyfills ? 256 : 0)
           | (StringComparer.Ordinal.GetHashCode(ClassName) << 9);
}

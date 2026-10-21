using System;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// One generated mapping in the registration for <c>IMapper</c> (<c>GeneratedMappers.Registration.g.cs</c>):
/// the type pair, the name of the generated methods and which overloads exist. Compared by value.
/// </summary>
internal sealed class MappingRegistration : IEquatable<MappingRegistration>
{
    public MappingRegistration(string sourceFullName, string destFullName, string methodName, string helperScope,
        GeneratedMethods methods)
    {
        SourceFullName = sourceFullName;
        DestFullName = destFullName;
        MethodName = methodName;
        HelperScope = helperScope;
        Methods = methods;
    }

    /// <summary>Fully-qualified C# name of the source type.</summary>
    public string SourceFullName { get; }

    /// <summary>Fully-qualified C# name of the destination type.</summary>
    public string DestFullName { get; }

    /// <summary><c>MapToX</c> (possibly qualified, IM0013); <c>List</c>/<c>Array</c> are appended for the collection methods.</summary>
    public string MethodName { get; }

    /// <summary>Helper-name scope of the mapping: the private in-place method is <c>MapInto{HelperScope}</c>.</summary>
    public string HelperScope { get; }

    /// <summary>The overloads generated for the mapping (assembly settings with the mapping's overrides applied).</summary>
    public GeneratedMethods Methods { get; }

    /// <inheritdoc/>
    public bool Equals(MappingRegistration? other)
        => other is not null
           && SourceFullName == other.SourceFullName
           && DestFullName == other.DestFullName
           && MethodName == other.MethodName
           && HelperScope == other.HelperScope
           && Methods.Equals(other.Methods);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingRegistration);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (((SourceFullName.GetHashCode() * 31 + DestFullName.GetHashCode()) * 31 + MethodName.GetHashCode()) * 31
            + HelperScope.GetHashCode()) * 31 + Methods.GetHashCode();
}

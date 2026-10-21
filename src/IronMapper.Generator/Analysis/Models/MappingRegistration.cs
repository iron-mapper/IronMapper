using System;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>
/// One generated mapping in the registration for <c>IMapper</c> (<c>GeneratedMappers.Registration.g.cs</c>):
/// the type pair and the name of the generated methods. Compared by value.
/// </summary>
internal sealed class MappingRegistration : IEquatable<MappingRegistration>
{
    public MappingRegistration(string sourceFullName, string destFullName, string methodName)
    {
        SourceFullName = sourceFullName;
        DestFullName = destFullName;
        MethodName = methodName;
    }

    /// <summary>Fully-qualified C# name of the source type.</summary>
    public string SourceFullName { get; }

    /// <summary>Fully-qualified C# name of the destination type.</summary>
    public string DestFullName { get; }

    /// <summary><c>MapToX</c> (possibly qualified, IM0013); <c>List</c>/<c>Array</c> are appended for the collection methods.</summary>
    public string MethodName { get; }

    /// <inheritdoc/>
    public bool Equals(MappingRegistration? other)
        => other is not null
           && SourceFullName == other.SourceFullName
           && DestFullName == other.DestFullName
           && MethodName == other.MethodName;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MappingRegistration);

    /// <inheritdoc/>
    public override int GetHashCode()
        => (SourceFullName.GetHashCode() * 31 + DestFullName.GetHashCode()) * 31 + MethodName.GetHashCode();
}

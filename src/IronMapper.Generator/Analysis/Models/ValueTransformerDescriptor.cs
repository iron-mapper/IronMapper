using System;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>Generator-side descriptor for a profile-level value transformer.</summary>
internal sealed class ValueTransformerDescriptor : IEquatable<ValueTransformerDescriptor>
{
    /// <summary>Fully-qualified type name (global:: prefixed) for matching against property types.</summary>
    public string FullyQualifiedTypeName { get; }

    /// <summary>Simple type name used to generate a unique method name (e.g. "String", "Decimal").</summary>
    public string FriendlyTypeName { get; }

    /// <summary>Verbatim C# expression body of the transformer lambda (parameter renamed to "value").</summary>
    public string LambdaBody { get; }

    /// <summary>Name of the private static helper method emitted in the generated mapper.</summary>
    public string MethodName { get; }

    /// <summary>Initialises a new <see cref="ValueTransformerDescriptor"/>.</summary>
    /// <param name="fullyQualifiedTypeName">Fully-qualified type name (global:: prefixed) for matching against property types.</param>
    /// <param name="friendlyTypeName">Simple type name used to generate a unique method name (e.g. "String", "Decimal").</param>
    /// <param name="lambdaBody">Verbatim C# expression body of the transformer lambda (parameter renamed to "value").</param>
    /// <param name="methodName">Name of the private static helper method emitted in the generated mapper.</param>
    public ValueTransformerDescriptor(string fullyQualifiedTypeName, string friendlyTypeName, string lambdaBody, string methodName)
    {
        FullyQualifiedTypeName = fullyQualifiedTypeName;
        FriendlyTypeName = friendlyTypeName;
        LambdaBody = lambdaBody;
        MethodName = methodName;
    }

    /// <inheritdoc/>
    public bool Equals(ValueTransformerDescriptor? other)
        => other is not null
            && FullyQualifiedTypeName == other.FullyQualifiedTypeName
            && LambdaBody == other.LambdaBody;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ValueTransformerDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = 17;
        h = h * 31 + FullyQualifiedTypeName.GetHashCode();
        h = h * 31 + LambdaBody.GetHashCode();
        return h;
    }
}

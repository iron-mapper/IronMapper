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

    public ValueTransformerDescriptor(string fullyQualifiedTypeName, string friendlyTypeName, string lambdaBody, string methodName)
    {
        FullyQualifiedTypeName = fullyQualifiedTypeName;
        FriendlyTypeName = friendlyTypeName;
        LambdaBody = lambdaBody;
        MethodName = methodName;
    }

    public bool Equals(ValueTransformerDescriptor? other)
        => other is not null
            && FullyQualifiedTypeName == other.FullyQualifiedTypeName
            && LambdaBody == other.LambdaBody;

    public override bool Equals(object? obj) => Equals(obj as ValueTransformerDescriptor);

    public override int GetHashCode()
    {
        var h = 17;
        h = h * 31 + FullyQualifiedTypeName.GetHashCode();
        h = h * 31 + LambdaBody.GetHashCode();
        return h;
    }
}

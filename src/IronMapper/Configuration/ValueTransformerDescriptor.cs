using System;

namespace IronMapper.Configuration;

/// <summary>Describes a profile-level value transformer registered via AddTransformer.</summary>
public sealed class ValueTransformerDescriptor
{
    /// <summary>The CLR type that this transformer handles.</summary>
    public Type ValueType { get; }

    /// <summary>Name of the generated private helper method in the mapper.</summary>
    public string TransformerMethodName { get; }

    /// <summary>The delegate for runtime fallback execution.</summary>
    public Delegate TransformerDelegate { get; }

    public ValueTransformerDescriptor(Type valueType, string transformerMethodName, Delegate transformerDelegate)
    {
        ValueType = valueType;
        TransformerMethodName = transformerMethodName;
        TransformerDelegate = transformerDelegate;
    }
}

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

    /// <summary>Initialises a new <see cref="ValueTransformerDescriptor"/>.</summary>
    /// <param name="valueType">The CLR type this transformer handles.</param>
    /// <param name="transformerMethodName">Name of the generated private helper method in the mapper.</param>
    /// <param name="transformerDelegate">The delegate for runtime fallback execution.</param>
    public ValueTransformerDescriptor(Type valueType, string transformerMethodName, Delegate transformerDelegate)
    {
        ValueType = valueType;
        TransformerMethodName = transformerMethodName;
        TransformerDelegate = transformerDelegate;
    }
}

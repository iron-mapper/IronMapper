using System;

namespace IronMapper.Generator.Analysis.Models;

/// <summary>How a profile creates the destination instead of a constructor the generator chooses.</summary>
internal enum FactoryKind
{
    /// <summary><c>ConstructWith(D.Restore)</c>: a static method, called with named arguments.</summary>
    Method,

    /// <summary><c>ConstructWith((string name, int age) =&gt; …)</c>: a private helper method with the lambda's parameters and body.</summary>
    Lambda,

    /// <summary><c>ConstructUsing(s =&gt; …)</c>: the expression as it is (with <c>source</c>), no parameters matched.</summary>
    ConstructUsing,
}

/// <summary>
/// The factory of a mapping (<see cref="MappingDescriptor.Factory"/>). The arguments of <see cref="FactoryKind.Method"/>
/// and <see cref="FactoryKind.Lambda"/> are <see cref="MappingDescriptor.ConstructorArguments"/>.
/// </summary>
internal sealed class FactoryDescriptor : IEquatable<FactoryDescriptor>
{
    public FactoryDescriptor(FactoryKind kind, string code, string? parameters, bool isBlock, string display)
    {
        Kind = kind;
        Code = code;
        Parameters = parameters;
        IsBlock = isBlock;
        Display = display;
    }

    public FactoryKind Kind { get; }

    /// <summary>
    /// <see cref="FactoryKind.Method"/>: the method to call (<c>global::App.Account.Restore</c>);
    /// <see cref="FactoryKind.Lambda"/>: the body of the helper (an expression, or the statements of a block, one per line);
    /// <see cref="FactoryKind.ConstructUsing"/>: the expression that creates the destination.
    /// </summary>
    public string Code { get; }

    /// <summary>The parameter list of the helper of <see cref="FactoryKind.Lambda"/> (<c>string name, int age</c>).</summary>
    public string? Parameters { get; }

    /// <summary>The body of <see cref="FactoryKind.Lambda"/> is a block (<see cref="Code"/> holds its statements).</summary>
    public bool IsBlock { get; }

    /// <summary>For messages: <c>factory 'Account.Restore'</c>, <c>the lambda of ConstructWith</c>, <c>ConstructUsing</c>.</summary>
    public string Display { get; }

    /// <inheritdoc/>
    public bool Equals(FactoryDescriptor? other)
        => other is not null
           && Kind == other.Kind
           && Code == other.Code
           && Parameters == other.Parameters
           && IsBlock == other.IsBlock
           && Display == other.Display;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as FactoryDescriptor);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + (int)Kind;
        hash = hash * 31 + Code.GetHashCode();
        hash = hash * 31 + (Parameters?.GetHashCode() ?? 0);
        hash = hash * 31 + IsBlock.GetHashCode();
        hash = hash * 31 + Display.GetHashCode();
        return hash;
    }
}

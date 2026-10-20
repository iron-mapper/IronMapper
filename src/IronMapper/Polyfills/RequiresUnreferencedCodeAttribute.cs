#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Polyfill for netstandard2.0: the trimmer and the trim/AOT analyzers recognize the attribute by its full name, so an
/// application that gets the netstandard2.0 assembly (e.g. on .NET 8) still sees the warning at its own call site.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
internal sealed class RequiresUnreferencedCodeAttribute : Attribute
{
    public RequiresUnreferencedCodeAttribute(string message) => Message = message;

    public string Message { get; }

    public string? Url { get; set; }
}
#endif

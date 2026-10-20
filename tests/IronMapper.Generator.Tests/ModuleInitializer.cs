using System.Runtime.CompilerServices;
using VerifyTests;

namespace IronMapper.Generator.Tests;

internal static class ModuleInitializer
{
    /// <summary>Registers the Verify converters for <c>GeneratorDriver</c> results (snapshot tests).</summary>
    [ModuleInitializer]
    internal static void Initialize() => VerifySourceGenerators.Initialize();
}

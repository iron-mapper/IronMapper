using IronMapper.Extensions.DI;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>IronMapperOptions.AddConverter&lt;T&gt;()</c> does nothing for mapping (the generated code creates converters
/// itself), so a call of it is <c>CS0618</c> in the user's build, an error with <c>TreatWarningsAsErrors</c>.
/// </summary>
public class AddConverterObsoleteTests
{
    [Fact]
    public void AddConverter_Call_IsCS0618()
    {
        const string source = """
            using IronMapper.Extensions.DI;
            using IronMapper.Interfaces;
            namespace App;
            public sealed class Upper : ITypeConverter<string, string> { public string Convert(string source) => source.ToUpperInvariant(); }
            public static class Startup
            {
                public static IronMapperOptions Configure(IronMapperOptions options) => options.AddConverter<Upper>();
            }
            """;

        MetadataReference diReference = MetadataReference.CreateFromFile(typeof(IronMapperOptions).Assembly.Location);
        (_, IReadOnlyList<Diagnostic> diagnostics) = GeneratorTestHelper.CompileProject("Obsolete", source, diReference);

        Diagnostic obsolete = Assert.Single(diagnostics);
        Assert.Equal("CS0618", obsolete.Id);
        Assert.Contains("AddConverter<T>() does nothing for mapping", obsolete.GetMessage());
        Assert.Contains("Remove the call. It will be removed in 3.0.0.", obsolete.GetMessage());
    }
}

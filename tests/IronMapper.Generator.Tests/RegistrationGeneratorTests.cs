using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// The registration of the generated mappings for <c>IMapper</c> (<c>GeneratedMappers.Registration.g.cs</c>,
/// plan item 1.3): module initializer, C# 8, assemblies without mappings.
/// </summary>
public class RegistrationGeneratorTests
{
    private const string Source = """
        using IronMapper.Attributes;
        using IronMapper.Generated;
        using IronMapper.Registration;

        namespace App
        {
            [MapTo(typeof(UserDto))]
            public class User { public int Id { get; set; } }

            public class UserDto { public int Id { get; set; } }
        }

        public static class Program
        {
            public static string Run()
            {
                // The module initializer has run (this type is in the module); the registry registers on first use.
                MapperRegistry.EnsureInitialized();
                var map = MapperCache<App.User, App.UserDto>.Map;
                if (map is null) return "not registered";
                var list = MapperRegistry.TryGetMap(typeof(System.Collections.Generic.List<App.User>),
                    typeof(App.UserDto[]), out var toArray);
                var array = (App.UserDto[])toArray(new System.Collections.Generic.List<App.User> { new App.User { Id = 2 } })!;
                return map(new App.User { Id = 5 }).Id + "," + list + "," + array[0].Id;
            }
        }
        """;

    [Fact]
    public void ModuleInitializer_RegistersTheMappings()
    {
        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(Source);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        var registration = Assert.Single(generated, s => s.Contains("RegisterIronMapperMappings"));
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", registration);
        Assert.DoesNotContain("class ModuleInitializerAttribute", registration);
        Assert.Equal("5,True,2", result);
    }

    [Fact]
    public void FeatureSwitchOff_ModuleInitializerRegistersNothing()
    {
        // Process-wide: the other tests of this class (the only ones that need the registration) do not run in parallel
        // with it. The module initializer of the compiled assembly runs when Program.Run is called.
        AppContext.SetSwitch(IronMapper.Registration.MapperRegistry.FeatureSwitchName, false);
        try
        {
            var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(Source);

            Assert.Empty(diagnostics);
            Assert.Empty(errors);
            Assert.Contains("if (global::IronMapper.Registration.MapperRegistry.IsSupported)",
                Assert.Single(generated, s => s.Contains("RegisterIronMapperMappings")));
            Assert.Equal("not registered", result);
        }
        finally
        {
            AppContext.SetSwitch(IronMapper.Registration.MapperRegistry.FeatureSwitchName, true);
        }
    }

    [Fact]
    public void CSharp8_NoModuleInitializer_CompilesWithoutWarnings()
    {
        var csharp8 = new CSharpParseOptions(LanguageVersion.CSharp8);
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { Source })
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(Source, csharp8));

        var driver = CSharpGeneratorDriver.Create(new[] { new IronMapperGenerator().AsSourceGenerator() }, parseOptions: csharp8)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var registration = Assert.Single(driver.GetRunResult().GeneratedTrees, t => t.ToString().Contains("RegisterIronMapperMappings")).ToString();
        Assert.DoesNotContain("ModuleInitializer]", registration);
        Assert.Contains("internal static void RegisterIronMapperMappings()", registration);
    }

    [Fact]
    public void NoMappings_NoRegistration()
    {
        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile("""
            namespace App;
            public class Plain { }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.DoesNotContain(generated, s => s.Contains("RegisterIronMapperMappings"));
    }
}

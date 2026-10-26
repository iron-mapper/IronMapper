using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IronMapper.Generator.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Optional overloads (<c>IronMapperGenerateInPlace/List/Array</c>, <c>GenerateInPlace/List/Array</c> of the attributes,
/// <c>GenerateMethods(...)</c> in profiles) and the attributes of the generated class (<c>[GeneratedCode]</c>,
/// <c>IronMapperExcludeFromCodeCoverage</c>). Each case compiles and runs the generated code: the single-object method
/// and the <c>IMapper</c> registry (List, array, in-place) work whatever is turned off. <c>IMapper</c> through DI:
/// <c>tests/IronMapper.Integration.Tests/TurnedOffOverloadsTests.cs</c>.
/// </summary>
[Collection(RegistryCollection.Name)]
public class GeneratedMethodsTests
{
    private const string Types = """
        namespace App
        {
            public class User { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class UserDto { public int Id { get; set; } public string Name { get; set; } = ""; }
        }
        """;

    // Maps through the generated single-object method and through the registry used by IMapper: List<T>, T[] and
    // in-place, whether the public overloads exist or not.
    private const string Program = """
        namespace App
        {
            using System.Collections.Generic;
            using IronMapper.Generated;
            using IronMapper.Registration;

            public static class Program
            {
                public static string Run()
                {
                    var user = new User { Id = 1, Name = "a" };
                    var dto = user.MapToUserDto();
                    var list = (List<UserDto>)MapperRegistry.Map(new[] { user }, typeof(User[]), typeof(List<UserDto>))!;
                    var array = (UserDto[])MapperRegistry.Map(new List<User> { user }, typeof(List<User>), typeof(UserDto[]))!;
                    MapperRegistry.TryGetMapInto(typeof(User), typeof(UserDto), out var mapInto);
                    var target = new UserDto();
                    mapInto(user, target);
                    return $"{dto.Name}{list[0].Name}{array.Length}{array[0].Name}{target.Name}";
                }
            }
        }
        """;

    private const string Expected = "aa1aa";

    public static IEnumerable<object[]> Combinations()
    {
        foreach (string? how in new[] { "msbuild", "attribute", "profile" })
        {
            foreach (bool inPlace in new[] { true, false })
            {
                foreach (bool list in new[] { true, false })
                {
                    foreach (bool array in new[] { true, false })
                    {
                        yield return new object[] { how, inPlace, list, array };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryCombination_OnlyEnabledPublicMethods_RegistryStillMapsEverything(string how, bool inPlace, bool list, bool array)
    {
        var properties = new Dictionary<string, string>();
        string profile = "";
        switch (how)
        {
            case "msbuild":
                properties["IronMapperGenerateInPlace"] = Bool(inPlace);
                properties["IronMapperGenerateList"] = Bool(list);
                properties["IronMapperGenerateArray"] = Bool(array);
                break;
            case "profile":
                profile = $$"""
                    using IronMapper.Configuration;

                    namespace App
                    {
                        public class P : MappingProfile
                        {
                            public P() { CreateMap<User, UserDto>().GenerateMethods(inPlace: {{Bool(inPlace)}}, list: {{Bool(list)}}, array: {{Bool(array)}}); }
                        }
                    }
                    """;
                break;
        }

        var result = Run(new[] { UserTypes(how, inPlace, list, array), profile, Program }, properties);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(Expected, result.Output);
        Assert.Contains("MapToUserDto/1", result.PublicMethods);
        Assert.Equal(inPlace, result.PublicMethods.Contains("MapToUserDto/2"));
        Assert.Equal(list, result.PublicMethods.Contains("MapToUserDtoList/1"));
        Assert.Equal(array, result.PublicMethods.Contains("MapToUserDtoArray/1"));
        // A turned-off in-place overload stays as a private method for IMapper.
        Assert.Equal(!inPlace, result.Sources.Any(s => s.Contains("private static void MapInto_User_UserDto(")));
    }

    // [MapTo] on the source type: with its Generate* properties (attribute), without them (MSBuild), none (profile).
    private static string UserTypes(string how, bool inPlace, bool list, bool array) => how switch
    {
        "attribute" => Types.Replace("public class User {",
            $"[IronMapper.Attributes.MapTo(typeof(UserDto), GenerateInPlace = {Bool(inPlace)}, GenerateList = {Bool(list)}, GenerateArray = {Bool(array)})] public class User {{"),
        "msbuild" => Types.Replace("public class User {", "[IronMapper.Attributes.MapTo(typeof(UserDto))] public class User {"),
        _ => Types,
    };

    private static string Bool(bool value) => value ? "true" : "false";

    [Fact]
    public void AssemblyOff_PerMappingOn_ReenablesOnlyThatMapping()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App
            {
                [MapTo(typeof(ADto), GenerateList = true)] public class A { public int Id { get; set; } }
                public class ADto { public int Id { get; set; } }
                [MapTo(typeof(BDto))] public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }
                public class C { public int Id { get; set; } }
                public class CDto { public int Id { get; set; } }
                public class P : MappingProfile { public P() { CreateMap<C, CDto>().GenerateMethods(array: true, inPlace: null); } }
            }
            """;

        var result = Run(new[] { source }, AllOff());

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "MapToADto/1", "MapToADtoList/1", "MapToBDto/1", "MapToCDto/1", "MapToCDtoArray/1" },
            result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void AssemblyOn_PerMappingOff_TurnsOffOnlyThatMapping()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App
            {
                [MapTo(typeof(ADto), GenerateArray = false)] public class A { public int Id { get; set; } }
                public class ADto { public int Id { get; set; } }
                [MapFrom(typeof(B), GenerateInPlace = false)] public class BDto { public int Id { get; set; } }
                public class B { public int Id { get; set; } }
            }
            """;

        var result = Run(new[] { source }, new Dictionary<string, string>());

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "MapToADto/1", "MapToADto/2", "MapToADtoList/1", "MapToBDto/1", "MapToBDtoArray/1", "MapToBDtoList/1" },
            result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void NestedCollections_WorkWithoutListAndArrayOfTheElementMapping()
    {
        string source = """
            using System.Collections.Generic;
            using System.Linq;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App
            {
                [MapTo(typeof(LineDto))] public class Line { public string Code { get; set; } = ""; }
                public class LineDto { public string Code { get; set; } = ""; }
                [MapTo(typeof(OrderDto))]
                public class Order
                {
                    public List<Line> AsList { get; set; } = new();
                    public Line[] AsArray { get; set; } = System.Array.Empty<Line>();
                    public IEnumerable<Line> Lazy { get; set; } = Enumerable.Empty<Line>();
                }
                public class OrderDto
                {
                    public List<LineDto> AsList { get; set; } = new();
                    public LineDto[] AsArray { get; set; } = System.Array.Empty<LineDto>();
                    public IEnumerable<LineDto> Lazy { get; set; } = Enumerable.Empty<LineDto>();
                }

                public static class Program
                {
                    public static string Run()
                    {
                        var lines = new[] { new Line { Code = "x" }, new Line { Code = "y" } };
                        var dto = new Order { AsList = lines.ToList(), AsArray = lines, Lazy = lines }.MapToOrderDto();
                        return string.Concat(dto.AsList.Select(l => l.Code)) + string.Concat(dto.AsArray.Select(l => l.Code))
                               + string.Concat(dto.Lazy.Select(l => l.Code));
                    }
                }
            }
            """;

        var result = Run(new[] { source }, AllOff());

        Assert.Empty(result.Errors);
        Assert.Equal("xyxyxy", result.Output);
        Assert.Equal(new[] { "MapToLineDto/1", "MapToOrderDto/1" }, result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void QualifiedNames_SameAsWithEveryOverload()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App
            {
                [MapTo(typeof(Admin.UserDto), GenerateList = false)]
                [MapTo(typeof(Public.UserDto), GenerateArray = false, GenerateInPlace = false)]
                public class User { public int Id { get; set; } }
            }
            namespace App.Admin { public class UserDto { public int Id { get; set; } } }
            namespace App.Public { public class UserDto { public int Id { get; set; } } }
            """;

        var result = Run(new[] { source }, new Dictionary<string, string>());

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, result.Diagnostics.Select(d => d.Id));
        Assert.Equal(
            new[] { "MapToAdminUserDto/1", "MapToAdminUserDto/2", "MapToAdminUserDtoArray/1", "MapToPublicUserDto/1", "MapToPublicUserDtoList/1" },
            result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void GenerateMethods_AppliesToTheMappingNotToItsReverse()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }
                public class P : MappingProfile
                {
                    public P() { CreateMap<A, B>().GenerateMethods(inPlace: false, list: false, array: false).ReverseMap(); }
                }
            }
            """;

        var result = Run(new[] { source }, new Dictionary<string, string>());

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "MapToA/1", "MapToA/2", "MapToAArray/1", "MapToAList/1", "MapToB/1" },
            result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void GenerateMethods_ConstantsAreRead_NonConstantIsIM0016AndKeepsTheAssemblyValue()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }
                public class P : MappingProfile
                {
                    private const bool No = false;
                    public P()
                    {
                        var off = false;
                        CreateMap<A, B>().GenerateMethods(list: No, array: off);
                    }
                }
            }
            """;

        var result = Run(new[] { source }, new Dictionary<string, string>());

        Assert.Empty(result.Errors);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("IM0016", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Argument 'array' of GenerateMethods in MappingProfile 'App.P' is not a constant ('array: off')", diagnostic.GetMessage());
        Assert.Equal("array: off", diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        // list: No (a constant) turns MapToBList off; array: off is not read, so MapToBArray stays.
        Assert.Equal(new[] { "MapToB/1", "MapToB/2", "MapToBArray/1" }, result.PublicMethods.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void GeneratedCode_Always_ExcludeFromCodeCoverage_WithTheProperty(string? value, bool excluded)
    {
        var properties = new Dictionary<string, string>();
        if (value is not null)
        {
            properties["IronMapperExcludeFromCodeCoverage"] = value;
        }

        var result = Run(new[] { Types.Replace("public class User {", "[IronMapper.Attributes.MapTo(typeof(UserDto))] public class User {") }, properties);

        Assert.Empty(result.Errors);
        var generatedCode = Assert.Single(result.ClassAttributes.OfType<System.CodeDom.Compiler.GeneratedCodeAttribute>());
        Assert.Equal("IronMapper", generatedCode.Tool);
        Assert.Equal(GeneratorVersion(), generatedCode.Version);
        Assert.Equal(excluded, result.ClassAttributes.OfType<System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute>().Any());
        // Declared once, on the class declaration: it covers the mapping files and the registration (partial parts).
        Assert.Single(result.Sources, s => s.Contains("GeneratedCode(\"IronMapper\""));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void GeneratedCodeAttribute_OnByDefault_OffWithTheProperty(string? value, bool marked)
    {
        var properties = new Dictionary<string, string>();
        if (value is not null)
        {
            properties["IronMapperGeneratedCodeAttribute"] = value;
        }

        var result = Run(new[] { Types.Replace("public class User {", "[IronMapper.Attributes.MapTo(typeof(UserDto))] public class User {") }, properties);

        Assert.Empty(result.Errors);
        Assert.Equal(marked, result.ClassAttributes.OfType<System.CodeDom.Compiler.GeneratedCodeAttribute>().Any());
        Assert.Equal(marked, result.Sources.Any(s => s.Contains("GeneratedCode(\"IronMapper\"")));
        // Independent of IronMapperExcludeFromCodeCoverage (off here).
        Assert.DoesNotContain(result.ClassAttributes, a => a is System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute);
    }

    [Fact]
    public void ChangingGeneratedCodeAttribute_RegeneratesOnlyTheClassDeclaration()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[]
        {
            Types.Replace("public class User {", "[IronMapper.Attributes.MapTo(typeof(UserDto))] public class User {"),
        });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new IronMapperGenerator().AsSourceGenerator() },
            optionsProvider: TestOptionsProvider.FromBuildProperties(new Dictionary<string, string>()),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        driver = driver.WithUpdatedAnalyzerConfigOptions(TestOptionsProvider.FromBuildProperties(
            new Dictionary<string, string> { ["IronMapperGeneratedCodeAttribute"] = "false" }));
        var result = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Results);

        var changed = result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(step => step.Outputs)
            .Where(o => o.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New)
            .ToList();
        Assert.Single(changed);
        Assert.Contains(result.GeneratedSources, s => s.HintName == "GeneratedMappers.g.cs"
            && !s.SourceText.ToString().Contains("GeneratedCode("));
    }

    [Fact]
    public void ChangingExcludeFromCodeCoverage_RegeneratesOnlyTheClassDeclaration()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[]
        {
            Types.Replace("public class User {", "[IronMapper.Attributes.MapTo(typeof(UserDto))] public class User {"),
        });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new IronMapperGenerator().AsSourceGenerator() },
            optionsProvider: TestOptionsProvider.FromBuildProperties(new Dictionary<string, string>()),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        driver = driver.WithUpdatedAnalyzerConfigOptions(TestOptionsProvider.FromBuildProperties(
            new Dictionary<string, string> { ["IronMapperExcludeFromCodeCoverage"] = "true" }));
        var result = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Results);

        var changed = result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(step => step.Outputs)
            .Where(o => o.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New)
            .ToList();
        Assert.Single(changed);
        Assert.Contains(result.GeneratedSources, s => s.HintName == "GeneratedMappers.g.cs"
            && s.SourceText.ToString().Contains("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]"));
    }

    private static Dictionary<string, string> AllOff() => new()
    {
        ["IronMapperGenerateInPlace"] = "false",
        ["IronMapperGenerateList"] = "false",
        ["IronMapperGenerateArray"] = "false",
    };

    private static string GeneratorVersion()
    {
        string version = typeof(IronMapperGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        int plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    private sealed record Result(
        IReadOnlyList<Diagnostic> Errors,
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyList<string> Sources,
        IReadOnlySet<string> PublicMethods,
        IReadOnlyList<Attribute> ClassAttributes,
        string? Output);

    /// <summary>
    /// Runs the generator with the MSBuild <paramref name="properties"/>, compiles, loads the assembly and returns the
    /// public methods of the generated class (<c>name/parameter count</c>) and the result of <c>Program.Run()</c> if
    /// the sources declare it.
    /// </summary>
    private static Result Run(string[] sources, Dictionary<string, string> properties)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(sources.Where(s => s.Length > 0));
        CSharpGeneratorDriver.Create(
                new[] { new IronMapperGenerator().AsSourceGenerator() },
                optionsProvider: TestOptionsProvider.FromBuildProperties(properties))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var generated = output.SyntaxTrees.Skip(compilation.SyntaxTrees.Count()).Select(t => t.ToString()).ToList();
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        || (d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true))
            .ToList();
        if (errors.Count > 0)
        {
            return new Result(errors, generatorDiagnostics, generated, new HashSet<string>(), Array.Empty<Attribute>(), null);
        }

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));

        var context = new System.Runtime.Loader.AssemblyLoadContext($"methods-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var mappers = assembly.GetType("IronMapper.Generated." + GeneratedClassName.For(assembly.GetName().Name), throwOnError: true)!;
            var methods = mappers.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(m => $"{m.Name}/{m.GetParameters().Length}").ToHashSet();
            var attributes = mappers.GetCustomAttributes(inherit: false).Cast<Attribute>().ToList();
            var program = assembly.GetTypes().SingleOrDefault(t => t.Name == "Program");
            string? run = (string?)program?.GetMethod("Run")!.Invoke(null, null);
            return new Result(errors, generatorDiagnostics, generated, methods, attributes, run);
        }
        finally
        {
            context.Unload();
        }
    }
}

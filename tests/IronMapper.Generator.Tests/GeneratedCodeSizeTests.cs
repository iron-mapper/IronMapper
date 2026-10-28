using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Item 7 of the 2.1.0 PR order (volume of the generated code): <c>…Core</c> only for pairs that something calls so,
/// the in-place overloads that change nothing (<c>[Obsolete]</c>, <c>IMapper</c> throws), and
/// <c>IronMapperRegistration=false</c> (item 2.16 of the plan: no registration, no private <c>MapInto</c>). The shape of
/// a mapping that is not nested anywhere is the snapshot <c>NotNestedMapping_SameCodeAs200</c>.
/// </summary>
[Collection(RegistryCollection.Name)]
public class GeneratedCodeSizeTests
{
    private static readonly Dictionary<string, string> WithoutRegistration = new() { ["IronMapperRegistration"] = "false" };

    // A value-type destination, ConvertUsing, only init properties: the in-place overload changes nothing for the caller.
    private const string NoOpMappings = """
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App
        {
            [MapTo(typeof(PointDto))]
            public class Point { public int X { get; set; } }
            public struct PointDto { public int X { get; set; } }

            public class Price { public decimal Amount { get; set; } }
            public class PriceDto { public string Text { get; set; } = ""; }

            [MapTo(typeof(TagDto))]
            public class Tag { public string Name { get; set; } = ""; }
            public class TagDto { public string Name { get; init; } = ""; }

            public class PriceProfile : MappingProfile
            {
                public PriceProfile() { CreateMap<Price, PriceDto>().ConvertUsing(p => new PriceDto { Text = p.Amount.ToString() }); }
            }
        }
        """;

    [Fact]
    public void InPlaceOverloadThatChangesNothing_Obsolete_EachCallWarnsWithTheReason()
    {
        string source = NoOpMappings + """

            namespace App
            {
                using IronMapper.Generated;

                public static class Calls
                {
                    public static void Run(PointDto point, PriceDto price, TagDto tag)
                    {
                        new Point().MapToPointDto(point);
                        new Price().MapToPriceDto(price);
                        new Tag().MapToTagDto(tag);
                    }
                }
            }
            """;

        var (_, warningsAndErrors) = GeneratorTestHelper.CompileProject("App", source);

        // Only the three calls: nothing in the generated files (the registration does not call these overloads).
        var obsolete = warningsAndErrors.Where(d => d.Id == "CS0618").ToList();
        Assert.Equal(3, obsolete.Count);
        Assert.All(obsolete, d => Assert.False(d.Location.SourceTree!.FilePath.EndsWith(".g.cs")));
        Assert.DoesNotContain(warningsAndErrors, d => d.Id != "CS0618");
        string messages = string.Join("\n", obsolete.Select(d => d.GetMessage()));
        Assert.Contains("MapToPointDto(source, destination) does nothing: PointDto is a value type", messages);
        Assert.Contains("MapToPriceDto(source, destination) does nothing: the mapping uses ConvertUsing", messages);
        Assert.Contains("MapToTagDto(source, destination) does nothing: TagDto has no property that an existing object can be updated with", messages);
        Assert.Contains("(destination = source.MapToTagDto())", messages);
        Assert.Contains("It will be removed in IronMapper 3.0.0.", messages);
    }

    [Fact]
    public void InPlaceOverloadThatChangesNothing_IMapperThrowsMappingException()
    {
        string source = NoOpMappings + """

            public static class Program
            {
                public static string Run()
                {
                    string Try(System.Type s, System.Type d, object source, object destination)
                    {
                        if (!IronMapper.Registration.MapperRegistry.TryGetMapInto(s, d, out var mapInto)) return "none";
                        try { mapInto(source, destination); return "no exception"; }
                        catch (IronMapper.Exceptions.MappingException e) { return e.Message; }
                    }

                    return Try(typeof(App.Point), typeof(App.PointDto), new App.Point(), new App.PointDto()) + "\n"
                         + Try(typeof(App.Price), typeof(App.PriceDto), new App.Price(), new App.PriceDto()) + "\n"
                         + Try(typeof(App.Tag), typeof(App.TagDto), new App.Tag(), new App.TagDto());
                }
            }
            """;

        var (_, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        Assert.Empty(errors);
        Assert.Equal(
            "No in-place mapping from Point to PointDto: PointDto is a value type, so the destination is passed by copy and the caller's value does not change. Map to a new object instead.\n"
            + "No in-place mapping from Price to PriceDto: the mapping uses ConvertUsing, which only creates a new object. Map to a new object instead.\n"
            + "No in-place mapping from Tag to TagDto: TagDto has no property that an existing object can be updated with (every mapped property is init-only). Map to a new object instead.",
            run);
    }

    [Fact]
    public void ReferencedAssemblysObsoleteInPlaceOverload_NotCalled_NestedObjectGetsANewInstance()
    {
        const string library = """
            using IronMapper.Attributes;

            namespace Lib;

            [MapTo(typeof(TagDto))]
            public class Tag { public string Name { get; set; } = ""; }
            public class TagDto { public string Name { get; init; } = ""; }
            """;
        const string app = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(PostDto))]
            public class Post { public Lib.Tag Tag { get; set; } = new(); }
            public class PostDto { public Lib.TagDto Tag { get; set; } = new(); }
            """;
        var (libraryCompilation, libraryDiagnostics) = GeneratorTestHelper.CompileProject("Lib", library);
        Assert.Empty(libraryDiagnostics);

        var (appCompilation, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app,
            GeneratorTestHelper.EmitReference(libraryCompilation).reference);

        // No CS0618 in the generated file: the in-place update assigns a new TagDto, as for such a pair of this compilation.
        Assert.Empty(appDiagnostics);
        string post = appCompilation.SyntaxTrees.Select(t => t.ToString()).Single(s => s.Contains("MapToPostDto(this global::App.Post source)"));
        Assert.Matches(@"destination\.Tag = \(source\.Tag \?\? NestedObjectIsNull<global::Lib\.Tag>\(""[^""]*""\)\)\.MapToTagDto\(\);", post);
        Assert.DoesNotContain("MapToTagDto(destination.Tag)", post);
    }

    [Fact]
    public void InPlaceCoreOfANestedPair_OnlyWhenAnInPlaceBodyOfTheParentIsGenerated()
    {
        const string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto), GenerateInPlace = false)]
            public class Order { public Customer Customer { get; set; } = new(); }
            public class OrderDto { public CustomerDto Customer { get; set; } = new(); }

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string Name { get; set; } = ""; }
            """;

        // With the registration the private MapInto of Order updates the existing customer through its in-place …Core.
        var (registered, _, registeredErrors) = GeneratorTestHelper.RunGeneratorAndCompile(source);
        Assert.Empty(registeredErrors);
        Assert.Contains(registered, s => s.Contains("private static void MapToCustomerDtoCore(global::App.Customer source, global::App.CustomerDto destination)"));
        Assert.Contains(registered, s => s.Contains("else MapToCustomerDtoCore(source.Customer, destination.Customer);"));

        // Without it Order has no in-place code at all, so nothing updates a customer in place.
        var (unregistered, _, unregisteredErrors) = GeneratorTestHelper.RunGeneratorAndCompile(source, WithoutRegistration);
        Assert.Empty(unregisteredErrors);
        Assert.DoesNotContain(unregistered, s => s.Contains("MapToCustomerDtoCore(global::App.Customer source, global::App.CustomerDto destination)"));
        Assert.DoesNotContain(unregistered, s => s.Contains("MapInto"));
        // The new-object …Core stays: Order creates its customer through it.
        Assert.Contains(unregistered, s => s.Contains("Customer = MapToCustomerDtoCore(source.Customer"));
    }

    [Fact]
    public void SelfReferencingMapping_UsesItsOwnCore()
    {
        const string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(EmployeeDto))]
            public class Employee { public string Name { get; set; } = ""; public Employee? Manager { get; set; } }
            public class EmployeeDto { public string Name { get; set; } = ""; public EmployeeDto? Manager { get; set; } }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(errors);
        string code = generated.Single(s => s.Contains("MapToEmployeeDto(this global::App.Employee source)"));
        Assert.Contains("Manager = source.Manager is null ? null : MapToEmployeeDtoCore(source.Manager),", code);
        Assert.Contains("else MapToEmployeeDtoCore(source.Manager, destination.Manager);", code);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData(" False ", false)]
    [InlineData("true", true)]
    [InlineData("", true)]
    [InlineData("no", true)]
    public void IronMapperRegistration_OnlyFalseTurnsTheRegistrationOff(string value, bool registered)
    {
        const string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(UserDto), GenerateInPlace = false)]
            public class User { public int Id { get; set; } }
            public class UserDto { public int Id { get; set; } }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source,
            new Dictionary<string, string> { ["IronMapperRegistration"] = value });

        Assert.Empty(errors);
        Assert.Equal(registered, generated.Any(s => s.Contains("RegisterIronMapperMappingsCore")));
        Assert.Equal(registered, generated.Any(s => s.Contains("MapInto_User_UserDto")));
    }

    [Fact]
    public void RegistrationTurnedOff_IMapperErrorNamesTheProperty()
    {
        const string source = """
            using IronMapper.Attributes;

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
                    try
                    {
                        IronMapper.Registration.MapperRegistry.Map(new App.User(), typeof(App.User), typeof(App.UserDto));
                        return "mapped";
                    }
                    catch (IronMapper.Exceptions.MappingException e) { return e.Message; }
                }
            }
            """;

        var (_, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source, WithoutRegistration);

        Assert.Empty(errors);
        Assert.StartsWith("No mapping registered from 'User' to 'UserDto'.", run);
        // No marker in the assembly: reading one would add the custom attribute search of the runtime to every NativeAOT
        // app with IMapper (7–15 KB in tests/IronMapper.AotProbe.DI), only for this message.
        Assert.Contains("A project built with <IronMapperRegistration>false</IronMapperRegistration> has no registration", run);
    }

    [Fact]
    public void ChangingIronMapperRegistration_RemovesTheRegistration_KeepsTheMappingFile()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[]
        {
            """
            namespace App
            {
                [IronMapper.Attributes.MapTo(typeof(UserDto))] public class User { public int Id { get; set; } }
                public class UserDto { public int Id { get; set; } }
            }
            """,
        });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new IronMapperGenerator().AsSourceGenerator() },
            optionsProvider: TestOptionsProvider.FromBuildProperties(new Dictionary<string, string>()));
        driver = driver.RunGenerators(compilation);
        string before = driver.GetRunResult().GeneratedTrees.Single(t => t.FilePath.EndsWith("App.User_MapTo_App.UserDto.g.cs")).ToString();

        driver = driver.WithUpdatedAnalyzerConfigOptions(TestOptionsProvider.FromBuildProperties(WithoutRegistration));
        var result = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Results);

        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName == "GeneratedMappers.Registration.g.cs");
        // The public in-place overload is generated: no MapInto either way, the file is the same.
        Assert.Equal(before, result.GeneratedSources.Single(s => s.HintName == "App.User_MapTo_App.UserDto.g.cs").SourceText.ToString());
    }
}

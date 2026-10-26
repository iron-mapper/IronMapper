using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// IM0004 (warning, decision of 23.10.2026): a cycle of generated mappings through at least one automatically mapped
/// nested object. A cycle only through collections (a tree) is not reported, and a property of the chain configured with
/// <c>Ignore</c> or <c>MapFrom</c> breaks the cycle. A cycle of types recurses forever only when the data has a cycle, so
/// finite chains (<c>Employee.Manager</c>) are mapped.
/// </summary>
public class CyclicMappingTests
{
    private static Diagnostic[] IM0004(IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == "IM0004").ToArray();

    private static string At(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    [Fact]
    public void TreeThroughCollection_NoIM0004()
    {
        string source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(CategoryDto))]
            public class Category { public string Name { get; set; } = ""; public List<Category> Children { get; set; } = new(); }
            public class CategoryDto { public string Name { get; set; } = ""; public List<CategoryDto> Children { get; set; } = new(); }

            public static class Program
            {
                public static string Run()
                {
                    var root = new Category { Name = "root", Children = { new Category { Name = "leaf" } } };
                    var dto = root.MapToCategoryDto();
                    return $"{dto.Name},{dto.Children[0].Name},{dto.Children[0].Children.Count}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("root,leaf,0", run);
    }

    [Fact]
    public void EmployeeManager_IM0004Warning_FiniteChainIsMapped()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(EmployeeDto))]
            public class Employee { public string Name { get; set; } = ""; public Employee? Manager { get; set; } }
            public class EmployeeDto { public string Name { get; set; } = ""; public EmployeeDto? Manager { get; set; } }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Employee { Name = "dev", Manager = new Employee { Name = "lead", Manager = new Employee { Name = "cto" } } }.MapToEmployeeDto();
                    return $"{dto.Manager!.Name},{dto.Manager.Manager!.Name},{dto.Manager.Manager.Manager is null}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("IM0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Manager", At(diagnostic));
        Assert.Equal("Property 'Manager' of 'App.EmployeeDto' is mapped automatically and closes a cycle of mappings: "
                     + "App.Employee → App.EmployeeDto, Manager: App.Employee → App.EmployeeDto. The mapping recurses without "
                     + "end only if the objects form a cycle at run time; break the cycle with Ignore or MapFrom on a property of "
                     + "the chain, or suppress IM0004 for models that are always finite (#pragma warning disable IM0004, or "
                     + "dotnet_diagnostic.IM0004.severity = none in .editorconfig).", diagnostic.GetMessage());
        Assert.Equal("lead,cto,True", run);
    }

    [Theory]
    [InlineData("[MapTo(typeof(EmployeeDto))] ", "[Ignore] ", null)]
    [InlineData("", "", ".ForMember(d => d.Manager, o => o.Ignore())")]
    [InlineData("", "", ".ForMember(d => d.Manager, o => o.MapFrom(s => s.Manager == null ? null : new EmployeeDto { Name = s.Manager.Name }))")]
    public void IgnoreOrMapFromOnThePath_BreaksTheCycle(string mapTo, string ignore, string? forMember)
    {
        // [Ignore] in an attribute mapping; a profile does not read [Ignore] (plan item 2.6), there it is ForMember.
        string profile = forMember is null ? "" : $$"""

            public class EmployeeProfile : MappingProfile
            {
                public EmployeeProfile() { CreateMap<Employee, EmployeeDto>(){{forMember}}; }
            }
            """;
        string source = $$"""
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            {{mapTo}}public class Employee { public string Name { get; set; } = ""; public Employee? Manager { get; set; } }
            public class EmployeeDto { public string Name { get; set; } = ""; {{ignore}}public EmployeeDto? Manager { get; set; } }
            """ + profile;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OrderCustomerOrders_IM0004WithTheFullChain_AtCreateMap()
    {
        string source = """
            using System.Collections.Generic;
            using IronMapper.Configuration;

            namespace App;

            public class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); }
            public class OrderDto { public int Id { get; set; } public CustomerDto Customer { get; set; } = new(); }
            public class Customer { public string Name { get; set; } = ""; public List<Order> Orders { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public List<OrderDto> Orders { get; set; } = new(); }

            public class ShopProfile : MappingProfile
            {
                public ShopProfile()
                {
                    CreateMap<Order, OrderDto>();
                    CreateMap<Customer, CustomerDto>();
                }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(IM0004(diagnostics));
        Assert.Equal("CreateMap<Order, OrderDto>()", At(diagnostic));
        Assert.Contains("Property 'Customer' of 'App.OrderDto' is mapped automatically and closes a cycle of mappings: "
                        + "App.Order → App.OrderDto, Customer: App.Customer → App.CustomerDto, Orders (elements): App.Order → App.OrderDto.",
            diagnostic.GetMessage());
        Assert.Single(diagnostics);
    }

    [Fact]
    public void TwoNestedObjectsOnTheCycle_ReportedAtEach()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(PersonDto))]
            public class Person { public Passport? Passport { get; set; } }
            public class PersonDto { public PassportDto? Passport { get; set; } }

            [MapTo(typeof(PassportDto))]
            public class Passport { public Person? Holder { get; set; } }
            public class PassportDto { public PersonDto? Holder { get; set; } }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var found = IM0004(diagnostics);
        Assert.Equal(new[] { "Holder", "Passport" }, found.Select(At).OrderBy(x => x, System.StringComparer.Ordinal));
        Assert.Contains(found, d => d.GetMessage().Contains("App.Person → App.PersonDto, Passport: App.Passport → App.PassportDto, Holder: App.Person → App.PersonDto."));
    }

    [Fact]
    public void CycleThroughConvertUsing_NoIM0004()
    {
        // The mapping of Customer calls the converter, not the mappings of its properties: no recursion.
        string source = """
            using System.Collections.Generic;
            using IronMapper.Configuration;

            namespace App;

            public class Order { public Customer Customer { get; set; } = new(); }
            public class OrderDto { public CustomerDto Customer { get; set; } = new(); }
            public class Customer { public List<Order> Orders { get; set; } = new(); }
            public class CustomerDto { public List<OrderDto> Orders { get; set; } = new(); }

            public class ShopProfile : MappingProfile
            {
                public ShopProfile()
                {
                    CreateMap<Order, OrderDto>();
                    CreateMap<Customer, CustomerDto>().ConvertUsing(s => new CustomerDto());
                }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0004(diagnostics));
    }

    [Fact]
    public void SeverityOption_SuppressesOrRaisesIM0004()
    {
        // The usual severity settings apply (.editorconfig, NoWarn, WarningsAsErrors). #pragma warning disable IM0004 is
        // applied by the compiler, not by the generator driver: checked by the build of tests/IronMapper.Tests
        // (TreatWarningsAsErrors; NestedObjectTests.cs, NestedProfile).
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(EmployeeDto))]
            public class Employee { public Employee? Manager { get; set; } }
            public class EmployeeDto { public EmployeeDto? Manager { get; set; } }
            """;

        Assert.Single(IM0004(Run(source, severity: null)));
        Assert.Empty(Run(source, severity: "none"));
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(Run(source, severity: "error")).Severity);
    }

    [Fact]
    public void ModelsThatCompileWith200_GetNoNewWarnings()
    {
        // What 2.0.0 compiles: trees and cycles through collections, nested objects through MapFrom (the documented
        // workaround), a nested property of the same type. With TreatWarningsAsErrors nothing new may appear.
        string source = """
            using System.Collections.Generic;
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Category { public string Name { get; set; } = ""; public List<Category> Children { get; set; } = new(); public Category? Parent { get; set; } }
            public class CategoryDto { public string Name { get; set; } = ""; public List<CategoryDto> Children { get; set; } = new(); public Category? Parent { get; set; } }
            public class Order { public Customer Customer { get; set; } = new(); public List<Line> Lines { get; set; } = new(); }
            public class OrderDto { public CustomerDto Customer { get; set; } = new(); public List<LineDto> Lines { get; set; } = new(); }
            public class Customer { public string Name { get; set; } = ""; public List<Order> Orders { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public List<OrderDto> Orders { get; set; } = new(); }
            public class Line { public Order? Order { get; set; } }
            public class LineDto { public OrderDto? Order { get; set; } }

            public class ShopProfile : MappingProfile
            {
                public ShopProfile()
                {
                    CreateMap<Category, CategoryDto>();
                    CreateMap<Customer, CustomerDto>();
                    CreateMap<Order, OrderDto>().ForMember(d => d.Customer, o => o.MapFrom(s => s.Customer.MapToCustomerDto()));
                    CreateMap<Line, LineDto>().ForMember(d => d.Order, o => o.Ignore());
                }
            }
            """;

        var (_, warningsAndErrors) = GeneratorTestHelper.CompileProject("Shop", source);

        Assert.Empty(warningsAndErrors);
    }

    private static IReadOnlyList<Diagnostic> Run(string source, string? severity)
    {
        var parseOptions = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);
        const string path = "/src/Employee.cs";
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path);
        var compilation = GeneratorTestHelper.CreateCompilation(System.Array.Empty<string>()).AddSyntaxTrees(tree);
        if (severity is not null)
        {
            var specific = new Dictionary<string, ReportDiagnostic>
            {
                ["IM0004"] = severity == "none" ? ReportDiagnostic.Suppress : ReportDiagnostic.Error,
            };
            compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(specific));
        }

        return CSharpGeneratorDriver.Create(new[] { new IronMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
            .RunGenerators(compilation).GetRunResult().Diagnostics;
    }
}

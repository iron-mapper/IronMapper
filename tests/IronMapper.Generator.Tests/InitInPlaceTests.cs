using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>init</c>-only properties in the in-place overload (issue #58, PR 10 of 2.1.0, plan item 2.15). A destination with
/// <c>set</c> and <c>init</c> properties: the in-place overload updates the settable ones and skips the <c>init</c>-only ones,
/// with warning IM0025 at the mapping that lists them (2.0.0 skipped them silently). An <c>init</c>-only nested object is
/// updated in place through its getter when the destination has one (2.0.0: skipped); <c>null</c> is listed in IM0025.
/// A destination with only <c>init</c> properties keeps the <c>[Obsolete]</c> overload of PR 7 (#66) and no IM0025.
/// </summary>
public class InitInPlaceTests
{
    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    private const string Usings = """
        using System.Collections.Generic;
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        """;

    [Fact]
    public void MixedType_InitSkipped_IM0025ListsThem_RequiredInitToo_IgnoredNotListed()
    {
        string source = Usings + """
            [MapTo(typeof(OrderDto))]
            public class Order
            {
                public string Name { get; set; } = "";
                public string Code { get; set; } = "";
                public int Version { get; set; }
                public string Ref { get; set; } = "";
                public string Note { get; set; } = "";
            }
            public class OrderDto
            {
                public string Name { get; set; } = "";
                public string Code { get; init; } = "";
                public int Version { get; init; }
                public required string Ref { get; init; }
                [Ignore] public string Note { get; init; } = "";
            }

            public static class Program
            {
                public static string Run()
                {
                    var existing = new OrderDto { Name = "old", Code = "old", Version = 1, Ref = "old" };
                    new Order { Name = "new", Code = "new", Version = 2, Ref = "new" }.MapToOrderDto(existing);
                    var created = new Order { Code = "c", Version = 3, Ref = "r" }.MapToOrderDto();
                    return $"{existing.Name},{existing.Code},{existing.Version},{existing.Ref};{created.Code},{created.Version},{created.Ref}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("new,old,1,old;c,3,r", run);
        var im0025 = Assert.Single(diagnostics);
        Assert.Equal("IM0025", im0025.Id);
        Assert.Equal(DiagnosticSeverity.Warning, im0025.Severity);
        Assert.Equal("In-place mapping from 'App.Order' to 'App.OrderDto' does not update every mapped property: Code, Version and Ref are init-only, so an existing object keeps their values. If they need not be mapped, mark them [Ignore] (a new object then does not get them either); if the in-place update is not needed, turn it off with GenerateInPlace = false.",
            Message(im0025));
        Assert.StartsWith("MapTo(typeof(OrderDto))", source[im0025.Location.SourceSpan.Start..]);
        // The overload works for the settable properties: not [Obsolete].
        Assert.DoesNotContain(generated, s => s.Contains("Obsolete"));
    }

    [Theory]
    [InlineData(".ForMember(d => d.Code, o => o.Ignore())", null)]
    [InlineData(".GenerateMethods(inPlace: false)", null)]
    [InlineData("", "Code is init-only, so an existing object keeps its value. If they need not be mapped, use ForMember(d => d.Code, o => o.Ignore()) for each (a new object then does not get them either); if the in-place update is not needed, use GenerateMethods(inPlace: false).")]
    public void Profile_IgnoreOrNoInPlace_NoIM0025(string configuration, string? message)
    {
        string source = Usings + """
            public class Row { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }
            public class Dto { public string Name { get; set; } = ""; public string Code { get; init; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>()CONFIG; }
            }
            """.Replace("CONFIG", configuration);

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        if (message is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var im0025 = Assert.Single(diagnostics);
            Assert.Equal($"In-place mapping from 'App.Row' to 'App.Dto' does not update every mapped property: {message}", Message(im0025));
            Assert.StartsWith("CreateMap<Row, Dto>()", source[im0025.Location.SourceSpan.Start..]);
        }
    }

    [Fact]
    public void InPlaceOffForTheProject_NoIM0025()
    {
        string source = Usings + """
            [MapTo(typeof(Dto))]
            public class Row { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }
            public class Dto { public string Name { get; set; } = ""; public string Code { get; init; } = ""; }
            """;

        var (_, diagnostics, errors, _) = GeneratorTestHelper.RunGeneratorCompileAndRun(
            source + "public static class Program { public static string Run() => new Row().MapToDto().Name; }",
            new Dictionary<string, string> { ["IronMapperGenerateInPlace"] = "false" });

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OnlyInitProperties_ObsoleteAsInPR7_NoIM0025()
    {
        string source = Usings + """
            [MapTo(typeof(Dto))]
            public class Row { public string Code { get; set; } = ""; }
            public class Dto { public string Code { get; init; } = ""; }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Contains(generated, s => s.Contains("[global::System.Obsolete("));
    }

    [Fact]
    public void NestedInit_ExistingObjectUpdatedInPlace_NullStaysNull_IM0025()
    {
        // Customer: init-only nested object; an existing one keeps its identity and gets the new values (2.0.0: skipped).
        string source = Usings + """
            [MapTo(typeof(OrderDto))]
            public class Order { public int Id { get; set; } public Customer? Customer { get; set; } }
            public class OrderDto { public int Id { get; set; } public CustomerDto? Customer { get; init; } }

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string Name { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var customer = new CustomerDto { Name = "old" };
                    var existing = new OrderDto { Customer = customer };
                    new Order { Id = 1, Customer = new Customer { Name = "new" } }.MapToOrderDto(existing);
                    bool same = ReferenceEquals(customer, existing.Customer);

                    var empty = new OrderDto();
                    new Order { Id = 2, Customer = new Customer { Name = "x" } }.MapToOrderDto(empty);

                    var kept = new OrderDto { Customer = new CustomerDto { Name = "kept" } };
                    new Order { Id = 3, Customer = null }.MapToOrderDto(kept);
                    return $"{same},{existing.Id},{existing.Customer!.Name};{empty.Id},{empty.Customer is null};{kept.Customer!.Name}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("True,1,new;2,True;kept", run);
        var im0025 = Assert.Single(diagnostics);
        Assert.Contains("Customer is an init-only nested object: an existing one is updated in place, but a null one stays null and a null source value is not assigned.",
            Message(im0025));
        Assert.Contains("if (source.Customer is not null && destination.Customer is not null) MapToCustomerDtoCore(source.Customer, destination.Customer);",
            generated.Single(s => s.Contains("MapToOrderDto(this ")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void NestedInit_StructCollectionAndPairThatCannotUpdate_Skipped()
    {
        // Point: a struct (the getter returns a copy). Lines: a collection (it would need an assignment). Tag: the pair's
        // mapping has an init-only property itself (CanUpdateInPlace), so it would be replaced, which init does not allow.
        string source = Usings + """
            [MapTo(typeof(OrderDto))]
            public class Order
            {
                public int Id { get; set; }
                public Point Point { get; set; }
                public List<string> Lines { get; set; } = new();
                public Tag Tag { get; set; } = new();
            }
            public class OrderDto
            {
                public int Id { get; set; }
                public PointDto Point { get; init; }
                public List<string> Lines { get; init; } = new();
                public TagDto Tag { get; init; } = new();
            }

            [MapTo(typeof(PointDto))]
            public struct Point { public int X { get; set; } }
            public struct PointDto { public int X { get; set; } }

            [MapTo(typeof(TagDto))]
            public class Tag { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }
            public class TagDto { public string Name { get; set; } = ""; public string Code { get; init; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var tag = new TagDto { Name = "old" };
                    var existing = new OrderDto { Tag = tag };
                    new Order { Id = 1, Point = new Point { X = 5 }, Lines = new() { "a" }, Tag = new Tag { Name = "new" } }.MapToOrderDto(existing);
                    return $"{existing.Id},{existing.Point.X},{existing.Lines.Count},{existing.Tag.Name}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("1,0,0,old", run);
        // The mapping to OrderDto, and the one to TagDto (its own in-place overload skips Code).
        var order = Assert.Single(diagnostics, d => Message(d).Contains("'App.OrderDto'"));
        Assert.Contains("does not update every mapped property: Point, Lines and Tag are init-only, so an existing object keeps their values.", Message(order));
        var tagDiagnostic = Assert.Single(diagnostics, d => Message(d).Contains("'App.TagDto'"));
        Assert.Contains("Code is init-only", Message(tagDiagnostic));
        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public void OnlyInitNestedObjects_NotObsoleteAnyMore_Updated()
    {
        // Up to PR 9 such an overload was [Obsolete] (no settable property); it updates the nested object now.
        string source = Usings + """
            [MapTo(typeof(OrderDto))]
            public class Order { public Customer Customer { get; set; } = new(); }
            public class OrderDto { public CustomerDto Customer { get; init; } = new(); }

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string Name { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var existing = new OrderDto();
                    var customer = existing.Customer;
                    new Order { Customer = new Customer { Name = "n" } }.MapToOrderDto(existing);
                    return $"{ReferenceEquals(customer, existing.Customer)},{existing.Customer.Name}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("True,n", run);
        Assert.DoesNotContain(generated, s => s.Contains("Obsolete"));
        Assert.Contains("an existing one is updated in place", Message(Assert.Single(diagnostics)));
    }

    [Fact]
    public void ReverseMap_HintNamesCreateMap()
    {
        string source = Usings + """
            public class Row { public string Name { get; set; } = ""; public string Code { get; init; } = ""; }
            public class Dto { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().ReverseMap(); }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0025 = Assert.Single(diagnostics);
        Assert.EndsWith("The reverse mapping of ReverseMap() takes no ForMember: declare it with CreateMap<Dto, Row>() to configure it, or turn the in-place overloads off with IronMapperGenerateInPlace=false.",
            Message(im0025));
        Assert.StartsWith("ReverseMap()", source[im0025.Location.SourceSpan.Start..]);
    }
}

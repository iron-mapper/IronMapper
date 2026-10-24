using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// IM0002: a destination property that the generated assignment cannot fill — incompatible types, a nested object or a
/// collection whose type pair has no mapping (nested objects with a mapping: <see cref="NestedObjectTests"/>). Up to 2.0 the
/// build failed inside the generated file (CS0029/CS0266, CS1061). Now: one IM0002 at the user's code, the property is not generated, the rest of the mapping
/// is generated and works. Mappings that compiled with 2.0 get no IM0002 (the false-positive tests below).
/// </summary>
public class IncompatiblePropertyTypesTests
{
    private static Diagnostic[] IM0002(IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == "IM0002").ToArray();

    private static string At(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    // ------------------------------------------------------------------
    // Attribute mappings
    // ------------------------------------------------------------------

    [Fact]
    public void Attribute_IncompatibleTypes_IM0002AtDestinationProperty_RestOfMappingWorks()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order
            {
                public int? Count { get; set; }
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }

            public class OrderDto
            {
                public int Count { get; set; }
                public System.Guid Id { get; set; }
                public string Name { get; set; } = "";
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Order { Count = 3, Id = "x", Name = "n" }.MapToOrderDto();
                    var target = new OrderDto();
                    new Order { Name = "m" }.MapToOrderDto(target);
                    return $"{dto.Name},{dto.Count},{dto.Id},{target.Name}";
                }
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var found = IM0002(generatorDiagnostics);
        Assert.Equal(new[] { "Count", "Id" }, found.Select(At).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        var count = found.Single(d => At(d) == "Count").GetMessage();
        Assert.Contains("Property 'Count' of 'App.OrderDto' is not mapped: 'Order.Count' is 'int?', which cannot be assigned to 'int'.", count);
        Assert.Contains("[MapConverter(typeof(...))] (an ITypeConverter<int?, int>)", count);
        Assert.Contains("[Ignore]", count);
        Assert.Contains("'Order.Id' is 'string', which cannot be assigned to 'System.Guid'", found.Single(d => At(d) == "Id").GetMessage());
        Assert.Equal("Count", found.Single(d => At(d) == "Count").Properties["PropertyName"]);
        // The other properties are mapped, also in place; the incompatible ones keep their default values.
        Assert.Equal("n,0,00000000-0000-0000-0000-000000000000,m", run);
    }

    [Fact]
    public void Attribute_NestedObjectWithoutMapping_IM0002SaysAddMappingForThePair()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(IM0002(generatorDiagnostics));
        Assert.Equal("Address", At(diagnostic));
        var message = diagnostic.GetMessage();
        Assert.Contains("'Customer.Address' is 'App.Address' and the destination is 'App.AddressDto', and there is no mapping from 'App.Address' to 'App.AddressDto'.", message);
        Assert.Contains("Add [MapTo(typeof(App.AddressDto))] to 'App.Address', [MapFrom(typeof(App.Address))] to 'App.AddressDto', or CreateMap<App.Address, App.AddressDto>() in a profile.", message);
        Assert.DoesNotContain(generated, s => s.Contains("Address = source.Address"));
    }

    [Fact]
    public void Attribute_CollectionElementWithoutMapping_IM0002InsteadOfCS1061()
    {
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public int Id { get; set; } public List<Line> Lines { get; set; } = new(); }
            public class OrderDto { public int Id { get; set; } public List<LineDto>? Lines { get; set; } }

            public class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }

            public static class Program
            {
                public static string Run() => new Order { Id = 4, Lines = { new Line() } }.MapToOrderDto() is { Lines: null } dto ? $"{dto.Id}" : "lines mapped";
            }
            """;

        var (generated, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(IM0002(generatorDiagnostics));
        Assert.Equal("Lines", At(diagnostic));
        var message = diagnostic.GetMessage();
        Assert.Contains("the elements of 'Order.Lines' are 'App.Line' and the elements of the destination are 'App.LineDto', and there is no mapping from 'App.Line' to 'App.LineDto'.", message);
        Assert.Contains("Add [MapTo(typeof(App.LineDto))] to 'App.Line', [MapFrom(typeof(App.Line))] to 'App.LineDto', or CreateMap<App.Line, App.LineDto>() in a profile.", message);
        Assert.DoesNotContain(generated, s => s.Contains("MapCollection_Lines"));
        Assert.Equal("4", run);
    }

    [Fact]
    public void Attribute_SameElementTypeDifferentCollection_IM0002InsteadOfCS0029()
    {
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(TagsDto))]
            public class Tags { public List<string> Values { get; set; } = new(); }
            public class TagsDto { public string[] Values { get; set; } = System.Array.Empty<string>(); }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Contains("'Tags.Values' is 'System.Collections.Generic.List<string>', which cannot be assigned to 'string[]'",
            Assert.Single(IM0002(generatorDiagnostics)).GetMessage());
    }

    [Fact]
    public void Attribute_MapConverterOrIgnore_NoIM0002()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order
            {
                [MapConverter(typeof(CountConverter))] public int? Count { get; set; }
                [Ignore] public string Id { get; set; } = "";
                public string Note { get; set; } = "";
            }

            public class OrderDto
            {
                public int Count { get; set; }
                public System.Guid Id { get; set; }
                [Ignore] public int Note { get; set; }
            }

            public class CountConverter : ITypeConverter<int?, int> { public int Convert(int? source) => source ?? -1; }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
    }

    // ------------------------------------------------------------------
    // Assignments that compile: no IM0002 (a build that passes on 2.0 must not get one)
    // ------------------------------------------------------------------

    [Fact]
    public void ImplicitConversions_NoIM0002()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            #nullable enable
            namespace App;

            public class Animal { public string Name { get; set; } = ""; }
            public class Dog : Animal { }
            public readonly struct Meters
            {
                public Meters(double value) => Value = value;
                public double Value { get; }
                public static implicit operator Meters(double value) => new(value);
            }

            [MapTo(typeof(AllDto))]
            public class All
            {
                public int Int { get; set; }
                public int Nullable { get; set; }
                public float Float { get; set; }
                public DateTime Date { get; set; }
                public Dog Pet { get; set; } = new();
                public double Distance { get; set; }
                public List<string> Tags { get; set; } = new();
                public string[] Codes { get; set; } = Array.Empty<string>();
                public dynamic? Anything { get; set; }
            }

            public class AllDto
            {
                public long Int { get; set; }
                public int? Nullable { get; set; }
                public double Float { get; set; }
                public DateTimeOffset Date { get; set; }
                public Animal Pet { get; set; } = new();
                public Meters Distance { get; set; }
                public IEnumerable<string> Tags { get; set; } = Array.Empty<string>();
                public IReadOnlyList<string> Codes { get; set; } = Array.Empty<string>();
                public int Anything { get; set; }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new All { Int = 5, Nullable = 6, Date = new DateTime(2020, 1, 1), Pet = new Dog { Name = "rex" }, Distance = 2.5, Anything = 7 }.MapToAllDto();
                    return $"{dto.Int},{dto.Nullable},{dto.Pet.Name},{dto.Distance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{dto.Anything}";
                }
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
        Assert.Equal("5,6,rex,2.5,7", run);
    }

    [Fact]
    public void CollectionElementMappedInThisProject_OrThroughBaseType_NoIM0002()
    {
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public List<Line> Lines { get; set; } = new(); public List<Special> Specials { get; set; } = new(); }
            public class OrderDto { public List<LineDto> Lines { get; set; } = new(); public List<LineDto> Specials { get; set; } = new(); }

            [MapTo(typeof(LineDto))]
            public class Line { public string Sku { get; set; } = ""; }
            // No mapping of its own: the generated item.MapToLineDto() binds to the mapping of the base type.
            public class Special : Line { }
            public class LineDto { public string Sku { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Order { Lines = { new Line { Sku = "a" } }, Specials = { new Special { Sku = "b" } } }.MapToOrderDto();
                    return dto.Lines[0].Sku + dto.Specials[0].Sku;
                }
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
        Assert.Equal("ab", run);
    }

    [Fact]
    public void CollectionElementMappedByHandWrittenExtensionMethodInGlobalNamespace_NoIM0002()
    {
        // The generated file has no usings; item.MapToLineDto() binds to an extension method of the global namespace,
        // as it did in 2.0.
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            public static class LineMappings
            {
                public static App.LineDto MapToLineDto(this App.Line line) => new App.LineDto { Sku = "hand:" + line.Sku };
            }

            namespace App
            {
                [MapTo(typeof(OrderDto))]
                public class Order { public List<Line> Lines { get; set; } = new(); }
                public class OrderDto { public List<LineDto> Lines { get; set; } = new(); }
                public class Line { public string Sku { get; set; } = ""; }
                public class LineDto { public string Sku { get; set; } = ""; }

                public static class Program
                {
                    public static string Run() => new Order { Lines = { new Line { Sku = "a" } } }.MapToOrderDto().Lines[0].Sku;
                }
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
        Assert.Equal("hand:a", run);
    }

    [Fact]
    public void CollectionElementMappedInReferencedAssembly_NoIM0002()
    {
        var library = """
            using IronMapper.Attributes;

            namespace Lib;

            [MapTo(typeof(LineDto))]
            public class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }
            """;
        var app = """
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public List<Lib.Line> Lines { get; set; } = new(); }
            public class OrderDto { public List<Lib.LineDto> Lines { get; set; } = new(); }
            """;

        var (libraryCompilation, libraryDiagnostics) = GeneratorTestHelper.CompileProject("Lib", library);
        Assert.Empty(libraryDiagnostics);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(libraryCompilation);
        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app, libraryRef);

        Assert.Empty(appDiagnostics);
    }

    [Fact]
    public void CollectionElement_InternalMappingOfReferencedAssembly_IM0002()
    {
        // The library's mapping is internal: the generated call in App cannot bind to it (CS1061 in 2.0).
        var library = """
            using IronMapper.Attributes;

            namespace Lib;

            [MapTo(typeof(LineDto))]
            internal class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }
            public class Holder { internal static object Create() => new Line(); }
            """;
        var app = """
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            public class Line { public string Sku { get; set; } = ""; }
            [MapTo(typeof(OrderDto))]
            public class Order { public List<Line> Lines { get; set; } = new(); }
            public class OrderDto { public List<Lib.LineDto> Lines { get; set; } = new(); }
            """;

        var (libraryCompilation, _) = GeneratorTestHelper.CompileProject("Lib", library);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(libraryCompilation);
        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app, libraryRef);

        Assert.Equal(new[] { "IM0002" }, appDiagnostics.Select(d => d.Id));
    }

    // ------------------------------------------------------------------
    // Profiles
    // ------------------------------------------------------------------

    private const string ProfileTypes = """
        using System.Collections.Generic;
        using IronMapper.Configuration;
        using IronMapper.Generated;
        using IronMapper.Interfaces;

        namespace App;

        public class Order
        {
            public int? Count { get; set; }
            public string Name { get; set; } = "";
            public Info Info { get; set; } = new();
            public List<Line> Lines { get; set; } = new();
        }

        public class OrderDto
        {
            public int Count { get; set; }
            public string Name { get; set; } = "";
            public int Code { get; set; }
            public List<LineDto> Lines { get; set; } = new();
        }

        public class Info { public string Code { get; set; } = ""; }
        public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }
        public class CountConverter : ITypeConverter<int?, int> { public int Convert(int? source) => source ?? -1; }
        """;

    [Fact]
    public void Profile_NameMatchAndIncludeMembersAndCollection_IM0002AtCreateMap()
    {
        var source = ProfileTypes + """

            public class OrderProfile : MappingProfile
            {
                public OrderProfile() { CreateMap<Order, OrderDto>().IncludeMembers(s => s.Info); }
            }

            public static class Program
            {
                public static string Run() => new Order { Name = "n", Count = 2 }.MapToOrderDto() is var d ? $"{d.Name},{d.Count},{d.Code},{d.Lines.Count}" : "";
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var found = IM0002(generatorDiagnostics);
        Assert.Equal(3, found.Length);
        Assert.All(found, d => Assert.StartsWith("CreateMap<Order, OrderDto>()", At(d)));
        var messages = found.Select(d => d.GetMessage()).ToArray();
        Assert.Contains(messages, m => m.Contains("Property 'Count' of 'App.OrderDto' is not mapped: 'Order.Count' is 'int?', which cannot be assigned to 'int'. Configure it with ForMember(d => d.Count, o => o.MapFrom(...)) that converts the value, with o.UseConverter<T>() (an ITypeConverter<int?, int>), or with o.Ignore()."));
        Assert.Contains(messages, m => m.Contains("'Order.Info.Code' is 'string', which cannot be assigned to 'int'"));
        Assert.Contains(messages, m => m.Contains("there is no mapping from 'App.Line' to 'App.LineDto'"));
        Assert.Equal("n,0,0,0", run);
    }

    [Fact]
    public void Profile_ForMemberConfiguresTheProperties_NoIM0002()
    {
        var source = ProfileTypes + """

            public class OrderProfile : MappingProfile
            {
                public OrderProfile()
                {
                    CreateMap<Line, LineDto>();
                    CreateMap<Order, OrderDto>()
                        .ForMember(d => d.Count, o => o.UseConverter<CountConverter>())
                        .ForMember(d => d.Code, o => o.MapFrom(s => s.Info.Code.Length));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var d = new Order { Info = new Info { Code = "abc" }, Lines = { new Line { Sku = "s" } } }.MapToOrderDto();
                    return $"{d.Count},{d.Code},{d.Lines[0].Sku}";
                }
            }
            """;

        var (_, generatorDiagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
        Assert.Equal("-1,3,s", run);
    }

    [Fact]
    public void Profile_MapFromExpressionOfWrongType_IM0002AtTheExpression()
    {
        var source = ProfileTypes + """

            public class OrderProfile : MappingProfile
            {
                public OrderProfile()
                {
                    CreateMap<Order, OrderDto>()
                        .ForMember(d => d.Count, o => o.MapFrom(s => s.Count))
                        .ForMember(d => d.Code, o => o.Ignore())
                        .ForMember(d => d.Lines, o => o.Ignore());
                }
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(IM0002(generatorDiagnostics));
        Assert.Equal("s.Count", At(diagnostic));
        Assert.Contains("Property 'Count' of 'App.OrderDto' is not mapped: the MapFrom expression is 'int?', which cannot be assigned to 'int'.", diagnostic.GetMessage());
    }

    [Fact]
    public void Profile_ConvertUsing_InPlaceStillAssignsTheProperties_IM0002()
    {
        // ConvertUsing replaces MapToOrderDto(source), but the in-place overload maps by properties (plan item 2.6), so
        // the incompatible properties failed to compile there in 2.0.
        var source = ProfileTypes + """

            public class OrderProfile : MappingProfile
            {
                public OrderProfile() { CreateMap<Order, OrderDto>().ConvertUsing(s => new OrderDto { Name = s.Name }); }
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Equal(2, IM0002(generatorDiagnostics).Length);   // Count, Lines (Code has no source: IM0001)
    }

    [Fact]
    public void Profile_ReverseMap_IM0002AtReverseMapWithHint()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            public class Item { public long Size { get; set; } }
            public class ItemDto { public int Size { get; set; } }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile() { CreateMap<ItemDto, Item>().ReverseMap(); }
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(IM0002(generatorDiagnostics));
        Assert.Equal("ReverseMap", At(diagnostic));
        Assert.Contains("'Item.Size' is 'long', which cannot be assigned to 'int'. The reverse mapping of ReverseMap() maps by name only", diagnostic.GetMessage());
    }

    [Fact]
    public void Profile_NestedObjectWithMapping_MappedWithoutIM0002()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>();
                }
            }
            """;

        var (_, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(IM0002(generatorDiagnostics));
    }

    // ------------------------------------------------------------------
    // IDE: the diagnostic of a collection element follows the mappings of other files
    // ------------------------------------------------------------------

    private const string OrderWithLines = """
        using System.Collections.Generic;
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(OrderDto))]
        public class Order { public List<Line> Lines { get; set; } = new(); }
        public class OrderDto { public List<LineDto> Lines { get; set; } = new(); }
        """;

    private const string LineTypes = """
        using IronMapper.Attributes;

        namespace App;

        /*MAP*/public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }
        """;

    [Theory]
    [InlineData("", "[MapTo(typeof(LineDto))] ", false)]
    [InlineData("[MapTo(typeof(LineDto))] ", "", true)]
    public void ElementMappingAddedOrRemovedInAnotherFile_SameAsFreshRun(string before, string after, bool reported)
    {
        var first = GeneratorTestHelper.CreateCompilation(new[] { OrderWithLines, LineTypes.Replace("/*MAP*/", before) });
        var tree = first.SyntaxTrees.ElementAt(1);
        var second = first.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(LineTypes.Replace("/*MAP*/", after), (CSharpParseOptions)tree.Options));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = driver.RunGenerators(first);
        var incremental = driver.RunGenerators(second).GetRunResult();
        var fresh = CSharpGeneratorDriver.Create(new IronMapperGenerator()).RunGenerators(second).GetRunResult();

        Assert.Equal(Describe(fresh.Diagnostics), Describe(incremental.Diagnostics));
        Assert.Equal(Sources(fresh), Sources(incremental));
        Assert.Equal(reported, incremental.Diagnostics.Any(d => d.Id == "IM0002"));
        Assert.Equal(!reported, incremental.GeneratedTrees.Any(t => t.ToString().Contains("MapCollection_Lines")));
    }

    private static IEnumerable<string> Describe(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}").OrderBy(s => s, StringComparer.Ordinal);

    private static IEnumerable<string> Sources(GeneratorDriverRunResult result)
        => result.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => $"{s.HintName}\n{s.SourceText}")
            .OrderBy(s => s, StringComparer.Ordinal);
}

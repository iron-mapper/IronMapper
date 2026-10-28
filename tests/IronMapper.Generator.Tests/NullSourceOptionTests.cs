using System.Collections.Generic;
using System.Linq;
using IronMapper.Generator.Analysis.Models;
using IronMapper.Generator.CodeGeneration;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>IronMapperNullSourceReturnsNull</c> (issue #27, PR 9 of 2.1.0, plan item 2.14): <c>MapToX(this S? source)</c> returns
/// <c>D?</c> with <c>[return: NotNullIfNotNull("source")]</c>, <c>null</c> → <c>null</c>, for a class source and a class
/// destination. Everything else keeps the behavior without the option: a struct source or destination, the in-place
/// overload, a <c>null</c> collection element, nested objects (by the nullable annotations), <c>IMapper</c>.
/// </summary>
public class NullSourceOptionTests
{
    private static readonly Dictionary<string, string> On = new() { ["IronMapperNullSourceReturnsNull"] = "true" };

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private const string Orders = """
        using System.Collections.Generic;
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }
        public class Customer { public string Name { get; set; } = ""; }
        public class CustomerDto { public string Name { get; set; } = ""; }
        public class Order
        {
            public string Name { get; set; } = "";
            public Customer? Customer { get; set; }
            public List<Line> Lines { get; set; } = new();
        }
        public class OrderDto
        {
            public string Name { get; set; } = "";
            public CustomerDto? Customer { get; set; }
            public List<LineDto> Lines { get; set; } = new();
        }

        public class OrderProfile : MappingProfile
        {
            public OrderProfile()
            {
                CreateMap<Line, LineDto>();
                CreateMap<Customer, CustomerDto>();
                CreateMap<Order, OrderDto>();
            }
        }

        """;

    [Fact]
    public void Off_ByDefault_NullThrowsAsBefore()
    {
        string source = Orders + """
            public static class Program
            {
                public static string Run()
                {
                    try { ((Order?)null)!.MapToOrderDto(); return "no exception"; }
                    catch (System.ArgumentNullException e) { return e.ParamName!; }
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("source", run);
        Assert.DoesNotContain(generated, s => s.Contains("NotNullIfNotNull") || s.Contains("NotNullElement"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void On_NullReturnsNull_NonNullMapped_SignatureAnnotated(string value)
    {
        string source = Orders + """
            public static class Program
            {
                public static string Run()
                {
                    Order? none = null;
                    var dto = new Order { Name = "o", Customer = new Customer { Name = "c" } }.MapToOrderDto();
                    return $"{none.MapToOrderDto() is null},{dto.Name},{dto.Customer!.Name}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source,
            new Dictionary<string, string> { ["IronMapperNullSourceReturnsNull"] = value });

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,o,c", run);
        string code = generated.Single(s => s.Contains("MapToOrderDto(this global::App.Order? source)")).ReplaceLineEndings("\n");
        Assert.Contains("""
                    [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull("source")]
                    public static global::App.OrderDto? MapToOrderDto(this global::App.Order? source)
                    {
                        if (source is null) return null;
            """.ReplaceLineEndings("\n"), code);
        // The in-place overload keeps its signature and its ArgumentNullException.
        Assert.Contains("public static void MapToOrderDto(this global::App.Order source, global::App.OrderDto destination)", code);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("")]
    public void AnyOtherValue_Off(string value)
    {
        var (generated, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(
            Orders + "public static class Program { public static string Run() => new Order().MapToOrderDto().Name; }",
            new Dictionary<string, string> { ["IronMapperNullSourceReturnsNull"] = value });

        AssertNoErrors(errors);
        Assert.Equal("", run);
        Assert.Contains(generated, s => s.Contains("MapToOrderDto(this global::App.Order source)"));
        Assert.DoesNotContain(generated, s => s.Contains("NotNullIfNotNull"));
    }

    [Fact]
    public void On_UserCode_NoNewNullableWarnings_TreatWarningsAsErrors()
    {
        // A non-null source gives a non-null result (NotNullIfNotNull), so code written for 2.0.0 has no CS8602/CS8600;
        // the generated files (mappings, registration, element loops) have no warning either.
        string source = Orders + """
            public static class Use
            {
                public static string NonNull(Order order)
                {
                    var dto = order.MapToOrderDto();
                    OrderDto typed = order.MapToOrderDto();
                    return dto.Name + typed.Name;
                }

                public static string? Maybe(Order? order) => order.MapToOrderDto()?.Name;

                public static int Collections(List<Order> orders) => orders.MapToOrderDtoList().Count + orders.MapToOrderDtoArray().Length;

                public static void InPlace(Order order, OrderDto dto) => order.MapToOrderDto(dto);
            }
            """;

        var (_, warningsAndErrors) = GeneratorTestHelper.CompileProject("Orders", source, On);

        Assert.True(warningsAndErrors.Count == 0, string.Join("\n", warningsAndErrors));
    }

    [Fact]
    public void On_NullableSourceDereferenced_CS8602_TheAnnotationIsRead()
    {
        // The other half of NotNullIfNotNull: a maybe-null source gives a maybe-null result.
        string source = Orders + """
            public static class Use
            {
                public static string Name(Order? order) => order.MapToOrderDto().Name;
            }
            """;

        var (_, warningsAndErrors) = GeneratorTestHelper.CompileProject("Orders", source, On);

        Assert.Equal("CS8602", Assert.Single(warningsAndErrors).Id);
    }

    [Fact]
    public void On_StructSourceAndStructDestination_Unchanged()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(PointDto))]
            public struct Point { public int X { get; set; } }
            public class PointDto { public int X { get; set; } }

            [MapTo(typeof(Size))]
            public class SizeRow { public int Width { get; set; } }
            public struct Size { public int Width { get; set; } }

            public static class Program
            {
                public static string Run()
                {
                    Size size = new SizeRow { Width = 2 }.MapToSize();
                    PointDto point = new Point { X = 1 }.MapToPointDto();
                    try { ((SizeRow?)null)!.MapToSize(); return "no exception"; }
                    catch (System.ArgumentNullException) { return $"{size.Width},{point.X},throws"; }
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source, On);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("2,1,throws", run);
        Assert.Contains(generated, s => s.Contains("public static global::App.PointDto MapToPointDto(this global::App.Point source)"));
        Assert.Contains(generated, s => s.Contains("public static global::App.Size MapToSize(this global::App.SizeRow source)"));
        Assert.DoesNotContain(generated, s => s.Contains("[return:"));
    }

    [Fact]
    public void On_InPlaceCollectionsAndNestedObjects_AsWithoutTheOption()
    {
        string source = Orders + """
            public class Program
            {
                private static string Throws(System.Action action)
                {
                    try { action(); return "no exception"; }
                    catch (System.ArgumentNullException e) { return "ANE:" + e.ParamName; }
                    catch (System.InvalidOperationException) { return "IOE"; }
                }

                public static string Run()
                {
                    var existing = new OrderDto { Name = "kept" };
                    string inPlace = Throws(() => ((Order?)null)!.MapToOrderDto(existing));
                    string nullList = ((IEnumerable<Order>?)null).MapToOrderDtoList().Count.ToString();
                    string nullElement = Throws(() => new List<Order> { null! }.MapToOrderDtoList());
                    string nullArrayElement = Throws(() => new[] { new Order(), null! }.MapToOrderDtoArray());
                    string nestedElement = Throws(() => new Order { Lines = new List<Line> { null! } }.MapToOrderDto());
                    var nested = new Order { Customer = null }.MapToOrderDto();
                    return $"{inPlace},{existing.Name},{nullList},{nullElement},{nullArrayElement},{nestedElement},{nested.Customer is null}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source, On);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("ANE:source,kept,0,ANE:source,ANE:source,ANE:source,True", run);
        string code = generated.Single(s => s.Contains("MapToOrderDto(this global::App.Order? source)")).ReplaceLineEndings("\n");
        Assert.Contains("public global::App.OrderDto Map(global::App.Order source) => MapToOrderDto(NotNullElement(source));", code);
        Assert.Contains("result.Add(NotNullElement(source[i]).MapToLineDto());", code);
        // The nested object is mapped by the nullable annotations, through …Core, as without the option.
        Assert.Contains("Customer = source.Customer is null ? null : MapToCustomerDtoCore(source.Customer),", code);
    }

    [Fact]
    public void On_FactoriesConvertersWhenAndHooks_NotCalledForNull()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Row { public string Name { get; set; } = ""; }
            public class Card
            {
                private Card(string name) => Name = name;
                public string Name { get; }
                public static int Created;
                public static Card Restore(string name) { Created++; return new Card(name); }
            }
            public class Text { public string Value { get; set; } = ""; }
            public class Logged { public string Name { get; set; } = ""; }
            public class Kept { public string Name { get; set; } = ""; }

            public static class Hooks
            {
                public static int Calls;
                public static void After(Row r, Logged l) => Calls++;
                public static bool Check(Row r) { Calls++; return true; }
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Card>().ConstructWith(Card.Restore);
                    CreateMap<Row, Text>().ConvertUsing(r => new Text { Value = r.Name + Hooks.Calls++ });
                    CreateMap<Row, Logged>().AfterMap(Hooks.After);
                    CreateMap<Row, Kept>().When(Hooks.Check);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    Row? none = null;
                    bool allNull = none.MapToCard() is null && none.MapToText() is null && none.MapToLogged() is null && none.MapToKept() is null;
                    return $"{allNull},{Card.Created},{Hooks.Calls},{new Row { Name = "r" }.MapToCard().Name}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source, On);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,0,0,r", run);
    }

    [Fact]
    public void ReferencedProjectWithTheOption_ConsumerWithout_NoWarnings()
    {
        // A project built with the option exposes MapToX(this S? source) → D? with NotNullIfNotNull: a consumer without
        // it calls it for nested objects and collection elements without a nullable warning in its generated files.
        string library = """
            using IronMapper.Attributes;

            namespace Lib;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string Name { get; set; } = ""; }
            """;
        string consumer = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order
            {
                public Lib.Customer Customer { get; set; } = new();
                public Lib.Customer? Optional { get; set; }
                public List<Lib.Customer> Others { get; set; } = new();
            }
            public class OrderDto
            {
                public Lib.CustomerDto Customer { get; set; } = new();
                public Lib.CustomerDto? Optional { get; set; }
                public List<Lib.CustomerDto> Others { get; set; } = new();
            }

            public static class Use
            {
                public static string Name(Lib.Customer customer) => customer.MapToCustomerDto().Name;
            }
            """;

        var (libraryCompilation, libraryWarnings) = GeneratorTestHelper.CompileProject("Lib", library, On);
        Assert.True(libraryWarnings.Count == 0, string.Join("\n", libraryWarnings));
        var (reference, _) = GeneratorTestHelper.EmitReference(libraryCompilation);

        var (_, warningsAndErrors) = GeneratorTestHelper.CompileProject("App", consumer, reference);

        Assert.True(warningsAndErrors.Count == 0, string.Join("\n", warningsAndErrors));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ClassDeclaration_NotNullIfNotNullCopy_OnlyWhenTheTargetHasNone(bool hasAttribute, bool copied)
    {
        var options = new GeneratedCodeOptions(useThrowIfNull: false, hasDoesNotReturn: false, supportsModuleInitializers: true,
            hasModuleInitializerAttribute: false, hasNotNullIfNotNull: hasAttribute, nullSourceReturnsNull: true);

        var (_, source) = MapperCodeEmitter.EmitClassDeclaration("public", usesConverterCache: false, excludeFromCodeCoverage: false,
            usesList: false, usesArray: false, options);

        Assert.Equal(copied, source.Contains("internal sealed class NotNullIfNotNullAttribute"));
        Assert.Contains("private static T NotNullElement<T>(T source)", source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmitPolyfills_False_NoAttributeCopies(bool emitPolyfills)
    {
        // A target without both attributes (netstandard2.0). IronMapperEmitPolyfills=false: a polyfill generator of the
        // project (PolySharp) declares them, and a second copy would be CS0101.
        var options = new GeneratedCodeOptions(useThrowIfNull: false, hasDoesNotReturn: false, supportsModuleInitializers: true,
            hasModuleInitializerAttribute: false, hasNotNullIfNotNull: false, nullSourceReturnsNull: true,
            emitPolyfills: emitPolyfills);
        var registrations = new EquatableArray<MappingRegistration>(System.Collections.Immutable.ImmutableArray.Create(
            new MappingRegistration("global::App.User", "global::App.UserDto", "MapToUserDto", "_User_UserDto", GeneratedMethods.All)));

        var (hintName, declaration) = MapperCodeEmitter.EmitClassDeclaration("public", usesConverterCache: false,
            excludeFromCodeCoverage: false, usesList: false, usesArray: false, options);
        var (_, registration) = MapperCodeEmitter.EmitRegistration(registrations, options);

        Assert.Equal("GeneratedMappers.g.cs", hintName);
        Assert.Equal(emitPolyfills, declaration.Contains("class NotNullIfNotNullAttribute"));
        Assert.Equal(emitPolyfills, registration.Contains("class ModuleInitializerAttribute"));
        // The uses stay: the attributes then come from the other generator.
        Assert.Contains("private static T NotNullElement<T>(T source)", declaration);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", registration);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData(" false ", false)]
    [InlineData("no", true)]
    public void EmitPolyfills_ReadFromTheMSBuildProperty_OnlyFalseTurnsItOff(string? value, bool expected)
    {
        var properties = new Dictionary<string, string>();
        if (value is not null)
        {
            properties["IronMapperEmitPolyfills"] = value;
        }

        var settings = GeneratorSettings.From(TestOptionsProvider.FromBuildProperties(properties).GlobalOptions);

        Assert.Equal(expected, settings.EmitPolyfills);
        Assert.False(settings.NullSourceReturnsNull);
    }

    [Fact]
    public void CodeOptions_NotNullIfNotNullFoundInTheFramework()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { "namespace App; public class A { }" });

        Assert.True(GeneratedCodeOptions.From(compilation).HasNotNullIfNotNull);
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Duplicate type pairs (IM0007), method name collisions (IM0013/IM0014), unique hint names, nested converter
/// types and unique hook/transformer helper names. These tests compile (and where useful run) the generated code.
/// Names that compile on 1.1.2 must not change: the rules apply only where the generated code used to fail.
/// </summary>
public class NameCollisionTests
{
    private static string Join(IReadOnlyList<string> sources) => string.Join("\n", sources);

    private static void AssertCompiles(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Compilation errors:\n" + string.Join("\n", errors.Select(e => e.ToString())));

    private static string[] Ids(IReadOnlyList<Diagnostic> diagnostics)
        => diagnostics.Select(d => d.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

    private static string Message(IReadOnlyList<Diagnostic> diagnostics, string id)
        => diagnostics.Single(d => d.Id == id).GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Duplicate type pairs → IM0007, generated once
    // ------------------------------------------------------------------

    [Fact]
    public void SamePair_AttributeAndProfile_ProfileWins_ReportsIM0007_OtherMappingsGenerated()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(UserDto))]
            public class User { public string First { get; set; } = ""; public string Last { get; set; } = ""; }
            public class UserDto { public string First { get; set; } = ""; public string Last { get; set; } = ""; }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<User, UserDto>()
                        .ForMember(d => d.First, o => o.MapFrom(s => s.First + "!"))
                        .AfterMap((s, d) => d.Last = d.Last + "?");
                }
            }

            [MapTo(typeof(OrderDto))]
            public class Order { public int Id { get; set; } }
            public class OrderDto { public int Id { get; set; } }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new User { First = "a", Last = "b" }.MapToUserDto();
                    return dto.First + dto.Last + new Order { Id = 3 }.MapToOrderDto().Id;
                }
            }
            """;

        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0007" }, Ids(diagnostics));
        Assert.Contains("App.UserProfile", Message(diagnostics, "IM0007"));
        Assert.Equal("a!b?3", result);
        Assert.Single(generated, s => s.Contains("MapToUserDto(this global::App.User source)"));
    }

    [Fact]
    public void SamePair_MapToAndMapFrom_GeneratedOnce_ReportsIM0007()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(UserDto))]
            public class User { public int Id { get; set; } }

            [MapFrom(typeof(User))]
            public class UserDto { public int Id { get; set; } }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0007" }, Ids(diagnostics));
        Assert.EndsWith("Only the definition from [MapTo] on 'App.User' is generated; remove the others.", Message(diagnostics, "IM0007"));
        Assert.Single(generated, s => s.Contains("MapToUserDto(this global::App.User source)"));
    }

    [Fact]
    public void OneSource_GlobalAndNamespacedDestinations_GlobalQualifier()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            [MapTo(typeof(Dto))]
            [MapTo(typeof(App.Dto))]
            public class Source { public int V { get; set; } }
            public class Dto { public int V { get; set; } }

            public static class Program
            {
                public static string Run() => new Source { V = 1 }.MapToGlobalDto().V + "," + new Source { V = 2 }.MapToAppDto().V;
            }

            namespace App { public class Dto { public int V { get; set; } } }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Equal("1,2", result);
    }

    [Fact]
    public void SamePair_TwoProfiles_FirstProfileByFullNameWins_ReportsIM0007()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class User { public string Name { get; set; } = ""; }
            public class UserDto { public string Name { get; set; } = ""; }

            // Declared first in the file, but "App.BProfile" sorts after "App.AProfile".
            public class BProfile : MappingProfile
            {
                public BProfile() { CreateMap<User, UserDto>().ForMember(d => d.Name, o => o.MapFrom(s => "B")); }
            }

            public class AProfile : MappingProfile
            {
                public AProfile()
                {
                    CreateMap<User, UserDto>()
                        .ForMember(d => d.Name, o => o.MapFrom(s => "A"))
                        .BeforeMap((s, d) => d.Name = "before");
                }
            }

            public static class Program
            {
                public static string Run() => new User().MapToUserDto().Name;
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0007" }, Ids(diagnostics));
        Assert.Contains("App.AProfile", Message(diagnostics, "IM0007"));
        Assert.Equal("A", result);
    }

    [Fact]
    public void SamePair_ExplicitCreateMapBeatsReverseMap()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class User { public string Name { get; set; } = ""; }
            public class UserDto { public string Name { get; set; } = ""; }

            public class AProfile : MappingProfile
            {
                public AProfile() { CreateMap<User, UserDto>().ReverseMap(); }
            }

            public class ZProfile : MappingProfile
            {
                public ZProfile() { CreateMap<UserDto, User>().ForMember(d => d.Name, o => o.MapFrom(s => "explicit")); }
            }

            public static class Program
            {
                public static string Run() => new UserDto { Name = "n" }.MapToUser().Name + "," + new User { Name = "m" }.MapToUserDto().Name;
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0007" }, Ids(diagnostics));
        Assert.Equal("explicit,m", result);
    }

    // ------------------------------------------------------------------
    // Method name collisions → deterministic names + IM0013
    // ------------------------------------------------------------------

    [Fact]
    public void OneSource_TwoDestinationsWithSameShortName_QualifiedByNamespace_ReportsIM0013()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App
            {
                [MapTo(typeof(App.Admin.UserDto))]
                [MapTo(typeof(App.Public.UserDto))]
                [MapTo(typeof(SummaryDto))]
                public class User { public string Name { get; set; } = ""; }
                public class SummaryDto { public string Name { get; set; } = ""; }

                public static class Program
                {
                    public static string Run()
                    {
                        var user = new User { Name = "u" };
                        var target = new App.Public.UserDto();
                        user.MapToPublicUserDto(target);
                        return user.MapToAdminUserDto().Name
                            + new[] { user }.MapToPublicUserDtoList()[0].Name
                            + new[] { user }.MapToAdminUserDtoArray()[0].Name
                            + target.Name
                            + user.MapToSummaryDto().Name;
                    }
                }
            }

            namespace App.Admin { public class UserDto { public string Name { get; set; } = ""; } }
            namespace App.Public { public class UserDto { public string Name { get; set; } = ""; } }
            """;

        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("'MapToAdminUserDto'") && d.GetMessage().Contains("'MapToUserDto'"));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("'MapToPublicUserDto'"));
        Assert.Equal("uuuuu", result);
        // A mapping without a collision keeps its 1.1.2 name.
        Assert.Contains("MapToSummaryDto(this global::App.User source)", Join(generated));
        Assert.DoesNotContain(" MapToUserDto(", Join(generated));
    }

    [Fact]
    public void OneSource_NestedDestinationsWithSameShortName_QualifiedByContainingType()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Line { public int Qty { get; set; } }
            public class Orders { public class Dto { public int Qty { get; set; } } }
            public class Invoices { public class Dto { public int Qty { get; set; } } }

            public class LineProfile : MappingProfile
            {
                public LineProfile()
                {
                    CreateMap<Line, Orders.Dto>().AfterMap((s, d) => d.Qty = d.Qty + 1);
                    CreateMap<Line, Invoices.Dto>().AfterMap((s, d) => d.Qty = d.Qty + 2);
                    AddTransformer<int>(v => v * 10);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var line = new Line { Qty = 1 };
                    return line.MapToOrdersDto().Qty + "," + line.MapToInvoicesDto().Qty;
                }
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Equal("11,12", result);
    }

    [Fact]
    public void OneSource_GenericDestinationsWithSameName_QualifiedByTypeArguments()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Item { public string Name { get; set; } = ""; }
            public class Box<T> { public string Name { get; set; } = ""; }

            public class BoxProfile : MappingProfile
            {
                public BoxProfile()
                {
                    CreateMap<Item, Box<int>>();
                    CreateMap<Item, Box<string>>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var item = new Item { Name = "i" };
                    return item.MapToBoxOfInt32().Name + item.MapToBoxOfString().Name;
                }
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Equal("ii", result);
    }

    [Fact]
    public void UnresolvableCollision_ReportsIM0014_SkipsOnlyThoseMappings()
    {
        // Qualified names MapToN1Dto/MapToN2Dto are already taken by mappings to X.N1Dto and X.N2Dto.
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App
            {
                [MapTo(typeof(N1.Dto))]
                [MapTo(typeof(N2.Dto))]
                [MapTo(typeof(X.N1Dto))]
                [MapTo(typeof(X.N2Dto))]
                public class S { public int V { get; set; } }

                public static class Program
                {
                    public static string Run() => new S { V = 4 }.MapToN1Dto().V + "," + new S { V = 5 }.MapToN2Dto().V;
                }
            }

            namespace N1 { public class Dto { public int V { get; set; } } }
            namespace N2 { public class Dto { public int V { get; set; } } }
            namespace X { public class N1Dto { public int V { get; set; } } public class N2Dto { public int V { get; set; } } }
            """;

        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0014", "IM0014" }, Ids(diagnostics));
        Assert.Equal("4,5", result);
        Assert.DoesNotContain("global::N1.Dto MapTo", Join(generated));
    }

    [Fact]
    public void CollectionElementMapping_UsesQualifiedElementMethodName()
    {
        string source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App
            {
                [MapTo(typeof(App.A.ItemDto))]
                [MapTo(typeof(App.B.ItemDto))]
                public class Item { public int V { get; set; } }

                [MapTo(typeof(CartDto))]
                public class Cart { public List<Item> Items { get; set; } = new(); }
                public class CartDto { public List<App.A.ItemDto> Items { get; set; } = new(); }

                public static class Program
                {
                    public static string Run()
                        => new Cart { Items = { new Item { V = 7 } } }.MapToCartDto().Items[0].V.ToString();
                }
            }

            namespace App.A { public class ItemDto { public int V { get; set; } } }
            namespace App.B { public class ItemDto { public int V { get; set; } } }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Equal("7", result);
    }

    // ------------------------------------------------------------------
    // Different pairs with the same short names (no method collision): names unchanged, hint names unique
    // ------------------------------------------------------------------

    [Fact]
    public void NestedTypesWithSameShortNames_InDifferentOuterClasses_CompileWithUnchangedNames()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Orders
            {
                [MapTo(typeof(ItemDto))]
                public class Item { public string Name { get; set; } = ""; }
                public class ItemDto { public string Name { get; set; } = ""; }
            }

            public class Invoices
            {
                public class Item { public string Name { get; set; } = ""; }
                public class ItemDto { public string Name { get; set; } = ""; }
            }

            public class InvoiceProfile : MappingProfile
            {
                public InvoiceProfile()
                {
                    CreateMap<Invoices.Item, Invoices.ItemDto>()
                        .BeforeMap((s, d) => d.Name = "x")
                        .AfterMap((s, d) => d.Name = d.Name + "!");
                    AddTransformer<string>(v => v.ToUpperInvariant());
                }
            }

            public class OrderHooksProfile : MappingProfile
            {
                public OrderHooksProfile()
                {
                    CreateMap<Orders.Item, Invoices.ItemDto>().AfterMap((s, d) => d.Name = d.Name + "?");
                    AddTransformer<string>(v => v + v);
                }
            }

            public static class Program
            {
                public static string Run()
                    => new Orders.Item { Name = "o" }.MapToOrdersItemDto().Name + ","
                     + new Invoices.Item { Name = "i" }.MapToItemDto().Name + ","
                     + new Orders.Item { Name = "p" }.MapToInvoicesItemDto().Name;
            }
            """;

        // Orders.Item → Orders.ItemDto ([MapTo]) and Orders.Item → Invoices.ItemDto (profile) collide on
        // MapToItemDto(this Orders.Item) and get qualified names; Invoices.Item → Invoices.ItemDto is an overload
        // and keeps MapToItemDto. All three pairs are "Item → ItemDto", so hint names and helper names
        // (hooks, TransformValue_String) must be unique by full names.
        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Equal(new[] { "IM0013", "IM0013" }, Ids(diagnostics));
        Assert.Equal("o,I!,pp?", result);
        Assert.Contains("MapToItemDto(this global::App.Invoices.Item source)", Join(generated));
    }
    [Fact]
    public void DifferentPairsWithSameShortNames_InDifferentNamespaces_KeepNames_HelpersAreUnique()
    {
        // 1.1.2: same hint name (CS8785, no code at all) and TransformValue_String_Item_ItemDto twice (CS0111).
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace Shop
            {
                public class Item { public string Name { get; set; } = ""; }
                public class ItemDto { public string Name { get; set; } = ""; }
                public class ShopProfile : MappingProfile
                {
                    public ShopProfile()
                    {
                        CreateMap<Item, ItemDto>().BeforeMap((s, d) => d.Name = "b").AfterMap((s, d) => d.Name = d.Name + "1");
                        AddTransformer<string>(v => v + "s");
                    }
                }
            }

            namespace Stock
            {
                public class Item { public string Name { get; set; } = ""; }
                public class ItemDto { public string Name { get; set; } = ""; }
                public class StockProfile : MappingProfile
                {
                    public StockProfile()
                    {
                        CreateMap<Item, ItemDto>().BeforeMap((s, d) => d.Name = "b").AfterMap((s, d) => d.Name = d.Name + "2");
                        AddTransformer<string>(v => v + "t");
                    }
                }
            }

            namespace App
            {
                public static class Program
                {
                    public static string Run()
                        => new Shop.Item { Name = "a" }.MapToItemDto().Name + "," + new Stock.Item { Name = "c" }.MapToItemDto().Name;
                }
            }
            """;

        var (generated, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("as1,ct2", result);
        Assert.Equal(2, generated.Count(s => s.Contains("MapToItemDto(this global::")));
    }

    // ------------------------------------------------------------------
    // Nested converter types
    // ------------------------------------------------------------------

    [Fact]
    public void NestedConverterTypes_ConvertUsing_UseConverter_MapConverter_Compile()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;
            using IronMapper.Interfaces;

            namespace App;

            public class Converters
            {
                public class Upper : ITypeConverter<string, string>
                {
                    public string Convert(string source) => source.ToUpperInvariant();
                }

                public class WholeOrder : ITypeConverter<Order, OrderDto>
                {
                    public OrderDto Convert(Order source) => new OrderDto { Code = "whole:" + source.Code };
                }
            }

            public class Order { public string Code { get; set; } = ""; }
            public class OrderDto { public string Code { get; set; } = ""; }

            [MapTo(typeof(TagDto))]
            public class Tag { [MapConverter(typeof(Converters.Upper))] public string Label { get; set; } = ""; }
            public class TagDto { public string Label { get; set; } = ""; }

            public class Note { public string Text { get; set; } = ""; }
            public class NoteDto { public string Text { get; set; } = ""; }

            public class AppProfile : MappingProfile
            {
                public AppProfile()
                {
                    CreateMap<Order, OrderDto>().ConvertUsing<Converters.WholeOrder>();
                    CreateMap<Note, NoteDto>().ForMember(d => d.Text, o => o.UseConverter<Converters.Upper>());
                }
            }

            public static class Program
            {
                public static string Run()
                    => new Order { Code = "o" }.MapToOrderDto().Code + ","
                     + new Tag { Label = "t" }.MapToTagDto().Label + ","
                     + new Note { Text = "n" }.MapToNoteDto().Text;
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertCompiles(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("whole:o,T,N", result);
    }
}

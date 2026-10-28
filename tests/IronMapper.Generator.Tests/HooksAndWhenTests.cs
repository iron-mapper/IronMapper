using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>BeforeMap</c>/<c>AfterMap</c> and <c>When</c> (PR 11 of 2.1.0, plan item 2.6). Issue #60: with a hook the destination
/// was created by <c>new D()</c> and assigned property by property, so a <c>required</c> property failed the build inside
/// the generated file (CS9035) and an <c>init</c>-only one was silently not mapped; now an object initializer sets them,
/// then <c>BeforeMap</c>, the other properties and <c>AfterMap</c> run. Issue #62: the in-place overload (and
/// <c>IMapper.Map(source, destination)</c>) did not evaluate <c>When</c> and did not call the hooks; now it does, as
/// AutoMapper's <c>Map(source, destination)</c>. <c>ConvertUsing</c> in place stays <c>[Obsolete]</c> (#66).
/// </summary>
public class HooksAndWhenTests
{
    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private const string Usings = """
        using System.Collections.Generic;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        """;

    [Fact]
    public void Hooks_RequiredAndInit_SetByInitializerBeforeBeforeMap()
    {
        string source = Usings + """
            public class Row { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public int Id { get; set; } }
            public class Dto
            {
                public required string Code { get; set; }
                public string Name { get; init; } = "";
                public int Id { get; set; }
                public string Seen { get; set; } = "";
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>()
                        .ForMember(d => d.Seen, o => o.Ignore())
                        .BeforeMap((s, d) => d.Seen = d.Code + "/" + d.Name + "/" + d.Id)
                        .AfterMap((s, d) => d.Id++);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Row { Code = "c", Name = "n", Id = 41 }.MapToDto();
                    return $"{dto.Code},{dto.Name},{dto.Id},{dto.Seen}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        // BeforeMap sees the required and init properties already set, the others not yet.
        Assert.Equal("c,n,42,c/n/0", run);
        Assert.Contains(generated, s => Normalize(s).Contains(Normalize("""
                        var destination = new global::App.Dto
                        {
                            Code = source.Code,
                            Name = source.Name,
                        };
                        BeforeMap_Row_Dto(source, destination);
                        destination.Id = source.Id;
                        AfterMap_Row_Dto(source, destination);
            """)));
        // Name is init-only: the in-place overload keeps it (IM0025, PR 10); nothing else.
        Assert.Equal("IM0025", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void Hooks_NoRequiredOrInit_SameCodeAsBefore()
    {
        string source = Usings + """
            public class Row { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().AfterMap((s, d) => d.Id++); }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Contains(generated, s => Normalize(s).Contains(Normalize("""
                        var destination = new global::App.Dto();
                        destination.Id = source.Id;
            """)));
    }

    [Fact]
    public void Hooks_Constructor_RequiredAndInitInInitializerAfterArguments()
    {
        string source = Usings + """
            public class Row { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Note { get; set; } = ""; }
            public class Dto
            {
                public Dto(int id) { Id = id; }
                public int Id { get; }
                public required string Code { get; init; }
                public string Name { get; init; } = "";
                public string Note { get; set; } = "";
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>().AfterMap((s, d) => d.Note += "!"); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Row { Id = 7, Code = "c", Name = "n", Note = "x" }.MapToDto();
                    return $"{dto.Id},{dto.Code},{dto.Name},{dto.Note}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("7,c,n,x!", run);
    }

    [Fact]
    public void Hooks_FactoryMethod_RequiredSettableAssignedAfterIt()
    {
        string source = Usings + """
            public class Row { public string Code { get; set; } = ""; public int Id { get; set; } }
            public class Dto
            {
                public required string Code { get; set; }
                public int Id { get; set; }
                public static Dto Create() => new Dto { Code = "factory" };
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>().ConstructWith(Dto.Create).BeforeMap((s, d) => d.Id = -1).AfterMap((s, d) => d.Id++);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Row { Code = "c", Id = 5 }.MapToDto();
                    return $"{dto.Code},{dto.Id}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("c,6", run);
    }

    [Theory]
    [InlineData(".ForMember(d => d.Code, o => o.Ignore())", "ForMember(d => d.Code, o => o.Ignore()) ignores it")]
    [InlineData(".ForMember(d => d.Code, o => o.Ignore()).AfterMap((s, d) => d.Id++)", "ForMember(d => d.Code, o => o.Ignore()) ignores it")]
    [InlineData(".ForMember(d => d.Ref, o => o.MapFrom(s => s.Id))", "'Row' has no property of that name")]
    public void Profile_RequiredWithoutValue_IM0026_InsteadOfCS9035(string configuration, string reason)
    {
        string source = Usings + """
            public class Row { public int Id { get; set; } public string Code { get; set; } = ""; }
            public class Dto
            {
                public int Id { get; set; }
                public int Ref { get; set; }
                public required string Code { get; set; }
                public required string Extra { get; init; }
            }

            public class RowProfile : MappingProfile
            {
                public RowProfile() { CreateMap<Row, Dto>()CONFIG; }
            }
            """.Replace("CONFIG", configuration);

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        // No CS9035 in the generated file: the mapping is not generated.
        AssertNoErrors(errors);
        Assert.DoesNotContain(generated, s => s.Contains("MapToDto("));
        var im0026 = diagnostics.Where(d => d.Id == "IM0026").ToList();
        Assert.All(im0026, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(im0026, d => Assert.StartsWith("CreateMap<Row, Dto>()", source[d.Location.SourceSpan.Start..]));
        var messages = im0026.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Contains("Mapping from 'App.Row' to 'App.Dto' is not generated: required property 'Extra' gets no value, 'Row' has no property of that name. Map it with ForMember(d => d.Extra, o => o.MapFrom(...)), or remove 'required'.", messages);
        if (reason.Contains("Ignore"))
        {
            Assert.Contains($"Mapping from 'App.Row' to 'App.Dto' is not generated: required property 'Code' gets no value, {reason}. Map it with ForMember(d => d.Code, o => o.MapFrom(...)), or remove 'required'.", messages);
            Assert.Equal(2, im0026.Count);
        }
        else
        {
            Assert.Single(im0026);
        }
    }

    [Fact]
    public void Attribute_RequiredIgnored_IM0026_SetsRequiredMembersConstructor_NoDiagnostic()
    {
        string source = """
            using System.Diagnostics.CodeAnalysis;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dto))]
            public class Row { public string Code { get; set; } = ""; [Ignore] public string Name { get; set; } = ""; }
            public class Dto { public required string Code { get; set; } public required string Name { get; set; } }

            [MapTo(typeof(Card))]
            public class Item { public string Code { get; set; } = ""; }
            public class Card
            {
                [SetsRequiredMembers] public Card() { Code = ""; Name = "default"; }
                public required string Code { get; set; }
                public required string Name { get; set; }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0026 = Assert.Single(diagnostics, d => d.Id == "IM0026");
        Assert.Equal("Mapping from 'App.Row' to 'App.Dto' is not generated: required property 'Name' gets no value, [Ignore] on 'Row.Name' leaves the source property unread. Add the property to 'Row', or put [MapProperty(\"Name\")] on the source property that holds the value, or remove 'required'.",
            im0026.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.StartsWith("MapTo(typeof(Dto))", source[im0026.Location.SourceSpan.Start..]);
        Assert.DoesNotContain(generated, s => s.Contains("MapToDto("));
        // [SetsRequiredMembers] on the constructor: Name keeps its value from the constructor.
        Assert.Contains(generated, s => s.Contains("MapToCard("));
    }

    [Fact]
    public void InPlace_HooksRunInOrder()
    {
        string source = Usings + """
            public class Row { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } public string Log { get; set; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>()
                        .ForMember(d => d.Log, o => o.Ignore())
                        .BeforeMap((s, d) => d.Log += "before:" + d.Id + ";")
                        .AfterMap((s, d) => d.Log += "after:" + d.Id + ";");
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var existing = new Dto { Id = 1 };
                    new Row { Id = 2 }.MapToDto(existing);
                    return existing.Log;
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("before:1;after:2;", run);
    }

    [Fact]
    public void InPlace_WhenFalse_DestinationUnchangedAndNoHooks_WhenTrue_Updated()
    {
        string source = Usings + """
            public class Row { public bool Active { get; set; } public string Name { get; set; } = ""; }
            public class Dto { public string Name { get; set; } = ""; public int Hooks { get; set; } }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>()
                        .ForMember(d => d.Hooks, o => o.Ignore())
                        .When(s => s.Active)
                        .BeforeMap((s, d) => d.Hooks++)
                        .AfterMap((s, d) => d.Hooks++);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var skipped = new Dto { Name = "old" };
                    new Row { Active = false, Name = "new" }.MapToDto(skipped);
                    var updated = new Dto { Name = "old" };
                    new Row { Active = true, Name = "new" }.MapToDto(updated);
                    return $"{skipped.Name},{skipped.Hooks};{updated.Name},{updated.Hooks}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("old,0;new,2", run);
    }

    [Fact]
    public void InPlace_NestedPairWithWhenAndHooks_ExistingNestedObjectFollowsThem()
    {
        string source = Usings + """
            public class Order { public string Status { get; set; } = ""; public Customer? Customer { get; set; } }
            public class Customer { public bool Active { get; set; } public string Name { get; set; } = ""; }
            public class OrderDto { public string Status { get; set; } = ""; public CustomerDto? Customer { get; set; } }
            public class CustomerDto { public string Name { get; set; } = ""; public int Updates { get; set; } }

            public class OrderProfile : MappingProfile
            {
                public OrderProfile()
                {
                    CreateMap<Order, OrderDto>();
                    CreateMap<Customer, CustomerDto>()
                        .ForMember(d => d.Updates, o => o.Ignore())
                        .When(s => s.Active)
                        .AfterMap((s, d) => d.Updates++);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var kept = new CustomerDto { Name = "old" };
                    var first = new OrderDto { Customer = kept };
                    new Order { Status = "s", Customer = new Customer { Active = false, Name = "new" } }.MapToOrderDto(first);
                    var updated = new CustomerDto { Name = "old" };
                    var second = new OrderDto { Customer = updated };
                    new Order { Status = "s", Customer = new Customer { Active = true, Name = "new" } }.MapToOrderDto(second);
                    return $"{ReferenceEquals(kept, first.Customer)},{kept.Name},{kept.Updates};{ReferenceEquals(updated, second.Customer)},{updated.Name},{updated.Updates}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,old,0;True,new,1", run);
    }

    [Fact]
    public void InPlace_OnlyInitWithAfterMap_NotObsolete_IM0025()
    {
        string source = Usings + """
            public class Row { public string Code { get; set; } = ""; }
            public class Dto { public string Code { get; init; } = ""; public int Seen { get; set; } }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>().ForMember(d => d.Seen, o => o.Ignore()).AfterMap((s, d) => d.Seen++);
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var existing = new Dto { Code = "old" };
                    new Row { Code = "new" }.MapToDto(existing);
                    return $"{existing.Code},{existing.Seen}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        // AfterMap changes the destination: the overload is no longer a no-op (PR 7 made it [Obsolete]).
        Assert.Equal("old,1", run);
        Assert.DoesNotContain(generated, s => s.Contains("Obsolete"));
        Assert.Equal("IM0025", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void InPlace_ConvertUsingWithHooks_StaysObsolete_NoHookCalls()
    {
        string source = Usings + """
            public class Row { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    CreateMap<Row, Dto>().ConvertUsing(s => new Dto { Id = s.Id }).When(s => s.Id > 0).AfterMap((s, d) => d.Id++);
                }
            }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        string file = generated.Single(s => s.Contains("MapToDto(this global::App.Row source, global::App.Dto destination)"));
        string inPlace = file[file.IndexOf("[global::System.Obsolete", System.StringComparison.Ordinal)..];
        inPlace = inPlace[..inPlace.IndexOf("        }", System.StringComparison.Ordinal)];
        Assert.DoesNotContain("AfterMap_Row_Dto(", inPlace);
        Assert.DoesNotContain("return;", inPlace);
    }
}

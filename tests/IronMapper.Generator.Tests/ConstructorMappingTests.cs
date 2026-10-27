using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Constructors and positional records (issue #24): a destination without an accessible parameterless constructor is
/// created by a constructor whose parameters take the source properties of the same name (ignoring case), widening and
/// other implicit conversions, collections and nested objects as properties do; parameters with a default value may be
/// left out; the other properties are set by an initializer after the constructor. With a parameterless constructor the
/// generated code is the same as in 2.0.0. Up to 2.0.0 such a destination failed to compile in the generated file
/// (CS7036), so no build that compiles with 2.0.0 changes.
/// </summary>
public class ConstructorMappingTests
{
    private static Diagnostic[] Of(string id, IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == id).ToArray();

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    // Line endings of the generated code (Environment.NewLine) and of this file may differ.
    private static string Generated(IEnumerable<string> sources, string mapping)
        => sources.Single(s => s.Contains($"private static global::App.{mapping} MapTo{mapping}Core(")).ReplaceLineEndings("\n");

    private static void AssertCode(string expected, string code) => Assert.Contains(expected.ReplaceLineEndings("\n"), code);

    [Fact]
    public void PositionalRecordToPositionalRecord_WideningToNullable_ByConstructor_NoInPlaceOverload()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(PendingStateDto))]
            public record PendingState(string NewAddress, System.DateTimeOffset ExpiresAt, int Attempts);
            public record PendingStateDto(string? NewAddress, System.DateTimeOffset? ExpiresAt, long Attempts);

            public static class Program
            {
                public static string Run()
                {
                    var state = new PendingState("a@b.c", new System.DateTimeOffset(2026, 1, 2, 0, 0, 0, System.TimeSpan.Zero), 3);
                    var dto = state.MapToPendingStateDto();
                    return $"{dto.NewAddress},{dto.ExpiresAt:yyyy-MM-dd},{dto.Attempts}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("a@b.c,2026-01-02,3", run);
        string code = Generated(generated, "PendingStateDto");
        AssertCode("""
                        return new global::App.PendingStateDto(
                            NewAddress: source.NewAddress,
                            ExpiresAt: source.ExpiresAt,
                            Attempts: source.Attempts);
            """, code);
        // No in-place overload and no in-place …Core: only the private method of the IMapper registration, which throws.
        Assert.DoesNotContain("MapToPendingStateDto(this global::App.PendingState source, global::App.PendingStateDto destination)", code);
        Assert.DoesNotContain("private static void MapToPendingStateDtoCore(", code);
        Assert.Contains("private static void MapInto_PendingState_PendingStateDto(global::App.PendingState source, global::App.PendingStateDto destination)", code);
        Assert.Contains("=> throw new global::IronMapper.Exceptions.MappingException(\"No in-place mapping from PendingState to PendingStateDto is generated", code);
    }

    [Fact]
    public void Profile_RecordWithDefaultValue_ComputedParameterThroughMapFrom_IgnoreUsesTheDefault()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class UserRecord
            {
                public System.Guid Id { get; set; }
                public string Email { get; set; } = "";
                public string Locale { get; set; } = "";
                public System.DateTimeOffset? BlockedAt { get; set; }
                public string Theme { get; set; } = "dark";
                public string Notes { get; set; } = "";
            }

            public record Account(System.Guid Id, string Email, bool Blocked, string Locale = "en", string Theme = Account.SystemTheme)
            {
                public const string SystemTheme = "system";
            }

            public class AccountProfile : MappingProfile
            {
                public AccountProfile()
                {
                    CreateMap<UserRecord, Account>()
                        .ForMember(d => d.Blocked, o => o.MapFrom(s => s.BlockedAt != null))
                        .ForMember(d => d.Theme, o => o.Ignore());
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var user = new UserRecord { Id = System.Guid.Empty, Email = "x@y.z", Locale = "de", BlockedAt = System.DateTimeOffset.UnixEpoch };
                    var account = user.MapToAccount();
                    return $"{account.Email},{account.Blocked},{account.Locale},{account.Theme}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        // Locale has a source; Theme is ignored and takes its default value (the source value "dark" is not used).
        Assert.Equal("x@y.z,True,de,system", run);
        string code = Generated(generated, "Account");
        Assert.Contains("Blocked: source.BlockedAt != null,", code);
        Assert.Contains("Locale: source.Locale);", code);
        Assert.DoesNotContain("Theme", code);
    }

    [Fact]
    public void ParameterWithDefaultValue_WithoutSource_IsLeftOut_NamedArgumentsKeepTheOthersInPlace()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Event))]
            public class EventRow { public string Name { get; set; } = ""; public System.DateTimeOffset At { get; set; } }

            public record Event(string Name, string? Detail = "none", System.DateTimeOffset At = default);

            public static class Program
            {
                public static string Run()
                {
                    var e = new EventRow { Name = "ping", At = System.DateTimeOffset.UnixEpoch }.MapToEvent();
                    return $"{e.Name},{e.Detail},{e.At.ToUnixTimeSeconds()}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("ping,none,0", run);
        AssertCode("""
                            Name: source.Name,
                            At: source.At);
            """, Generated(generated, "Event"));
    }

    [Fact]
    public void ClassConstructor_ParametersMatchedIgnoringCase_OtherPropertiesByInitializerAfterTheConstructor()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Item))]
            public class ItemRow
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string Note { get; set; } = "";
                public int Rank { get; set; }
                public string Unused { get; set; } = "";
            }

            public class Item
            {
                public Item(int id, string name) { Id = id; Name = name.ToUpperInvariant(); }
                public int Id { get; }
                public string Name { get; set; }
                public string Note { get; set; } = "";
                public int Rank { get; init; }
            }

            public static class Program
            {
                public static string Run()
                {
                    var item = new ItemRow { Id = 7, Name = "box", Note = "n", Rank = 2 }.MapToItem();
                    return $"{item.Id},{item.Name},{item.Note},{item.Rank}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        // The extra source property is not reported (IM0001 is about destination properties).
        Assert.Empty(diagnostics);
        // Name is set by the constructor only: the initializer does not overwrite what the constructor made of it.
        Assert.Equal("7,BOX,n,2", run);
        AssertCode("""
                        return new global::App.Item(
                            id: source.Id,
                            name: source.Name)
                        {
                            Note = source.Note,
                            Rank = source.Rank,
                        };
            """, Generated(generated, "Item"));
    }

    [Fact]
    public void Collections_ArrayToReadOnlyList_AndElementsMappedOneByOne_AsForProperties()
    {
        string source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Change))]
            public class ChangeRow
            {
                public System.Guid[] MovedIds { get; set; } = System.Array.Empty<System.Guid>();
                public string[] Tags { get; set; } = System.Array.Empty<string>();
                public List<LineRow> Lines { get; set; } = new();
            }

            [MapTo(typeof(Line))]
            public class LineRow { public int Qty { get; set; } }
            public record Line(int Qty);

            public record Change(IReadOnlyList<System.Guid> MovedIds, IReadOnlyList<string> Tags, IReadOnlyList<Line> Lines);

            public static class Program
            {
                public static string Run()
                {
                    var row = new ChangeRow { MovedIds = new[] { System.Guid.Empty }, Tags = new[] { "a", "b" }, Lines = { new LineRow { Qty = 2 }, new LineRow { Qty = 5 } } };
                    var change = row.MapToChange();
                    return $"{change.MovedIds.Count},{string.Join("+", change.Tags)},{change.Lines.Count},{change.Lines[1].Qty},{ReferenceEquals(change.Tags, row.Tags)}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        // Same element type: the array is passed as is (the reference is copied, as for properties).
        Assert.Equal("1,a+b,2,5,True", run);
        string code = Generated(generated, "Change");
        Assert.Contains("Tags: source.Tags,", code);
        Assert.Contains("Lines: MapCollection_Lines_ChangeRow_Change(source.Lines));", code);
        Assert.Contains("private static global::System.Collections.Generic.List<global::App.Line> MapCollection_Lines_ChangeRow_Change(", code);
    }

    [Fact]
    public void NestedObjectParameter_MappedThroughCore_WithTheNullChecksOfAProperty()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); public Customer? Payer { get; set; } }
            public record OrderDto(int Id, CustomerDto Customer, CustomerDto? Payer);

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; }
            public record CustomerDto(string Name);

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Order { Id = 1, Customer = new Customer { Name = "Ann" } }.MapToOrderDto();
                    string thrown;
                    try { new Order { Customer = null! }.MapToOrderDto(); thrown = "none"; }
                    catch (System.InvalidOperationException e) { thrown = e.Message.Split(',')[0]; }
                    return $"{dto.Id},{dto.Customer.Name},{dto.Payer is null},{thrown}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("1,Ann,True,Order.Customer is null", run);
        string code = Generated(generated, "OrderDto");
        Assert.Contains("Customer: MapToCustomerDtoCore(source.Customer ?? NestedObjectIsNull<global::App.Customer>(\"Order.Customer is null, but OrderDto.Customer is not nullable.", code);
        Assert.Contains("Payer: source.Payer is null ? null : MapToCustomerDtoCore(source.Payer));", code);
    }

    [Fact]
    public void InPlaceOfAParent_NestedObjectCreatedByAConstructor_GetsANewObject()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public Point Location { get; set; } = new(); }
            public class OrderDto { public PointDto Location { get; set; } = new(0, 0); }

            [MapTo(typeof(PointDto))]
            public class Point { public int X { get; set; } public int Y { get; set; } }
            public record PointDto(int X, int Y);

            public static class Program
            {
                public static string Run()
                {
                    var target = new OrderDto();
                    var before = target.Location;
                    new Order { Location = new Point { X = 1, Y = 2 } }.MapToOrderDto(target);
                    return $"{target.Location.X},{target.Location.Y},{ReferenceEquals(before, target.Location)}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("1,2,False", run);
        Assert.Contains("destination.Location = MapToPointDtoCore(source.Location ?? NestedObjectIsNull", Generated(generated, "OrderDto"));
    }

    [Fact]
    public void NarrowingNullableReference_ParameterBehavesAsAProperty_NullableWarningInTheGeneratedFile()
    {
        // As for a property (item 3.5 of the plan): string? → string compiles with a nullable warning in the generated file
        // (CS8601 for a property, CS8604 for a parameter) and passes null through.
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(AccountByProperty))]
            [MapTo(typeof(AccountByParameter))]
            public class IdentityUser { public string? Email { get; set; } }

            public class AccountByProperty { public string Email { get; set; } = ""; }
            public record AccountByParameter(string Email);
            """;

        var (_, diagnostics, compilation) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(compilation, d => d.Severity == DiagnosticSeverity.Error);
        // CS8601 in the initializer and in the in-place overload of the property, CS8604 in the constructor call.
        Assert.Equal(new[] { "CS8601", "CS8604" }, compilation.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray());
        Assert.Single(compilation, d => d.Id == "CS8604");
    }

    [Fact]
    public void PublicConstructorAndStaticFactory_TheConstructorIsUsed()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(ChatInfo))]
            public class ChatRow { public long ChatId { get; set; } public string Title { get; set; } = ""; }

            public record ChatInfo(long ChatId, string Title)
            {
                public static ChatInfo Create(long chatId, string title) => new(chatId, title.Trim().ToUpperInvariant());
            }

            public static class Program
            {
                public static string Run() => new ChatRow { ChatId = 5, Title = " t " }.MapToChatInfo().Title;
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal(" t ", run);
        Assert.DoesNotContain("Create(", Generated(generated, "ChatInfo"));
    }

    [Fact]
    public void SeveralConstructors_TheOneWithTheMostMappedParametersThatCanBeCalled()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Money))]
            public class Price { public decimal Amount { get; set; } public string Currency { get; set; } = ""; }

            public class Money
            {
                public Money(decimal amount) : this(amount, "EUR", 0) { }
                public Money(decimal amount, string currency) : this(amount, currency, 1) { }
                // Cannot be called: the source has no Rate and the parameter has no default value.
                public Money(decimal amount, string currency, int rate) { Amount = amount; Currency = currency; Rate = rate; }
                // The currency cannot be converted from string.
                public Money(decimal amount, System.Guid currency) : this(amount, currency.ToString(), 2) { }
                public decimal Amount { get; }
                public string Currency { get; }
                public int Rate { get; }
            }

            public static class Program
            {
                public static string Run()
                {
                    var money = new Price { Amount = 2.5m, Currency = "USD" }.MapToMoney();
                    return $"{money.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{money.Currency},{money.Rate}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("2.5,USD,1", run);
    }

    [Fact]
    public void EqualConstructors_IM0017_MappingNotGenerated()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Range))]
            public class RangeRow { public int From { get; set; } public int To { get; set; } }

            public class Range
            {
                public Range(int from, int to) { From = from; To = to; }
                public Range(long from, long to) { From = (int)from; To = (int)to; }
                public int From { get; }
                public int To { get; }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0017 = Assert.Single(Of("IM0017", diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, im0017.Severity);
        Assert.Equal(
            "Mapping from 'App.RangeRow' to 'App.Range' was not generated: the constructors 'Range(int, int)' and 'Range(long, long)' fit equally well (each maps 2 parameter(s) from the source). Remove or change one of the constructors, or create the object with ConvertUsing(s => new Range(...)) in a profile.",
            im0017.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(generated, s => s.Contains("MapToRange("));
    }

    [Fact]
    public void ParameterWithoutSourceAndDefault_IM0009AtTheParameter_MappingNotGenerated()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Project))]
            public class ProjectRow { public System.Guid Id { get; set; } public string Name { get; set; } = ""; public string Plan { get; set; } = ""; }

            public record Project(System.Guid Id, string Name, System.Guid OwnerId);
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0009 = Assert.Single(Of("IM0009", diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, im0009.Severity);
        Assert.Equal(
            "Mapping to 'App.Project' was not generated: constructor parameter 'OwnerId' gets no value: 'ProjectRow' has no property named 'OwnerId' (names are compared ignoring case), and the parameter has no default value. Add the property to 'ProjectRow', or put [MapProperty(\"OwnerId\")] on the source property that holds the value.",
            im0009.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("OwnerId", source.Substring(im0009.Location.SourceSpan.Start, im0009.Location.SourceSpan.Length));
        // The extra source property Plan gives no IM0001.
        Assert.Empty(Of("IM0001", diagnostics));
        Assert.DoesNotContain(generated, s => s.Contains("MapToProject("));
    }

    [Fact]
    public void Profile_IgnoreOnAParameterWithoutDefault_IM0009AtCreateMap()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class ProjectRow { public System.Guid Id { get; set; } public string Name { get; set; } = ""; }
            public record Project(System.Guid Id, string Name);

            public class ProjectProfile : MappingProfile
            {
                public ProjectProfile()
                {
                    CreateMap<ProjectRow, Project>().ForMember(d => d.Name, o => o.Ignore());
                }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0009 = Assert.Single(Of("IM0009", diagnostics));
        Assert.Contains("constructor parameter 'Name' gets no value: it is ignored (Ignore() of 'Name' in MappingProfile 'App.ProjectProfile') and has no default value.",
            im0009.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.StartsWith("CreateMap<ProjectRow, Project>()", source[im0009.Location.SourceSpan.Start..]);
    }

    [Fact]
    public void Profile_ParameterWithoutSource_HintNamesForMemberOfTheProperty()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class ProjectRow { public System.Guid Id { get; set; } }
            public record Project(System.Guid Id, System.Guid OwnerId);

            public class ProjectProfile : MappingProfile
            {
                public ProjectProfile() { CreateMap<ProjectRow, Project>(); }
            }
            """;

        var (_, diagnostics, _) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.EndsWith("Configure it with ForMember(d => d.OwnerId, o => o.MapFrom(...)), or add the property to 'ProjectRow'.",
            Assert.Single(Of("IM0009", diagnostics)).GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void IncompatibleParameterType_IM0009WithTheReasonOfIM0002()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Project))]
            public class ProjectRow { public string Id { get; set; } = ""; }
            public record Project(System.Guid Id);
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Contains("constructor parameter 'Id' gets no value: 'ProjectRow.Id' is 'string', which cannot be assigned to 'System.Guid'.",
            Assert.Single(Of("IM0009", diagnostics)).GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void OnlyPrivateConstructor_IM0018_MappingNotGenerated()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class ProjectRow { public System.Guid Id { get; set; } public string Name { get; set; } = ""; }

            public sealed class Project
            {
                private Project(System.Guid id, string name) { Id = id; Name = name; }
                public System.Guid Id { get; }
                public string Name { get; }
                public static Project Restore(System.Guid id, string name) => new(id, name);
            }

            public class ProjectProfile : MappingProfile
            {
                public ProjectProfile() { CreateMap<ProjectRow, Project>(); }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0018 = Assert.Single(Of("IM0018", diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, im0018.Severity);
        Assert.StartsWith("Mapping from 'App.ProjectRow' to 'App.Project' was not generated: 'App.Project' has no public or internal constructor",
            im0018.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(generated, s => s.Contains("MapToProject("));
    }

    [Fact]
    public void Profile_UseConverterAndMapFromOnParameters_MapPropertyOnASourceProperty()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Generated;
            using IronMapper.Interfaces;

            namespace App;

            public sealed class SecondsToSpan : ITypeConverter<int?, System.TimeSpan?>
            {
                public System.TimeSpan? Convert(int? source) => source is null ? null : System.TimeSpan.FromSeconds(source.Value);
            }

            public class ScheduleRow { public int? GraceSeconds { get; set; } public string Name { get; set; } = ""; }
            public record Schedule(System.TimeSpan? GraceSeconds, string Label);

            public class ScheduleProfile : MappingProfile
            {
                public ScheduleProfile()
                {
                    CreateMap<ScheduleRow, Schedule>()
                        .ForMember(d => d.GraceSeconds, o => o.UseConverter<SecondsToSpan>())
                        .ForMember(d => d.Label, o => o.MapFrom(s => s.Name + "!"));
                }
            }

            [MapTo(typeof(Tag))]
            public class TagRow { [MapProperty("Text")] public string Value { get; set; } = ""; }
            public record Tag(string Text);

            public static class Program
            {
                public static string Run()
                {
                    var schedule = new ScheduleRow { GraceSeconds = 90, Name = "daily" }.MapToSchedule();
                    return $"{schedule.GraceSeconds},{schedule.Label},{new TagRow { Value = "v" }.MapToTag().Text}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("00:01:30,daily!,v", run);
    }

    [Fact]
    public void ReverseMap_ToAPositionalRecord_ByConstructor()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public record Flip(string CheckId, int Code, System.DateTimeOffset? ExpectedAt = null);
            public class FlipRecord { public string CheckId { get; set; } = ""; public int Code { get; set; } }

            public class FlipProfile : MappingProfile
            {
                public FlipProfile() { CreateMap<Flip, FlipRecord>().ReverseMap(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var flip = new FlipRecord { CheckId = "c", Code = 3 }.MapToFlip();
                    return $"{flip.CheckId},{flip.Code},{flip.ExpectedAt is null},{new Flip("d", 4).MapToFlipRecord().Code}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("c,3,True,4", run);
    }

    [Fact]
    public void ExceptionFromTheConstructor_ReachesTheCallerAsItIs()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Project))]
            public class ProjectRow { public string Name { get; set; } = ""; }

            public class Project
            {
                public Project(string name) => Name = name.Length == 0 ? throw new System.ArgumentException("Name is empty.", nameof(name)) : name;
                public string Name { get; }
            }

            public static class Program
            {
                public static string Run()
                {
                    try { new ProjectRow().MapToProject(); return "none"; }
                    catch (System.ArgumentException e) { return $"{e.GetType().Name}:{e.ParamName}"; }
                }
            }
            """;

        var (_, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("ArgumentException:name", run);
    }

    [Fact]
    public void ParameterlessConstructor_OrStruct_ObjectInitializerAsBefore()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(PointDto))]
            [MapTo(typeof(PointStruct))]
            [MapTo(typeof(PointWithBoth))]
            public class Point { public int X { get; set; } public int Y { get; set; } }

            public record PointDto { public int X { get; init; } public int Y { get; init; } }
            public record struct PointStruct(int X, int Y);
            public record PointWithBoth(int X, int Y) { public PointWithBoth() : this(0, 0) { } }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        foreach (string dest in new[] { "PointDto", "PointStruct", "PointWithBoth" })
        {
            string code = Generated(generated, dest);
            Assert.Contains($"return new global::App.{dest}\n", code.Replace("\r\n", "\n"));
            Assert.Contains($"public static void MapTo{dest}(this global::App.Point source, global::App.{dest} destination)", code);
        }
    }

    [Fact]
    public void NestedObjectParameterWithoutMapping_IM0002_FileCompiles()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public Customer Customer { get; set; } = new(); }
            public record OrderDto(CustomerDto Customer);

            public class Customer { public string Name { get; set; } = ""; }
            public record CustomerDto(string Name);
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0002 = Assert.Single(Of("IM0002", diagnostics));
        Assert.Contains("there is no mapping from 'App.Customer' to 'App.CustomerDto'", im0002.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("Customer: default!);", Generated(generated, "OrderDto"));
    }

    [Fact]
    public void KeywordParameterName_EscapedInTheNamedArgument()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Seat))]
            public class SeatRow { public string Class { get; set; } = ""; }

            public class Seat
            {
                public Seat(string @class) => Class = @class;
                public string Class { get; }
            }

            public static class Program
            {
                public static string Run() => new SeatRow { Class = "first" }.MapToSeat().Class;
            }
            """;

        var (generated, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("first", run);
        Assert.Contains("@class: source.Class);", Generated(generated, "Seat"));
    }

    [Fact]
    public void RequiredPropertyOfTheParameter_StaysInTheInitializerUnlessTheConstructorSetsRequiredMembers()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(Label))]
            [MapTo(typeof(SetLabel))]
            public class LabelRow { public string Text { get; set; } = ""; }

            public class Label
            {
                public Label(string text) => Text = text;
                public required string Text { get; init; }
            }

            public class SetLabel
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                public SetLabel(string text) => Text = text;
                public required string Text { get; init; }
            }

            public static class Program
            {
                public static string Run() => new LabelRow { Text = "t" }.MapToLabel().Text + new LabelRow { Text = "u" }.MapToSetLabel().Text;
            }
            """;

        var (generated, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("tu", run);
        Assert.Contains("Text = source.Text,", Generated(generated, "Label"));
        Assert.DoesNotContain("Text = source.Text,", Generated(generated, "SetLabel"));
    }
}

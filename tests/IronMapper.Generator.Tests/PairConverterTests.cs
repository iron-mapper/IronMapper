using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Converters through the mappings of type pairs (item 3.5 of the plan, PR 5 of 2.1.0):
/// <c>CreateMap&lt;Guid, UserId&gt;().ConvertUsing(UserId.From)</c> and <c>CreateMap&lt;UserId, Guid&gt;().ConvertUsing(v =&gt; v.Value)</c>
/// convert every property, constructor parameter, factory parameter and collection element with that pair, as the
/// mapping of a pair maps a nested object; <c>Nullable&lt;T&gt;</c> is lifted (<c>Guid?</c> ↔ <c>UserId?</c> by the same
/// pair). The value objects are the form found in a real project, with neutral names.
/// </summary>
public class PairConverterTests
{
    private static Diagnostic[] Of(string id, IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == id).ToArray();

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    // Line endings of the generated code (Environment.NewLine) and of this file may differ.
    private static string Generated(IEnumerable<string> sources, string mapping)
        => sources.Single(s => s.Contains($" global::App.{mapping} MapTo{mapping}(this ")).ReplaceLineEndings("\n");

    // A value object from a Guid: a private constructor, a factory that rejects Guid.Empty; back through Value.
    private const string MemberId = """
        using System.Collections.Generic;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        public readonly record struct MemberId
        {
            private MemberId(System.Guid value) => Value = value;
            public System.Guid Value { get; }
            public static MemberId From(System.Guid value)
                => value == System.Guid.Empty ? throw new System.ArgumentException("The id is empty.", nameof(value)) : new(value);
        }

        """;

    private const string PairMaps = """
                CreateMap<System.Guid, MemberId>().ConvertUsing(MemberId.From);
                CreateMap<MemberId, System.Guid>().ConvertUsing(v => v.Value);
        """;

    [Fact]
    public void BothDirections_PropertiesConvertedByThePairMappings_ValueObjectWithPrivateConstructor()
    {
        string source = MemberId + """
            public class TaskRow { public System.Guid Assignee { get; set; } public string Title { get; set; } = ""; }
            public class TaskCard { public MemberId Assignee { get; set; } public string Title { get; set; } = ""; }

            public class TaskProfile : MappingProfile
            {
                public TaskProfile()
                {
            PAIRS
                    CreateMap<TaskRow, TaskCard>().ReverseMap();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var id = System.Guid.NewGuid();
                    var card = new TaskRow { Assignee = id, Title = "t" }.MapToTaskCard();
                    var row = card.MapToTaskRow();
                    return $"{card.Assignee.Value == id},{row.Assignee == id},{row.Title}";
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,True,t", run);
        // The …Core of the pair mapping, as for a nested object; the method group is called, the lambda is inlined.
        Assert.Contains("Assignee = MapToMemberIdCore(source.Assignee),", Generated(generated, "TaskCard"));
        Assert.Contains("Assignee = MapToGuidCore(source.Assignee),", generated.Single(s => s.Contains("MapToTaskRow(this global::App.TaskCard source)")));
        Assert.Contains("return global::App.MemberId.From(source);", Generated(generated, "MemberId"));
        Assert.Contains("return source.Value;", generated.Single(s => s.Contains("private static global::System.Guid MapToGuidCore(")));
    }

    [Fact]
    public void Nullable_LiftedByTheSamePair_NullToNull_ValueToNullable()
    {
        string source = MemberId + """
            public class ReviewRow
            {
                public System.Guid? Reviewer { get; set; }
                public System.Guid? Approver { get; set; }
                public System.Guid Author { get; set; }
            }
            public class Review
            {
                public MemberId? Reviewer { get; set; }
                public MemberId? Approver { get; set; }
                public MemberId? Author { get; set; }
            }

            public class ReviewProfile : MappingProfile
            {
                public ReviewProfile()
                {
            PAIRS
                    CreateMap<ReviewRow, Review>();
                    CreateMap<Review, ReviewRow>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var id = System.Guid.NewGuid();
                    var review = new ReviewRow { Reviewer = id, Approver = null, Author = id }.MapToReview();
                    var back = review.MapToReviewRow();
                    return $"{review.Reviewer?.Value == id},{review.Approver is null},{review.Author?.Value == id},{back.Reviewer == id},{back.Approver is null}";
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        // MemberId? → Guid (Author back) narrows: no mapping can fix it, IM0002 as for an assignment.
        var narrowing = Assert.Single(diagnostics);
        Assert.Equal("IM0002", narrowing.Id);
        Assert.StartsWith("Property 'Author' of 'App.ReviewRow' is not mapped: 'Review.Author' is 'App.MemberId?', which cannot be assigned to 'System.Guid'.",
            Message(narrowing));
        Assert.Equal("True,True,True,True,True", run);
        string code = Generated(generated, "Review");
        Assert.Contains("Reviewer = source.Reviewer.HasValue ? (global::App.MemberId?)MapToMemberIdCore(source.Reviewer.Value) : null,", code);
        Assert.Contains("Author = MapToMemberIdCore(source.Author),", code);
        Assert.Contains("Reviewer = source.Reviewer.HasValue ? (global::System.Guid?)MapToGuidCore(source.Reviewer.Value) : null,",
            generated.Single(s => s.Contains("MapToReviewRow(this global::App.Review source)")));
    }

    [Fact]
    public void ExplicitNullablePairMapping_WinsOverTheLiftedOne()
    {
        string source = MemberId + """
            public class Row { public System.Guid? Owner { get; set; } }
            public class Card { public MemberId? Owner { get; set; } }

            public class CardProfile : MappingProfile
            {
                public CardProfile()
                {
            PAIRS
                    CreateMap<System.Guid?, MemberId?>().ConvertUsing(v => v.HasValue && v.Value != System.Guid.Empty ? MemberId.From(v.Value) : (MemberId?)null);
                    CreateMap<Row, Card>();
                }
            }

            public static class Program
            {
                public static string Run() => $"{new Row { Owner = System.Guid.Empty }.MapToCard().Owner is null}";
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True", run);
        Assert.Contains("Owner = MapToNullableCore(source.Owner),", generated.Single(s => s.Contains("MapToCard(this global::App.Row source)")));
    }

    [Fact]
    public void FactoryAndConstructorParameters_ConvertedByThePairMapping()
    {
        string source = MemberId + """
            public class ProjectRow
            {
                public System.Guid Id { get; set; }
                public string Name { get; set; } = "";
                public System.Guid OwnerId { get; set; }
                public System.Guid? DeputyId { get; set; }
            }

            // A domain type: private constructor, a factory with value objects.
            public sealed class Project
            {
                private Project(System.Guid id, string name, MemberId ownerId, MemberId? deputyId)
                {
                    Id = id; Name = name; OwnerId = ownerId; DeputyId = deputyId;
                }
                public System.Guid Id { get; }
                public string Name { get; }
                public MemberId OwnerId { get; }
                public MemberId? DeputyId { get; }
                public static Project Restore(System.Guid id, string name, MemberId ownerId, MemberId? deputyId)
                    => new(id, name, ownerId, deputyId);
            }

            // A contract of the application layer: a positional record.
            public record ProjectCard(System.Guid Id, string Name, MemberId OwnerId, MemberId? DeputyId);

            public class ProjectProfile : MappingProfile
            {
                public ProjectProfile()
                {
            PAIRS
                    CreateMap<ProjectRow, Project>().ConstructWith(Project.Restore);
                    CreateMap<ProjectRow, ProjectCard>();
                    CreateMap<Project, ProjectRow>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new ProjectRow { Id = System.Guid.NewGuid(), Name = "p", OwnerId = System.Guid.NewGuid() };
                    var project = row.MapToProject();
                    var card = row.MapToProjectCard();
                    var back = project.MapToProjectRow();
                    return $"{project.OwnerId.Value == row.OwnerId},{project.DeputyId is null},{card.OwnerId == project.OwnerId},{back.OwnerId == row.OwnerId},{back.DeputyId is null}";
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,True,True,True,True", run);
        string project = Generated(generated, "Project");
        Assert.Contains("ownerId: MapToMemberIdCore(source.OwnerId),", project);
        Assert.Contains("deputyId: source.DeputyId.HasValue ? (global::App.MemberId?)MapToMemberIdCore(source.DeputyId.Value) : null)", project);
        Assert.Contains("OwnerId: MapToMemberIdCore(source.OwnerId),", Generated(generated, "ProjectCard"));
    }

    [Fact]
    public void ForMemberOfThePropertyOfTheSameName_ConfiguresTheFactoryParameter()
    {
        // The case of the real project: Restore(…, MemberId ownerId), the domain type has a public get-only OwnerId.
        // ForMember(d => d.OwnerId, …) configures the parameter (PR 3/PR 4), no ForCtorParam("ownerId") is needed.
        string source = MemberId + """
            public class ProjectRow { public string Name { get; set; } = ""; public string Owner { get; set; } = ""; }

            public sealed class Project
            {
                private Project(string name, MemberId ownerId) { Name = name; OwnerId = ownerId; }
                public string Name { get; }
                public MemberId OwnerId { get; }
                public static Project Restore(string name, MemberId ownerId) => new(name, ownerId);
            }

            public class ProjectProfile : MappingProfile
            {
                public ProjectProfile()
                {
                    CreateMap<ProjectRow, Project>()
                        .ConstructWith(Project.Restore)
                        .ForMember(d => d.OwnerId, o => o.MapFrom(s => MemberId.From(System.Guid.Parse(s.Owner))));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var id = System.Guid.NewGuid();
                    return $"{new ProjectRow { Name = "p", Owner = id.ToString() }.MapToProject().OwnerId.Value == id}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True", run);
        Assert.Contains("ownerId: global::App.MemberId.From(global::System.Guid.Parse(source.Owner)))", Generated(generated, "Project"));
    }

    [Fact]
    public void CollectionElements_ArrayOfGuidToReadOnlyListOfValueObjects_AndBack()
    {
        string source = MemberId + """
            public class TeamRow { public System.Guid[] Members { get; set; } = System.Array.Empty<System.Guid>(); }
            public class Team { public IReadOnlyList<MemberId> Members { get; set; } = new List<MemberId>(); }

            public class TeamProfile : MappingProfile
            {
                public TeamProfile()
                {
            PAIRS
                    CreateMap<TeamRow, Team>().ReverseMap();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var ids = new[] { System.Guid.NewGuid(), System.Guid.NewGuid() };
                    var team = new TeamRow { Members = ids }.MapToTeam();
                    var back = team.MapToTeamRow();
                    return $"{team.Members.Count},{team.Members[1].Value == ids[1]},{back.Members[0] == ids[0]}";
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("2,True,True", run);
        Assert.Contains("result.Add(source[i].MapToMemberId());", Generated(generated, "Team"));
    }

    [Fact]
    public void AttributeMapping_UsesThePairMappingOfAProfile()
    {
        string source = MemberId + """
            [IronMapper.Attributes.MapTo(typeof(NoteCard))]
            public class Note { public System.Guid Author { get; set; } }
            public class NoteCard { public MemberId Author { get; set; } }

            public class PairProfile : MappingProfile
            {
                public PairProfile()
                {
            PAIRS
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var id = System.Guid.NewGuid();
                    return $"{new Note { Author = id }.MapToNoteCard().Author.Value == id}";
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True", run);
    }

    [Fact]
    public void PairWithoutMapping_IM0002AsBefore_PropertyNotGenerated_RestIs()
    {
        string source = MemberId + """
            public class TaskRow { public System.Guid Assignee { get; set; } public string Title { get; set; } = ""; }
            public class TaskCard { public MemberId Assignee { get; set; } public string Title { get; set; } = ""; }

            public class TaskProfile : MappingProfile
            {
                public TaskProfile() { CreateMap<TaskRow, TaskCard>(); }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0002 = Assert.Single(diagnostics);
        Assert.Equal("IM0002", im0002.Id);
        Assert.Equal(
            "Property 'Assignee' of 'App.TaskCard' is not mapped: 'TaskRow.Assignee' is 'System.Guid', which cannot be assigned to 'App.MemberId'. Configure it with ForMember(d => d.Assignee, o => o.MapFrom(...)) that converts the value, with o.UseConverter<T>() (an ITypeConverter<System.Guid, App.MemberId>), or with o.Ignore().",
            Message(im0002));
        // No code fix for a value: [MapTo] cannot be added to Guid, and a property-by-property mapping would not convert it.
        Assert.False(im0002.Properties.ContainsKey("PairSourceType"));
        string code = Generated(generated, "TaskCard");
        Assert.Contains("Title = source.Title,", code);
        Assert.DoesNotContain("Assignee", code);
    }

    [Fact]
    public void ExplicitConfiguration_WinsOverThePairMapping()
    {
        string source = MemberId + """
            public class TaskRow { public System.Guid Assignee { get; set; } public System.Guid Reviewer { get; set; } }
            public class TaskCard { public MemberId Assignee { get; set; } public MemberId Reviewer { get; set; } }

            public class TaskProfile : MappingProfile
            {
                public TaskProfile()
                {
            PAIRS
                    CreateMap<TaskRow, TaskCard>()
                        .ForMember(d => d.Assignee, o => o.MapFrom(s => MemberId.From(s.Reviewer)))
                        .ForMember(d => d.Reviewer, o => o.Ignore());
                }
            }
            """.Replace("PAIRS", PairMaps);

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        string code = Generated(generated, "TaskCard");
        Assert.Contains("Assignee = global::App.MemberId.From(source.Reviewer),", code);
        Assert.DoesNotContain("Reviewer =", code);
    }

    [Fact]
    public void ReferenceValueObject_NullableSourceToNullableDestination_NullToNull()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public sealed class Email
            {
                private Email(string value) => Value = value;
                public string Value { get; }
                public static Email Parse(string value) => new(value.Trim().ToLowerInvariant());
            }

            public class UserRow { public string Email { get; set; } = ""; public string? Backup { get; set; } }
            public class User { public Email Email { get; set; } = Email.Parse("x"); public Email? Backup { get; set; } }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<string, Email>().ConvertUsing(Email.Parse);
                    CreateMap<UserRow, User>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var user = new UserRow { Email = " A@B.C ", Backup = null }.MapToUser();
                    return $"{user.Email.Value},{user.Backup is null}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("a@b.c,True", run);
        string code = Generated(generated, "User");
        Assert.Contains("Email = MapToEmailCore(source.Email),", code);
        Assert.Contains("Backup = source.Backup is null ? null : MapToEmailCore(source.Backup),", code);
    }

    [Fact]
    public void PairMappingInAReferencedAssembly_CalledAsExtensionMethod_LiftedToo()
    {
        string library = MemberId.Replace("namespace App;", "namespace Lib;") + """
            public class PairProfile : MappingProfile
            {
                public PairProfile()
                {
            PAIRS
                }
            }
            """.Replace("PAIRS", PairMaps);
        var (libCompilation, libProblems) = GeneratorTestHelper.CompileProject("Lib", library);
        Assert.Empty(libProblems);
        var (reference, _) = GeneratorTestHelper.EmitReference(libCompilation);

        string app = """
            using IronMapper.Configuration;
            using Lib;

            namespace App;

            public class Row { public System.Guid Owner { get; set; } public System.Guid? Deputy { get; set; } }
            public class Card { public MemberId Owner { get; set; } public MemberId? Deputy { get; set; } }

            public class CardProfile : MappingProfile
            {
                public CardProfile() { CreateMap<Row, Card>(); }
            }
            """;
        var (appCompilation, problems) = GeneratorTestHelper.CompileProject("App", app, reference);

        Assert.Empty(problems);
        string code = appCompilation.SyntaxTrees.Select(t => t.ToString()).Single(s => s.Contains("MapToCard(this global::App.Row source)"));
        Assert.Contains("Owner = source.Owner.MapToMemberId(),", code);
        Assert.Contains("Deputy = source.Deputy.HasValue ? (global::Lib.MemberId?)source.Deputy.Value.MapToMemberId() : null,", code);
    }

    [Fact]
    public void CSharp8_LiftedAndCheckedValues_CompileWithoutWarnings()
    {
        // No target-typed conditional before C# 9: the generated `x ? (T?)… : null` casts.
        string source = """
            using IronMapper.Configuration;

            namespace App
            {
                public struct Code
                {
                    private Code(int value) { Value = value; }
                    public int Value { get; }
                    public static Code From(int value) => new Code(value);
                }

                public class Row { public int? A { get; set; } public string? B { get; set; } }
                public class Card { public Code? A { get; set; } public Code? B { get; set; } }

                public class CardProfile : MappingProfile
                {
                    public CardProfile()
                    {
                        CreateMap<int, Code>().ConvertUsing(Code.From);
                        CreateMap<string, Code>().ConvertUsing(s => Code.From(s.Length));
                        CreateMap<Row, Card>();
                    }
                }
            }
            """;
        var csharp8 = new CSharpParseOptions(LanguageVersion.CSharp8);
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source })
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, csharp8));

        var driver = CSharpGeneratorDriver.Create(new[] { new IronMapperGenerator().AsSourceGenerator() }, parseOptions: csharp8)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        string code = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).Single(s => s.Contains("MapToCard(this global::App.Row source)"));
        Assert.Contains("A = source.A.HasValue ? (global::App.Code?)MapToCodeCore(source.A.Value) : null,", code);
        Assert.Contains("B = source.B is null ? null : (global::App.Code?)MapToCodeCore(source.B),", code);
    }

    [Fact]
    public void ReverseMapWithConverter_BothDirections_NullableLiftedBack_FactoryParameter()
    {
        // One line for the pair: ConvertUsing for Guid → MemberId, ReverseMap(v => v.Value) for MemberId → Guid.
        string source = MemberId + """
            public class MemberRow { public System.Guid Owner { get; set; } public System.Guid? Deputy { get; set; } }

            public sealed class Board
            {
                private Board(MemberId owner, MemberId? deputy) { Owner = owner; Deputy = deputy; }
                public MemberId Owner { get; }
                public MemberId? Deputy { get; }
                public static Board Restore(MemberId owner, MemberId? deputy) => new(owner, deputy);
            }

            public class BoardProfile : MappingProfile
            {
                public BoardProfile()
                {
                    CreateMap<System.Guid, MemberId>().ConvertUsing(MemberId.From).ReverseMap(v => v.Value);
                    CreateMap<MemberRow, Board>().ConstructWith(Board.Restore);
                    CreateMap<Board, MemberRow>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new MemberRow { Owner = System.Guid.NewGuid(), Deputy = System.Guid.NewGuid() };
                    var board = row.MapToBoard();
                    var back = board.MapToMemberRow();
                    var empty = new MemberRow { Owner = row.Owner }.MapToBoard().MapToMemberRow();
                    return $"{board.Owner.Value == row.Owner},{board.Deputy?.Value == row.Deputy},{back.Owner == row.Owner},{back.Deputy == row.Deputy},{empty.Deputy is null}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,True,True,True,True", run);
        Assert.Contains("return source.Value;", generated.Single(s => s.Contains("private static global::System.Guid MapToGuidCore(global::App.MemberId source)")));
        Assert.Contains("owner: MapToMemberIdCore(source.Owner),", Generated(generated, "Board"));
        string back = generated.Single(s => s.Contains("MapToMemberRow(this global::App.Board source)")).ReplaceLineEndings("\n");
        Assert.Contains("Owner = MapToGuidCore(source.Owner),", back);
        Assert.Contains("Deputy = source.Deputy.HasValue ? (global::System.Guid?)MapToGuidCore(source.Deputy.Value) : null,", back);
    }

    [Fact]
    public void ReverseMapWithConverter_StaticMethodGroup()
    {
        string source = MemberId + """
            public static class Ids { public static System.Guid ToGuid(MemberId id) => id.Value; }

            public class Row { public System.Guid Owner { get; set; } }
            public class Card { public MemberId Owner { get; set; } }

            public class CardProfile : MappingProfile
            {
                public CardProfile()
                {
                    CreateMap<System.Guid, MemberId>().ConvertUsing(MemberId.From).ReverseMap(Ids.ToGuid);
                    CreateMap<Row, Card>().ReverseMap();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var id = System.Guid.NewGuid();
                    return $"{new Row { Owner = id }.MapToCard().MapToRow().Owner == id}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True", run);
        Assert.Contains("return global::App.Ids.ToGuid(source);", generated.Single(s => s.Contains("private static global::System.Guid MapToGuidCore(")));
    }

    [Fact]
    public void ReverseMapWithConverter_InstanceMethod_IM0022NamesTheReverseMapping()
    {
        string source = MemberId + """
            public class Converters { public System.Guid Back(MemberId id) => id.Value; }

            public class PairProfile : MappingProfile
            {
                public PairProfile() { CreateMap<System.Guid, MemberId>().ConvertUsing(MemberId.From).ReverseMap(new Converters().Back); }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0022 = Assert.Single(diagnostics);
        Assert.Equal("IM0022", im0022.Id);
        Assert.Equal("Mapping from 'App.MemberId' to 'System.Guid' was not generated: 'Converters.Back' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.",
            Message(im0022));
    }

    [Theory]
    [InlineData("ConvertUsing(new Converters().Convert)", "'Converters.Convert' is an instance method, which the generated class cannot call. Pass a static method, or a lambda that does not capture an instance.")]
    [InlineData("ConvertUsing(Hidden)", "'PairProfile.Hidden' cannot be called by the generated class: 'App.PairProfile.Hidden(System.Guid)' is private. Make it public or internal.")]
    [InlineData("ConvertUsing(Stored)", "ConvertUsing takes a lambda, such as ConvertUsing(s => …), or a static method group, such as ConvertUsing(MemberId.From), written in the call; 'Stored' is neither, so the generated method cannot call it.")]
    public void ConvertUsing_ArgumentTheGeneratedCodeCannotCall_IM0022_MappingNotGenerated(string call, string reason)
    {
        string source = MemberId + """
            public class Converters { public MemberId Convert(System.Guid value) => MemberId.From(value); }

            public class PairProfile : MappingProfile
            {
                private static readonly System.Func<System.Guid, MemberId> Stored = MemberId.From;

                public PairProfile() { CreateMap<System.Guid, MemberId>().CALL; }

                private static MemberId Hidden(System.Guid value) => MemberId.From(value);
            }
            """.Replace("CALL", call);

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0022 = Assert.Single(diagnostics);
        Assert.Equal("IM0022", im0022.Id);
        Assert.Equal(DiagnosticSeverity.Error, im0022.Severity);
        Assert.Equal($"Mapping from 'System.Guid' to 'App.MemberId' was not generated: {reason}", Message(im0022));
        string argument = call.Substring("ConvertUsing(".Length, call.Length - "ConvertUsing(".Length - 1);
        Assert.Equal(argument, source.Substring(im0022.Location.SourceSpan.Start, im0022.Location.SourceSpan.Length));
        Assert.DoesNotContain(generated, s => s.Contains("MapToMemberId("));
    }
}

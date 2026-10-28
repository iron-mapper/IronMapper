using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Factories in profiles (PR 4 of 2.1.0): <c>ConstructWith(D.Restore)</c> (a static method, also with a private
/// constructor) and <c>ConstructWith((string a, int b) =&gt; …)</c> (a lambda with typed parameters), whose parameters are
/// matched like those of a constructor; <c>ForCtorParam</c> for a parameter without a property of the same name;
/// <c>ConstructUsing(s =&gt; …)</c>, written by hand as in AutoMapper. The cases are the forms found in a real project, with
/// neutral names.
/// </summary>
public class FactoryMappingTests
{
    private static Diagnostic[] Of(string id, IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == id).ToArray();

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    // Line endings of the generated code (Environment.NewLine) and of this file may differ.
    private static string Generated(IEnumerable<string> sources, string mapping)
        => sources.Single(s => s.Contains($" global::App.{mapping} MapTo{mapping}(this ")).ReplaceLineEndings("\n");

    private static void AssertCode(string expected, string code) => Assert.Contains(expected.ReplaceLineEndings("\n"), code);

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    private const string Team = """
        using System.Collections.Generic;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        // A value object from a Guid: a private constructor, a factory that rejects Guid.Empty.
        public readonly record struct MemberId
        {
            private MemberId(System.Guid value) => Value = value;
            public System.Guid Value { get; }
            public static MemberId From(System.Guid value)
                => value == System.Guid.Empty ? throw new System.ArgumentException("empty id", nameof(value)) : new(value);
        }

        public class TeamRow
        {
            public System.Guid Owner { get; set; }
            public string Name { get; set; } = "";
            public System.Guid[] MemberIds { get; set; } = System.Array.Empty<System.Guid>();
            public string? Motto { get; set; }
            public string Extra { get; set; } = "";
        }

        public sealed class Team
        {
            private Team(MemberId owner, string name, IReadOnlyList<System.Guid> memberIds)
            {
                Owner = owner; Name = name; MemberIds = memberIds;
            }

            public MemberId Owner { get; }
            public string Name { get; }
            public IReadOnlyList<System.Guid> MemberIds { get; }
            public string? Motto { get; set; }

            public static Team Restore(MemberId owner, string name, IReadOnlyList<System.Guid> memberIds) => new(owner, name, memberIds);
        }
        """;

    [Fact]
    public void ConstructWith_StaticFactory_PrivateConstructor_ValueObjectThroughMapFrom_ArrayToReadOnlyList_PropertyAfterTheCall()
    {
        string source = Team + """

            public class TeamProfile : MappingProfile
            {
                public TeamProfile()
                {
                    CreateMap<TeamRow, Team>()
                        .ConstructWith(Team.Restore)
                        .ForMember(d => d.Owner, o => o.MapFrom(s => MemberId.From(s.Owner)));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var owner = System.Guid.NewGuid();
                    var row = new TeamRow { Owner = owner, Name = "core", MemberIds = new[] { owner, owner }, Motto = "ship", Extra = "x" };
                    var team = row.MapToTeam();
                    return $"{team.Owner.Value == owner},{team.Name},{team.MemberIds.Count},{team.Motto}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,core,2,ship", run);
        string code = Generated(generated, "Team");
        AssertCode("""
                        var destination = global::App.Team.Restore(
                            owner: global::App.MemberId.From(source.Owner),
                            name: source.Name,
                            memberIds: source.MemberIds);
                        destination.Motto = source.Motto;
                        return destination;
            """, code);
        // No in-place overload: the private method of the IMapper registration throws.
        Assert.DoesNotContain("MapToTeam(this global::App.TeamRow source, global::App.Team destination)", code);
        Assert.Contains("No in-place mapping from TeamRow to Team is generated: Team is created by factory 'Team.Restore'. Map to a new object instead.", code);
    }

    [Fact]
    public void ConstructWith_MinimalFactory_ExtraSourceProperties_NoDiagnostics()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class TagRow { public string Name { get; set; } = ""; public string Color { get; set; } = ""; public int UsageCount { get; set; } }

            public sealed class Tag
            {
                private Tag(string name) => Name = name;
                public string Name { get; }
                public static Tag Create(string name) => new(name.Trim());
            }

            public class TagProfile : MappingProfile
            {
                public TagProfile() { CreateMap<TagRow, Tag>().ConstructWith(Tag.Create); }
            }

            public static class Program
            {
                public static string Run() => new TagRow { Name = " urgent ", Color = "red", UsageCount = 4 }.MapToTag().Name;
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("urgent", run);
        AssertCode("""
                        return global::App.Tag.Create(
                            name: source.Name);
            """, Generated(generated, "Tag"));
    }

    [Fact]
    public void ConstructWith_FourOptionalParameters_WithoutSource_KeepTheirDefaults_NullableEnum()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public enum Theme { Light, Dark }

            public class PreferencesRow
            {
                public System.Guid Id { get; set; }
                public string? Locale { get; set; }
                public Theme? Theme { get; set; }
            }

            public sealed class Preferences
            {
                private Preferences() { }
                public System.Guid Id { get; private set; }
                public string? Locale { get; private set; }
                public int? PageSize { get; private set; }
                public Theme? Theme { get; private set; }
                public bool? Beta { get; private set; }

                public static Preferences Restore(System.Guid id, string? locale = null, int? pageSize = null, Theme? theme = null,
                    bool? beta = null)
                    => new() { Id = id, Locale = locale, PageSize = pageSize ?? 20, Theme = theme, Beta = beta };
            }

            public class PreferencesProfile : MappingProfile
            {
                public PreferencesProfile() { CreateMap<PreferencesRow, Preferences>().ConstructWith(Preferences.Restore); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var p = new PreferencesRow { Id = System.Guid.Empty, Locale = "de", Theme = Theme.Dark }.MapToPreferences();
                    var q = new PreferencesRow { Id = System.Guid.Empty }.MapToPreferences();
                    return $"{p.Locale},{p.PageSize},{p.Theme},{p.Beta is null}|{q.Locale is null},{q.Theme is null}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("de,20,Dark,True|True,True", run);
        AssertCode("""
                        return global::App.Preferences.Restore(
                            id: source.Id,
                            locale: source.Locale,
                            theme: source.Theme);
            """, Generated(generated, "Preferences"));
    }

    [Fact]
    public void ConstructWith_ParameterBuiltByANestedFactory_NestedPairAndFlatColumns()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class SettingsRow { public int PageSize { get; set; } public string Locale { get; set; } = ""; }

            public sealed class Settings
            {
                private Settings(int pageSize, string locale) { PageSize = pageSize; Locale = locale; }
                public int PageSize { get; }
                public string Locale { get; }
                public static Settings Create(int pageSize, string locale) => new(pageSize, locale);
            }

            public sealed class Account
            {
                private Account(System.Guid id, Settings settings) { Id = id; Settings = settings; }
                public System.Guid Id { get; }
                public Settings Settings { get; }
                public static Account Restore(System.Guid id, Settings settings) => new(id, settings);
            }

            // The settings as a nested object of the source: mapped by the pair's mapping, itself a factory.
            public class AccountRow { public System.Guid Id { get; set; } public SettingsRow Settings { get; set; } = new(); }

            // The settings as columns of the same row: the parameter is built by calling the nested factory.
            public class FlatAccountRow { public System.Guid Id { get; set; } public int PageSize { get; set; } public string Locale { get; set; } = ""; }

            public class AccountProfile : MappingProfile
            {
                public AccountProfile()
                {
                    CreateMap<SettingsRow, Settings>().ConstructWith(Settings.Create);
                    CreateMap<AccountRow, Account>().ConstructWith(Account.Restore);
                    CreateMap<FlatAccountRow, Account>()
                        .ConstructWith(Account.Restore)
                        .ForCtorParam("settings", o => o.MapFrom(s => Settings.Create(s.PageSize, s.Locale)));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var nested = new AccountRow { Settings = new SettingsRow { PageSize = 50, Locale = "fr" } }.MapToAccount();
                    var flat = new FlatAccountRow { PageSize = 10, Locale = "it" }.MapToAccount();
                    return $"{nested.Settings.PageSize},{nested.Settings.Locale}|{flat.Settings.PageSize},{flat.Settings.Locale}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("50,fr|10,it", run);
        var accounts = generated.Where(s => s.Contains(" global::App.Account MapToAccount(this ")).Select(s => s.ReplaceLineEndings("\n")).ToList();
        Assert.Contains(accounts, code => code.Contains("settings: MapToSettingsCore(source.Settings ?? NestedObjectIsNull<global::App.SettingsRow>("));
        Assert.Contains(accounts, code => code.Contains("settings: global::App.Settings.Create(source.PageSize, source.Locale));"));
    }

    [Fact]
    public void ConstructWith_TypeWithPublicConstructorAndFactory_TheFactoryIsUsed()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class ContactRow { public string Email { get; set; } = ""; }

            public sealed class Contact
            {
                public Contact(string email) => Email = email;
                public string Email { get; }
                public static Contact Create(string email) => new(email.Trim().ToLowerInvariant());
            }

            public class ContactCopy { public string Email { get; set; } = ""; }

            public class ContactProfile : MappingProfile
            {
                public ContactProfile()
                {
                    CreateMap<ContactRow, Contact>().ConstructWith(Contact.Create);
                    // Without ConstructWith the public constructor is chosen.
                    CreateMap<ContactCopy, Contact>();
                }
            }

            public static class Program
            {
                public static string Run()
                    => new ContactRow { Email = " A@B.C " }.MapToContact().Email + "|" + new ContactCopy { Email = " A@B.C " }.MapToContact().Email;
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("a@b.c| A@B.C ", run);
    }

    [Fact]
    public void ConstructWith_LambdaWithTypedParameters_ChoosesTheConstructor_HelperWithItsBody()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class PersonRow { public string Name { get; set; } = ""; public int Age { get; set; } public string? Nick { get; set; } }

            public class Person
            {
                public Person(string name) => Name = name;
                public Person(string name, int age) { Name = name; Age = age; }
                public string Name { get; }
                public int Age { get; }
                public string? Nick { get; set; }
            }

            public class PersonProfile : MappingProfile
            {
                public PersonProfile()
                {
                    // Both constructors could be called; the lambda takes the one with one parameter.
                    CreateMap<PersonRow, Person>().ConstructWith((string name) => new Person(name.ToUpperInvariant()));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var p = new PersonRow { Name = "ann", Age = 30, Nick = "a" }.MapToPerson();
                    return $"{p.Name},{p.Age},{p.Nick}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("ANN,0,a", run);
        string code = Generated(generated, "Person");
        AssertCode("""
                        var destination = ConstructWith_PersonRow_Person(
                            name: source.Name);
                        destination.Nick = source.Nick;
                        return destination;
            """, code);
        AssertCode("""
                    private static global::App.Person ConstructWith_PersonRow_Person(string name)
                        => new global::App.Person(name.ToUpperInvariant());
            """, code);
    }

    [Fact]
    public void ConstructWith_LambdaWithBlockBody_AndDefaultValue_AndHooks()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public static class Limits { public const int DefaultSize = 7; }

            public class BoxRow { public string Label { get; set; } = ""; }

            public class Box
            {
                public Box(string label, int size) { Label = label; Size = size; }
                public string Label { get; }
                public int Size { get; }
                public string Trace { get; set; } = "";
            }

            public class BoxProfile : MappingProfile
            {
                public BoxProfile()
                {
                    CreateMap<BoxRow, Box>()
                        .ConstructWith((string label, int size = Limits.DefaultSize) =>
                        {
                            var trimmed = label.Trim();
                            return new Box(trimmed, size);
                        })
                        .AfterMap((s, d) => d.Trace = d.Label + ":" + d.Size);
                }
            }

            public static class Program
            {
                public static string Run() => new BoxRow { Label = " lid " }.MapToBox().Trace;
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("lid:7", run);
        string code = Generated(generated, "Box");
        AssertCode("""
                    private static global::App.Box ConstructWith_BoxRow_Box(string label, int size = global::App.Limits.DefaultSize)
                    {
                        var trimmed = label.Trim();
                        return new global::App.Box(trimmed, size);
                    }
            """, code);
        Assert.Contains("var destination = ConstructWith_BoxRow_Box(", code);
    }

    [Fact]
    public void ForCtorParam_ParameterWithoutPropertyOfTheSameName_MapFrom_NameOfAndLiteral()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class AuthorRow { public string First { get; set; } = ""; public string Last { get; set; } = ""; public string Code { get; set; } = ""; }

            public class Author
            {
                public Author(string display, string code) { Text = display; Id = code; }
                public string Text { get; }
                public string Id { get; }
            }

            public class AuthorProfile : MappingProfile
            {
                public AuthorProfile()
                {
                    CreateMap<AuthorRow, Author>()
                        .ForCtorParam("display", o => o.MapFrom(s => s.First + " " + s.Last))
                        .ForCtorParam(nameof(AuthorRow.Code), o => o.MapFrom(s => s.Code.ToUpperInvariant()));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var a = new AuthorRow { First = "Ada", Last = "L", Code = "x1" }.MapToAuthor();
                    return $"{a.Text},{a.Id}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("Ada L,X1", run);
    }

    [Fact]
    public void ForCtorParam_UnknownName_IM0019AtTheName_MappingNotGenerated()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class AuthorRow { public string Name { get; set; } = ""; }
            public record Author(string Name);
            public class AuthorDto { public string Name { get; set; } = ""; }

            public class AuthorProfile : MappingProfile
            {
                public AuthorProfile()
                {
                    CreateMap<AuthorRow, Author>().ForCtorParam("nmae", o => o.MapFrom(s => s.Name));
                    CreateMap<AuthorRow, AuthorDto>().ForCtorParam("name", o => o.MapFrom(s => s.Name));
                }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0019 = Of("IM0019", diagnostics).OrderBy(d => d.Location.SourceSpan.Start).ToArray();
        Assert.Equal(2, im0019.Length);
        Assert.All(im0019, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(
            "Mapping from 'App.AuthorRow' to 'App.Author' was not generated: ForCtorParam(\"nmae\") names no parameter of the constructor of 'Author' that the generator chose. Its parameters: Name.",
            Message(im0019[0]));
        Assert.Equal("\"nmae\"", source.Substring(im0019[0].Location.SourceSpan.Start, im0019[0].Location.SourceSpan.Length));
        Assert.StartsWith(
            "Mapping from 'App.AuthorRow' to 'App.AuthorDto' was not generated: ForCtorParam(\"name\") names no parameter: 'AuthorDto' is created with an object initializer",
            Message(im0019[1]));
        Assert.DoesNotContain(generated, s => s.Contains("MapToAuthor(") || s.Contains("MapToAuthorDto("));
    }

    [Fact]
    public void ConstructWith_FactoryParameterWithoutSource_IM0009NamesTheFactory()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class TagRow { public string Name { get; set; } = ""; }

            public sealed class Tag
            {
                private Tag(string name, int weight) { Name = name; Weight = weight; }
                public string Name { get; }
                public int Weight { get; }
                public static Tag Create(string name, int weight) => new(name, weight);
            }

            public class TagProfile : MappingProfile
            {
                public TagProfile() { CreateMap<TagRow, Tag>().ConstructWith(Tag.Create); }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0009 = Assert.Single(Of("IM0009", diagnostics));
        Assert.StartsWith("Mapping to 'App.Tag' was not generated: parameter 'weight' of factory 'Tag.Create' gets no value: 'TagRow' has no property named 'weight'",
            Message(im0009));
        Assert.DoesNotContain(generated, s => s.Contains("MapToTag("));
    }

    [Fact]
    public void ConstructWith_ValueObjectParameterWithoutPairMapping_IM0002AsForAProperty()
    {
        string source = Team + """

            public class TeamProfile : MappingProfile
            {
                public TeamProfile() { CreateMap<TeamRow, Team>().ConstructWith(Team.Restore); }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        // Owner exists in the source, but it is a Guid and the parameter a MemberId, and no mapping converts Guid to
        // MemberId: IM0002 with the reason of an assignment; the argument is default! so the generated file compiles.
        AssertNoErrors(errors);
        Assert.Empty(Of("IM0009", diagnostics));
        Assert.StartsWith("Property 'owner' of 'App.Team' is not mapped: 'TeamRow.Owner' is 'System.Guid', which cannot be assigned to 'App.MemberId'. Configure it with ForMember(d => d.Owner, o => o.MapFrom(...)) that converts the value",
            Message(Assert.Single(Of("IM0002", diagnostics))));
    }

    [Fact]
    public void ParameterWithoutPropertyOfTheSameName_IncompatibleSource_IM0002HintsForCtorParam()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class BadgeRow { public string Code { get; set; } = ""; }

            public sealed class Badge
            {
                private Badge(System.Guid code) => Id = code;
                public System.Guid Id { get; }
                public static Badge Create(System.Guid code) => new(code);
            }

            public class BadgeProfile : MappingProfile
            {
                public BadgeProfile() { CreateMap<BadgeRow, Badge>().ConstructWith(Badge.Create); }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        // string → Guid needs a conversion; without a mapping of the pair, IM0002 (the argument is default!).
        Assert.Contains("Configure it with ForCtorParam(\"code\", o => o.MapFrom(...)) that converts the value",
            Message(Assert.Single(Of("IM0002", diagnostics))));
    }

    [Theory]
    [InlineData("ConstructWith(new TeamProfile().Make)", "'TeamProfile.Make' is an instance method; ConstructWith takes a static method")]
    [InlineData("ConstructWith(Hidden)", "'TeamProfile.Hidden' cannot be called by the generated class: 'App.TeamProfile.Hidden(App.MemberId, string, System.Collections.Generic.IReadOnlyList<System.Guid>)' is private. Make it public or internal.")]
    [InlineData("ConstructWith(Other.Create)", "'Other.Create' returns 'App.Other', not 'App.Team' or a type derived from it.")]
    [InlineData("ConstructWith(Other.Find)", "'Other.Find' returns 'Team?', which may be null, and the mapping returns 'Team'.")]
    [InlineData("ConstructWith(Other.Load)", "the parameter 'name' of 'Other.Load' is passed by reference (ref)")]
    public void ConstructWith_MethodTheGeneratedCodeCannotUse_IM0020AtTheArgument(string call, string reason)
    {
        string source = Team + $$"""

            public class Other
            {
                public static Other Create(string name) => new();
                public static Team? Find(string name) => null;
                public static Team Load(ref string name) => throw new System.NotSupportedException();
            }

            public class TeamProfile : MappingProfile
            {
                public TeamProfile() { CreateMap<TeamRow, Team>().{{call}}; }

                public Team Make(string name) => throw new System.NotSupportedException();

                private static Team Hidden(MemberId owner, string name, IReadOnlyList<System.Guid> memberIds) => Team.Restore(owner, name, memberIds);
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0020 = Assert.Single(Of("IM0020", diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, im0020.Severity);
        Assert.Contains(reason, Message(im0020));
        string argument = call["ConstructWith(".Length..^1];
        Assert.Equal(argument, source.Substring(im0020.Location.SourceSpan.Start, im0020.Location.SourceSpan.Length));
        Assert.DoesNotContain(generated, s => s.Contains("MapToTeam("));
    }

    [Fact]
    public void ConstructWith_OverloadedMethodGroup_AndUntypedLambda_IM0020SaysWhatToWrite()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class TagRow { public string Name { get; set; } = ""; }

            public sealed class Tag
            {
                private Tag(string name) => Name = name;
                public string Name { get; }
                public static Tag Create(string name) => new(name);
                public static Tag Create(string name, int weight) => new(name);
            }

            public class TagCopy { public string Name { get; set; } = ""; }

            public class TagProfile : MappingProfile
            {
                public TagProfile()
                {
                    CreateMap<TagRow, Tag>().ConstructWith(Tag.Create);
                    CreateMap<TagCopy, Tag>().ConstructWith((name) => Tag.Create(name));
                }
            }
            """;

        // The compiler reports both arguments too (CS8917: the delegate type cannot be inferred).
        var (generated, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var im0020 = Of("IM0020", diagnostics).OrderBy(d => d.Location.SourceSpan.Start).ToArray();
        Assert.Equal(2, im0020.Length);
        Assert.Contains("'Tag.Create' has 2 overloads, so the delegate type of the method group cannot be inferred. Choose one with a lambda: ConstructWith((string name) => Tag.Create(name)).",
            Message(im0020[0]));
        Assert.Contains("the parameters of the lambda need types", Message(im0020[1]));
        Assert.DoesNotContain(generated, s => s.Contains("MapToTag("));
    }

    [Fact]
    public void ConstructWith_WithConvertUsing_OrTwice_IM0020()
    {
        string source = Team + """

            public class TeamProfile : MappingProfile
            {
                public TeamProfile()
                {
                    CreateMap<TeamRow, Team>()
                        .ConvertUsing(s => Team.Restore(MemberId.From(s.Owner), s.Name, s.MemberIds))
                        .ConstructWith(Team.Restore);
                }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var im0020 = Assert.Single(Of("IM0020", diagnostics));
        Assert.Contains("ConstructWith has no effect with ConvertUsing, which creates the whole object. Remove one of them.", Message(im0020));
        Assert.DoesNotContain(generated, s => s.Contains("MapToTeam("));
    }

    [Fact]
    public void ConstructWith_InitPropertyWithASourceValue_IM0021_MappingGeneratedWithoutIt()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class NoteRow { public string Text { get; set; } = ""; public string Author { get; set; } = ""; public string Topic { get; set; } = ""; }

            public sealed class Note
            {
                private Note(string text) => Text = text;
                public string Text { get; }
                public string Author { get; init; } = "";
                public string Topic { get; init; } = "";
                public static Note Create(string text) => new(text);
            }

            public class NoteProfile : MappingProfile
            {
                public NoteProfile()
                {
                    CreateMap<NoteRow, Note>().ConstructWith(Note.Create).ForMember(d => d.Topic, o => o.Ignore());
                }
            }

            public static class Program
            {
                public static string Run() => new NoteRow { Text = "t", Author = "a" }.MapToNote().Author.Length.ToString();
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var im0021 = Assert.Single(diagnostics);
        Assert.Equal("IM0021", im0021.Id);
        Assert.Equal(DiagnosticSeverity.Warning, im0021.Severity);
        Assert.Equal(
            "Property 'Note.Author' is not mapped: it is init-only, and the destination is created by factory 'Note.Create', so it cannot be set after the call. Pass the value to the factory, or ignore the property with ForMember(d => d.Author, o => o.Ignore()).",
            Message(im0021));
        Assert.Equal("0", run);
    }

    [Fact]
    public void NoAccessibleConstructor_IM0018PointsToConstructWith()
    {
        string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Project))]
            public class ProjectRow { public System.Guid Id { get; set; } }

            public sealed class Project
            {
                private Project(System.Guid id) => Id = id;
                public System.Guid Id { get; }
                public static Project Restore(System.Guid id) => new(id);
            }
            """;

        var (_, diagnostics, _) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Equal(
            "Mapping from 'App.ProjectRow' to 'App.Project' was not generated: 'App.Project' has no public or internal constructor the generated code can call. Create it with its static factory method in a profile: CreateMap<ProjectRow, Project>().ConstructWith(Project.Create), where Create is the factory method.",
            Message(Assert.Single(Of("IM0018", diagnostics))));
    }

    [Fact]
    public void ConstructUsing_ExpressionAsWritten_SettablePropertiesAfterIt_InPlaceIgnoresIt()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class OrderRow { public int Number { get; set; } public string Status { get; set; } = ""; public string Code { get; set; } = ""; }

            public class Order
            {
                public Order() { }
                public Order(string code) => Code = code;
                public string Code { get; init; } = "";
                public int Number { get; set; }
                public string Status { get; set; } = "";
            }

            public class Invoice
            {
                public Invoice(int number) => Number = number;
                public int Number { get; }
                public string Status { get; set; } = "";
            }

            public class OrderProfile : MappingProfile
            {
                public OrderProfile()
                {
                    CreateMap<OrderRow, Order>().ConstructUsing(s => new Order("#" + s.Code));
                    CreateMap<OrderRow, Invoice>().ConstructUsing(s => new Invoice(s.Number * 10));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new OrderRow { Number = 3, Status = "open", Code = "a" };
                    var order = row.MapToOrder();
                    var existing = new Order("keep");
                    row.MapToOrder(existing);
                    var invoice = row.MapToInvoice();
                    return $"{order.Code},{order.Number},{order.Status}|{existing.Code},{existing.Number}|{invoice.Number},{invoice.Status}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        // The init property Code is left to the expression; the in-place update ignores ConstructUsing.
        Assert.Equal("#a,3,open|keep,3|30,open", run);
        AssertCode("""
                        var destination = new global::App.Order("#" + source.Code);
                        destination.Number = source.Number;
                        destination.Status = source.Status;
                        return destination;
            """, Generated(generated, "Order"));
        string invoice = Generated(generated, "Invoice");
        Assert.DoesNotContain("MapToInvoice(this global::App.OrderRow source, global::App.Invoice destination)", invoice);
        Assert.Contains("Invoice has no accessible parameterless constructor and is created by ConstructUsing", invoice);
    }

    [Fact]
    public void ConstructWith_ReverseMap_MapsByNameWithoutTheFactory()
    {
        string source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class TagRow { public string Name { get; set; } = ""; }

            public sealed class Tag
            {
                private Tag(string name) => Name = name;
                public string Name { get; }
                public static Tag Create(string name) => new(name.ToUpperInvariant());
            }

            public class TagProfile : MappingProfile
            {
                public TagProfile() { CreateMap<TagRow, Tag>().ConstructWith(Tag.Create).ReverseMap(); }
            }

            public static class Program
            {
                public static string Run() => new TagRow { Name = "x" }.MapToTag().MapToTagRow().Name;
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("X", run);
    }
}

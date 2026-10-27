using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// IM0023 (issue #54, PR 6 of 2.1.0): a value declared nullable (<c>string?</c>) goes where the generated code needs a
/// non-nullable one — a property, a constructor or factory parameter, a converter, a transformer, a collection element.
/// Up to now the compiler reported <c>CS8601</c>/<c>CS8604</c>/<c>CS8619</c> inside the generated file; now the warning is
/// at the user's code and the generated code marks that value with <c>!</c>, mapping it as before.
/// </summary>
public class NullableNarrowingTests
{
    private const string Path = "/src/Mappings.cs";

    private static Diagnostic[] Of(string id, IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == id).ToArray();

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertNoWarningsInGeneratedCode(IReadOnlyList<Diagnostic> compilation)
        => Assert.True(compilation.Count == 0, "Errors or warnings in the generated code:\n" + string.Join("\n", compilation));

    private const string Models = """
        using System.Collections.Generic;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace App;

        public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }

        public class UserRow
        {
            public string? Email { get; set; }
            public string? Name { get; set; }
            public List<string?> Tags { get; set; } = new();
            public List<Line?> Lines { get; set; } = new();
        }

        public class User
        {
            public string Email { get; set; } = "";
            public string Name { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public List<LineDto?> Lines { get; set; } = new();
        }

        public record UserCard(string Email, string Name);

        public sealed class Account
        {
            private Account(string login) => Login = login;
            public string Login { get; }
            public static Account Open(string login) => new(login);
        }

        """;

    [Fact]
    public void Property_Parameter_FactoryParameter_Elements_IM0023AtCreateMap_NoWarningInTheGeneratedFile_NullPassedAsBefore()
    {
        string source = Models + """
            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<Line, LineDto>();
                    CreateMap<UserRow, User>();
                    CreateMap<UserRow, UserCard>();
                    CreateMap<UserRow, Account>().ConstructWith(Account.Open).ForCtorParam("login", o => o.MapFrom(s => s.Email));
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var row = new UserRow { Email = null, Name = "n", Tags = new() { "a", null }, Lines = new() { new Line { Sku = "x" } } };
                    var user = row.MapToUser();
                    var card = row.MapToUserCard();
                    var account = row.MapToAccount();
                    string thrown;
                    try { new UserRow { Lines = new() { null } }.MapToUser(); thrown = "no"; }
                    catch (System.ArgumentNullException) { thrown = "ArgumentNullException"; }
                    return $"{user.Email is null},{card.Email is null},{account.Login is null},{user.Tags.Count},{user.Lines[0]!.Sku},{thrown}";
                }
            }
            """;

        var (generated, diagnostics, compilation, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoWarningsInGeneratedCode(compilation);
        // As in 2.0.0: null reaches the non-nullable member, a null element throws in the element mapping.
        Assert.Equal("True,True,True,2,x,ArgumentNullException", run);
        var im0023 = Of("IM0023", diagnostics);
        Assert.Equal(diagnostics.Count, im0023.Length);
        Assert.All(im0023, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        string[] messages = im0023.Select(Message).OrderBy(m => m, System.StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "Parameter 'Email' of 'App.UserCard' can receive null: 'UserRow.Email' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Email, o => o.MapFrom(s => s.Email!)) or s.Email ?? a default, declare the destination nullable, or ignore it with ForMember(d => d.Email, o => o.Ignore()).",
            "Parameter 'Name' of 'App.UserCard' can receive null: 'UserRow.Name' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Name, o => o.MapFrom(s => s.Name!)) or s.Name ?? a default, declare the destination nullable, or ignore it with ForMember(d => d.Name, o => o.Ignore()).",
            "Parameter 'login' of 'App.Account' can receive null: the MapFrom expression is 'string?' and the destination is 'string'. Return a non-null value from it (s => s.X! or s.X ?? a default), or declare the destination nullable.",
            "Property 'Email' of 'App.User' can receive null: 'UserRow.Email' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Email, o => o.MapFrom(s => s.Email!)) or s.Email ?? a default, declare the destination nullable, or ignore it with ForMember(d => d.Email, o => o.Ignore()).",
            "Property 'Lines' of 'App.User' can receive null: the elements of 'UserRow.Lines' are 'App.Line?', and the mapping of the elements (MapToLineDto) takes 'App.Line': a null element throws ArgumentNullException. Declare the source elements non-nullable, or remove the null elements before the mapping.",
            "Property 'Name' of 'App.User' can receive null: 'UserRow.Name' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Name, o => o.MapFrom(s => s.Name!)) or s.Name ?? a default, declare the destination nullable, or ignore it with ForMember(d => d.Name, o => o.Ignore()).",
            "Property 'Tags' of 'App.User' can receive null: the elements of 'UserRow.Tags' are 'string?' and the elements of the destination are 'string'. Map it with ForMember(d => d.Tags, o => o.MapFrom(s => s.Tags!)) if no element is null, declare the destination elements nullable, or ignore it with ForMember(d => d.Tags, o => o.Ignore()).",
        }, messages);
        // The CreateMap call, or the MapFrom expression.
        Assert.All(im0023.Where(d => !Message(d).Contains("MapFrom expression")),
            d => Assert.StartsWith("CreateMap<", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)));
        Assert.Equal("s.Email", source.Substring(im0023.Single(d => Message(d).Contains("MapFrom expression")).Location.SourceSpan.Start, "s.Email".Length));

        string user = generated.Single(s => s.Contains("MapToUserCore(global::App.UserRow source)"));
        Assert.Contains("Email = source.Email!,", user);
        Assert.Contains("destination.Email = source.Email!;", user);   // the in-place overload too
        Assert.Contains("Tags = source.Tags!,", user);                  // List<string?> assigned as is (was IM0002)
        Assert.Contains("result.Add(source[i]!.MapToLineDto());", user);
        Assert.Contains("Email: source.Email!,", generated.Single(s => s.Contains("MapToUserCardCore(")));
        Assert.Contains("login: source.Email!)", generated.Single(s => s.Contains("MapToAccountCore(")));
    }

    [Theory]
    [InlineData(".ForMember(d => d.Email, o => o.MapFrom(s => s.Email!))", "source.Email!")]
    [InlineData(".ForMember(d => d.Email, o => o.MapFrom(s => s.Email ?? \"\"))", "source.Email ?? \"\"")]
    [InlineData(".ForMember(d => d.Email, o => o.Ignore())", null)]
    public void FixedInTheProfile_NoIM0023(string configuration, string? expected)
    {
        string source = Models + """
            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserRow, User>()CONFIG
                        .ForMember(d => d.Name, o => o.MapFrom(s => s.Name!))
                        .ForMember(d => d.Tags, o => o.MapFrom(s => s.Tags!))
                        .ForMember(d => d.Lines, o => o.Ignore());
                }
            }
            """.Replace("CONFIG", configuration);

        var (generated, diagnostics, compilation) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoWarningsInGeneratedCode(compilation);
        Assert.Empty(diagnostics);
        string user = generated.Single(s => s.Contains("MapToUserCore("));
        if (expected is null)
        {
            Assert.DoesNotContain("Email =", user);
        }
        else
        {
            Assert.Contains($"Email = {expected},", user);
        }
    }

    [Fact]
    public void AttributeMapping_IM0023AtTheDestinationProperty_ConverterThatTakesANonNullableValue()
    {
        string source = """
            using IronMapper.Attributes;
            using IronMapper.Interfaces;

            namespace App;

            public sealed class Upper : ITypeConverter<string, string> { public string Convert(string s) => s.ToUpperInvariant(); }
            public sealed class Safe : ITypeConverter<string?, string> { public string Convert(string? s) => s ?? ""; }

            [MapTo(typeof(TagDto))]
            public class Tag
            {
                public string? Name { get; set; }
                [MapConverter(typeof(Upper))] public string? Code { get; set; }
                [MapConverter(typeof(Safe))] public string? Label { get; set; }
            }

            public class TagDto
            {
                public string Name { get; set; } = "";
                public string? Code { get; set; }
                public string Label { get; set; } = "";
            }
            """;

        var (generated, diagnostics, compilation) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoWarningsInGeneratedCode(compilation);
        var im0023 = Of("IM0023", diagnostics).OrderBy(d => d.Location.SourceSpan.Start).ToArray();
        Assert.Equal(2, im0023.Length);
        Assert.Equal("Property 'Name' of 'App.TagDto' can receive null: 'Tag.Name' is 'string?' and the destination is 'string'. Declare the destination nullable, or map it in a profile with ForMember(d => d.Name, o => o.MapFrom(s => s.Name!)) or s.Name ?? a default; [Ignore] leaves it out.",
            Message(im0023[0]));
        Assert.Equal("Name", source.Substring(im0023[0].Location.SourceSpan.Start, im0023[0].Location.SourceSpan.Length));
        // The destination Code accepts null, but the converter does not: a converter of string? (Safe) is fine.
        Assert.Equal("Property 'Code' of 'App.TagDto' can receive null: 'Tag.Code' is 'string?' and the converter 'App.Upper' takes 'string'. Implement ITypeConverter<string?, …> to handle null, or declare the source property non-nullable.",
            Message(im0023[1]));
        string code = generated.Single(s => s.Contains("MapToTagDtoCore("));
        Assert.Contains("Code = ConverterCache<global::App.Upper>.Instance.Convert(source.Code!),", code);
        Assert.Contains("Label = ConverterCache<global::App.Safe>.Instance.Convert(source.Label),", code);
    }

    [Fact]
    public void TransformerOfTheType_NullableSourceToNullableDestination_IM0023ForTheTransformer()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Row { public string? Note { get; set; } public string Title { get; set; } = ""; }
            public class Dto { public string? Note { get; set; } public string Title { get; set; } = ""; }

            public class RowProfile : MappingProfile
            {
                public RowProfile()
                {
                    AddTransformer<string>(v => v.Trim());
                    CreateMap<Row, Dto>();
                }
            }
            """;

        var (generated, diagnostics, compilation) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoWarningsInGeneratedCode(compilation);
        Assert.Equal("Property 'Note' of 'App.Dto' can receive null: 'Row.Note' is 'string?' and the transformer of 'string' (AddTransformer<string>) takes a non-null 'string': null is passed to it. Declare the source property non-nullable, or configure the member with ForMember(d => d.Note, o => o.MapFrom(...)) (transformers do not apply to MapFrom).",
            Message(Assert.Single(diagnostics)));
        Assert.Contains("Note = TransformValue_String_Row_Dto(source.Note!),", generated.Single(s => s.Contains("MapToDtoCore(")));
    }

    [Fact]
    public void IncludeMembers_ReverseMap_ParameterWithoutProperty_Hints()
    {
        string source = """
            using IronMapper.Configuration;

            namespace App;

            public class Contact { public string? Phone { get; set; } }
            public class PersonRow { public Contact Contact { get; set; } = new(); public string? Nick { get; set; } }
            public class PersonDto { public string Phone { get; set; } = ""; }
            public class Alias { public string Nick { get; set; } = ""; }
            public class AliasDto { public string? Nick { get; set; } }
            public sealed class Handle
            {
                private Handle(string value) => Text = value;
                public string Text { get; }
                public static Handle Create(string nick) => new(nick);
            }

            public class PersonProfile : MappingProfile
            {
                public PersonProfile()
                {
                    CreateMap<PersonRow, PersonDto>().IncludeMembers(s => s.Contact);
                    CreateMap<Alias, AliasDto>().ReverseMap();
                    CreateMap<PersonRow, Handle>().ConstructWith(Handle.Create);
                }
            }
            """;

        var (_, diagnostics, compilation) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoWarningsInGeneratedCode(compilation);
        string[] messages = Of("IM0023", diagnostics).Select(Message).OrderBy(m => m, System.StringComparer.Ordinal).ToArray();
        Assert.True(messages.Length == 3, string.Join("\n", messages));
        Assert.True(true, string.Join("\n", messages));
        Assert.Contains(messages, m => m.Contains("'AliasDto.Nick' is 'string?' and the destination is 'string'. The reverse mapping of ReverseMap() maps by name only: declare it with CreateMap and use ForMember(d => d.Nick, o => o.MapFrom(s => s.Nick!)) there, or declare the destination nullable."));
        Assert.Contains(messages, m => m.Contains("'PersonRow.Nick' is 'string?' and the destination is 'string'. Map a non-null value with ForCtorParam(\"nick\", o => o.MapFrom(s => s.Nick!))"));
        Assert.Contains(messages, m => m.Contains("'PersonRow.Contact.Phone' is 'string?' and the destination is 'string'. Map a non-null value with ForMember(d => d.Phone, o => o.MapFrom(s => s.Contact.Phone!))"));
    }

    [Fact]
    public void NullableDisabledInTheProject_NoIM0023_NoWarning()
    {
        // Without nullable annotations the types are oblivious: nothing narrows, as nothing warned in 2.0.0.
        string source = Models.Replace("public string? ", "public string ").Replace("List<string?>", "List<string>").Replace("List<Line?>", "List<Line>").Replace("List<LineDto?>", "List<LineDto>") + """
            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<Line, LineDto>();
                    CreateMap<UserRow, User>();
                    CreateMap<UserRow, UserCard>();
                }
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source });
        compilation = compilation.WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable));

        CSharpGeneratorDriver.Create(new IronMapperGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Id != "CS1591"));
    }

    [Fact]
    public void WarningSuppressedInTheProject_NoWarn_NoIM0023ForIt()
    {
        // A project with <NoWarn>CS8601</NoWarn> built without the warning of the assignment (also with
        // TreatWarningsAsErrors): it gets no IM0023 for it either; the argument (CS8604) is still reported.
        string source = Models + """
            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserRow, User>().ForMember(d => d.Tags, o => o.Ignore()).ForMember(d => d.Lines, o => o.Ignore());
                    CreateMap<UserRow, UserCard>();
                }
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source });
        compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            new Dictionary<string, ReportDiagnostic> { ["CS8601"] = ReportDiagnostic.Suppress }));

        CSharpGeneratorDriver.Create(new IronMapperGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        Assert.All(diagnostics, d => Assert.StartsWith("Parameter", Message(d)));
        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public void WarningAnErrorInTheProject_WarningsAsErrorsNullable_IM0023IsAnErrorForIt()
    {
        // <WarningsAsErrors>nullable</WarningsAsErrors> made the compiler warning inside the generated file an error: the
        // build failed. IM0023 takes the severity of the warning it replaces, so the build keeps failing; a warning that
        // is not an error (CS8604 here) stays a warning.
        string source = Models + """
            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserRow, User>().ForMember(d => d.Tags, o => o.Ignore()).ForMember(d => d.Lines, o => o.Ignore());
                    CreateMap<UserRow, UserCard>();
                }
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source });
        compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            new Dictionary<string, ReportDiagnostic> { ["CS8601"] = ReportDiagnostic.Error }));

        CSharpGeneratorDriver.Create(new IronMapperGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal(
            Message(d).StartsWith("Property", System.StringComparison.Ordinal) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            d.Severity));
        Assert.Equal(2, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
        // The generated file still has no CS8601 (it would be an error there).
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Id == "CS8601");
    }

    [Theory]
    [InlineData("error", DiagnosticSeverity.Error)]
    [InlineData("none", null)]
    public void EditorConfigSeverity_Applies(string severity, DiagnosticSeverity? expected)
    {
        string source = Models + """
            public class UserProfile : MappingProfile
            {
                public UserProfile() { CreateMap<UserRow, UserCard>(); }
            }
            """;
        var parseOptions = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, Path);
        var csharp = GeneratorTestHelper.CreateCompilation(System.Array.Empty<string>()).AddSyntaxTrees(tree);
        // .editorconfig as csc reads it: parsed by the compiler, applied to the tree by its path.
        var set = AnalyzerConfigSet.Create(new[]
        {
            AnalyzerConfig.Parse($"root = true\n\n[*.cs]\ndotnet_diagnostic.IM0023.severity = {severity}\n", "/src/.editorconfig"),
        });
        csharp = csharp.WithOptions(csharp.Options.WithSyntaxTreeOptionsProvider(
            new TreeOptions(tree, set.GetOptionsForSourcePath(Path).TreeOptions)));

        var diagnostics = CSharpGeneratorDriver.Create(new[] { new IronMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
            .RunGenerators(csharp).GetRunResult().Diagnostics;

        if (expected is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal(2, diagnostics.Length);
            Assert.All(diagnostics, d => Assert.Equal(expected, d.Severity));
        }
    }

    /// <summary>Per-tree diagnostic severities from <c>.editorconfig</c>, as the compiler's own provider passes them.</summary>
    private sealed class TreeOptions : SyntaxTreeOptionsProvider
    {
        private readonly SyntaxTree _tree;
        private readonly ImmutableDictionary<string, ReportDiagnostic> _severities;

        public TreeOptions(SyntaxTree tree, ImmutableDictionary<string, ReportDiagnostic> severities)
        {
            _tree = tree;
            _severities = severities;
        }

        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = default;
            return tree == _tree && _severities.TryGetValue(diagnosticId, out severity);
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = default;
            return false;
        }
    }
}

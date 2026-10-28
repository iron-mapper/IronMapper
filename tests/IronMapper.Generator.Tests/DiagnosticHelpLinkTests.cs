using System.Reflection;
using System.Text.RegularExpressions;
using IronMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>helpLinkUri</c> of every diagnostic: the page of its code on the project site, which is made from the section
/// <c>## IMxxxx — Title</c> of <c>docs/diagnostics.md</c> (and <c>.ru.md</c>), so a code without a section would link to
/// nothing. The descriptors are found by reflection over the generator assembly, not listed here.
/// </summary>
public class DiagnosticHelpLinkTests
{
    public static TheoryData<string> DescriptorIds()
    {
        TheoryData<string> data = [];
        foreach (DiagnosticDescriptor descriptor in AllDescriptors())
        {
            data.Add(descriptor.Id);
        }

        return data;
    }

    [Fact]
    public void Descriptors_AreFound_WithUniqueIds()
    {
        List<DiagnosticDescriptor> descriptors = AllDescriptors();

        Assert.True(descriptors.Count >= 25, $"found {descriptors.Count} descriptors");
        Assert.Equal(descriptors.Count, descriptors.Select(d => d.Id).Distinct().Count());
        Assert.All(descriptors, d => Assert.Matches("^IM[0-9]{4}$", d.Id));
    }

    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void HelpLinkUri_IsThePageOfTheCode(string id)
    {
        DiagnosticDescriptor descriptor = AllDescriptors().Single(d => d.Id == id);

        Assert.Equal($"https://iron-mapper.ru/diagnostics/{id.ToLowerInvariant()}/", descriptor.HelpLinkUri);
    }

    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void Docs_HaveASectionForTheCode(string id)
    {
        foreach (string file in new[] { "diagnostics.md", "diagnostics.ru.md" })
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Docs", file));

            Assert.True(Regex.IsMatch(text, $"^## {id} — ", RegexOptions.Multiline), $"docs/{file} has no section '## {id} — …'");
        }
    }

    [Fact]
    public void ReportedDiagnostic_CarriesTheHelpLink()
    {
        const string source = """
            using IronMapper.Attributes;
            namespace App;
            [MapTo(typeof(Dto))] public class Row { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;

        (_, IReadOnlyList<Diagnostic> diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Diagnostic unmapped = Assert.Single(diagnostics, d => d.Id == "IM0001");
        Assert.Equal("https://iron-mapper.ru/diagnostics/im0001/", unmapped.Descriptor.HelpLinkUri);
    }

    private static List<DiagnosticDescriptor> AllDescriptors()
        => typeof(DiagnosticDescriptors).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();
}

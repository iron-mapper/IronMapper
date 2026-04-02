using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Models.App;
using IronMapper.Demo.Services;
using IronMapper.Generated;

namespace IronMapper.Demo.Demos;

/// <summary>
/// Demonstrates conditional mapping with When(s => s.DeathDate == null).
/// ConditionalMappingProfile registers AuthorResponse → LivingAuthorSummary
/// with a guard: the generated method returns default (null) for deceased authors.
/// </summary>
public static class Demo6_ConditionalMapping
{
    public static async Task RunAsync(OpenLibraryService api)
    {
        PrintHeader("DEMO 6 — Conditional mapping: alive authors only");

        Console.WriteLine("Loading mixed authors list (dead and alive)...\n");

        // Tolkien (OL26320A) — died 1973; Orwell (OL118077A) — died 1950
        // Stephen King (OL2162284A) — living; J.K. Rowling (OL23919A) — living
        var authors = await api.GetMultipleAuthorsAsync(
            "OL26320A", "OL118077A", "OL2162284A", "OL23919A");

        if (authors.Count == 0)
            authors = FallbackAuthors();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"SOURCE  List<AuthorResponse> — {authors.Count} authors:");
        foreach (var a in authors)
        {
            var status = a.DeathDate is null ? "living" : $"died {a.DeathDate}";
            Console.WriteLine($"  {a.Name,-30} [{status}]");
        }

        // ── Conditional mapping ───────────────────────────────────────────
        // ConditionalMappingProfile.When(s => s.DeathDate == null) generates:
        //   if (!(source.DeathDate == null)) return default!;
        // Dead authors → null; living authors → populated LivingAuthorSummary.
        var allMapped = authors
            .Select(a => a.MapToLivingAuthorSummary())
            .ToList();

        // Cast to nullable to safely filter without compiler warnings
        var living = allMapped
            .Cast<LivingAuthorSummary?>()
            .Where(a => a is not null && a.Id.Length > 0)
            .Select(a => a!)
            .ToList();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"\nRESULT  List<LivingAuthorSummary> — {living.Count} of {authors.Count}:");
        foreach (var a in living)
        {
            Console.WriteLine($"  {a.Name,-30} BirthYear={a.BirthYear}  {a.ProfileUrl}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n  Mapped {living.Count} of {authors.Count} authors (living only)");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  ConditionalMappingProfile.CreateMap<AuthorResponse, LivingAuthorSummary>()");
        Console.WriteLine("      .When(s => s.DeathDate == null)");
        Console.WriteLine();
        Console.WriteLine("  Generated code:");
        Console.WriteLine("      if (!(source.DeathDate == null)) return default!;");
        Console.WriteLine("      // ... properties set only for living authors");
        Console.ResetColor();
    }

    private static List<AuthorResponse> FallbackAuthors() =>
    [
        new() { Key = "/authors/OL26320A",   Name = "J.R.R. Tolkien",  BirthDate = "3 January 1892",  DeathDate = "2 September 1973" },
        new() { Key = "/authors/OL118077A",  Name = "George Orwell",   BirthDate = "25 June 1903",    DeathDate = "21 January 1950"  },
        new() { Key = "/authors/OL2162284A", Name = "Stephen King",    BirthDate = "21 September 1947", DeathDate = null             },
        new() { Key = "/authors/OL23919A",   Name = "J.K. Rowling",    BirthDate = "31 July 1965",    DeathDate = null               },
    ];

    private static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('─', 60));
        Console.WriteLine($" {title}");
        Console.WriteLine(new string('─', 60));
        Console.ResetColor();
    }
}

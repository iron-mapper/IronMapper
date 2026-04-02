using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IronMapper.Generated;
using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Models.App;
using IronMapper.Demo.Services;

namespace IronMapper.Demo.Demos;

/// <summary>
/// Demonstrates Fluent Profile API: BookMappingProfile configures
/// BookDoc → BookSummary with computed fields.
/// </summary>
public static class Demo2_FluentProfile
{
    public static async Task RunAsync(OpenLibraryService api)
    {
        PrintHeader("DEMO 2 — Fluent API: профиль маппингов");

        Console.WriteLine("Ищем книги по запросу \"clean code\"...\n");
        var response = await api.SearchBooksAsync("clean code", 1);
        var doc = response?.Docs?.Count > 0 ? response.Docs[0] : null;

        if (doc is null)
        {
            doc = new BookDoc
            {
                Key            = "/works/OL17802W",
                Title          = "Clean Code",
                AuthorName     = new List<string> { "Robert C. Martin" },
                FirstPublishYear = 2008,
                EditionCount   = 55,
                Subject        = new List<string> { "Computer programming", "Software engineering", "Agile" },
                CoverI         = 8091016
            };
        }

        // ── Before ──────────────────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("ИСТОЧНИК  BookDoc (сырые данные из API):");
        Console.WriteLine($"  Key:           {doc.Key}");
        Console.WriteLine($"  Title:         {doc.Title}");
        Console.WriteLine($"  AuthorName:    [{string.Join(", ", doc.AuthorName ?? new List<string>())}]");
        Console.WriteLine($"  FirstPublishYear: {doc.FirstPublishYear}");
        Console.WriteLine($"  EditionCount:  {doc.EditionCount}");
        Console.WriteLine($"  CoverI:        {doc.CoverI}");
        Console.WriteLine($"  Subject[0..2]: {TakeSubjects(doc.Subject)}");

        // ── Profile-configured mapping ────────────────────────────────────
        // BookMappingProfile.CreateMap<BookDoc, BookSummary>() was analysed
        // by the Source Generator at compile time.  The generated method:
        //   public static BookSummary MapToBookSummary(this BookDoc source) { ... }
        var summary = doc.MapToBookSummary();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nРЕЗУЛЬТАТ  BookSummary (после маппинга профилем):");
        Console.WriteLine($"  Id:             {summary.Id}");
        Console.WriteLine($"  Title:          {summary.Title}");
        Console.WriteLine($"  AuthorsDisplay: {summary.AuthorsDisplay}");
        Console.WriteLine($"  PublishedYear:  {summary.PublishedYear}");
        Console.WriteLine($"  EditionsInfo:   {summary.EditionsInfo}");
        Console.WriteLine($"  CoverUrl:       {summary.CoverUrl}");
        Console.WriteLine($"  TopSubjects:    [{string.Join(", ", summary.TopSubjects)}]");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  BookMappingProfile.CreateMap<BookDoc, BookSummary>()");
        Console.WriteLine("  Трансформации применены без рефлексии в compile time.");
        Console.ResetColor();
    }

    private static string TakeSubjects(System.Collections.Generic.List<string>? subjects)
    {
        if (subjects is null || subjects.Count == 0) return "(none)";
        return string.Join(", ", subjects.Count > 3 ? subjects.GetRange(0, 3) : subjects);
    }

    private static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('─', 60));
        Console.WriteLine($" {title}");
        Console.WriteLine(new string('─', 60));
        Console.ResetColor();
    }
}

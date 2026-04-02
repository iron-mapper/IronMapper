using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IronMapper.Attributes;
using IronMapper.Generated;
using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Services;

namespace IronMapper.Demo.Demos;

// ── Lightweight types for the attribute-based mapping demo ──────────────────
// [MapTo] tells the Source Generator to emit MapToBookQuickView() at compile time.

[MapTo(typeof(BookQuickView))]
public class BookRawEntry
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? FirstPublishYear { get; set; }
    public int? EditionCount { get; set; }
}

public class BookQuickView
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? FirstPublishYear { get; set; }
    public int? EditionCount { get; set; }
}

// ────────────────────────────────────────────────────────────────────────────

public static class Demo1_SimpleMapping
{
    public static async Task RunAsync(OpenLibraryService api)
    {
        PrintHeader("DEMO 1 — Простой маппинг атрибутами [MapTo]");

        Console.WriteLine("Ищем книгу через Open Library API...\n");
        var response = await api.SearchBooksAsync("Lord of the Rings", 1);
        var doc = response?.Docs?.Count > 0 ? response.Docs[0] : null;

        if (doc is null)
        {
            doc = new BookDoc
            {
                Key = "/works/OL27448W",
                Title = "The Lord of the Rings",
                FirstPublishYear = 1954,
                EditionCount = 120
            };
        }

        // Build a BookRawEntry from API data
        var raw = new BookRawEntry
        {
            Key      = doc.Key,
            Title    = doc.Title,
            FirstPublishYear = doc.FirstPublishYear,
            EditionCount     = doc.EditionCount
        };

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("ИСТОЧНИК  [MapTo(typeof(BookQuickView))] BookRawEntry:");
        Console.WriteLine($"  Key:              {raw.Key}");
        Console.WriteLine($"  Title:            {raw.Title}");
        Console.WriteLine($"  FirstPublishYear: {raw.FirstPublishYear}");
        Console.WriteLine($"  EditionCount:     {raw.EditionCount}");

        // ── One-line compile-time mapping ──────────────────────────────────
        // The Source Generator emitted this extension method at compile time:
        //   public static BookQuickView MapToBookQuickView(this BookRawEntry source) { ... }
        var view = raw.MapToBookQuickView();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nРЕЗУЛЬТАТ  BookQuickView:");
        Console.WriteLine($"  Key:              {view.Key}");
        Console.WriteLine($"  Title:            {view.Title}");
        Console.WriteLine($"  FirstPublishYear: {view.FirstPublishYear}");
        Console.WriteLine($"  EditionCount:     {view.EditionCount}");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  Маппинг выполнен в compile time — нет рефлексии, нет DI.");
        Console.WriteLine("  Сгенерированный вызов: raw.MapToBookQuickView()");
        Console.ResetColor();

        await Task.CompletedTask;
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

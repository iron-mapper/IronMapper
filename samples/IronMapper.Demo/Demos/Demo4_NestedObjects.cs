using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Services;
using IronMapper.Generated;

namespace IronMapper.Demo.Demos;

/// <summary>
/// Demonstrates nested-object mapping: WorkResponse → BookDetail with
/// an embedded AuthorInfo mapped separately and attached afterwards.
/// </summary>
public static class Demo4_NestedObjects
{
    private const string LordOfTheRingsWorkId = "OL27448W";
    private const string TolkienAuthorId      = "OL26320A";

    public static async Task RunAsync(OpenLibraryService api)
    {
        PrintHeader("DEMO 4 — Nested objects (book + author)");

        Console.WriteLine("Loading The Lord of the Rings and Tolkien from Open Library...\n");

        var workTask   = api.GetWorkAsync(LordOfTheRingsWorkId);
        var authorTask = api.GetAuthorAsync(TolkienAuthorId);
        await Task.WhenAll(workTask, authorTask);

        var work   = workTask.Result   ?? FallbackWork();
        var author = authorTask.Result ?? FallbackAuthor();

        // ── Step 1: map the work ─────────────────────────────────────────────
        // WorkResponse → BookDetail via BookMappingProfile
        var bookDetail = work.MapToBookDetail();

        // ── Step 2: map the author ───────────────────────────────────────────
        // AuthorResponse → AuthorInfo via BookMappingProfile
        var authorInfo = author.MapToAuthorInfo();

        // ── Step 3: compose the nested object ───────────────────────────────
        bookDetail.Author = authorInfo;

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("RESULT  BookDetail (nested object):");
        Console.WriteLine($"  Id:               {bookDetail.Id}");
        Console.WriteLine($"  Title:            {bookDetail.Title}");
        Console.WriteLine($"  FirstPublishDate: {bookDetail.FirstPublishDate}");
        Console.WriteLine($"  CoverUrl:         {bookDetail.CoverUrl}");
        Console.WriteLine($"  Subjects:         [{string.Join(", ", bookDetail.Subjects)}]");
        Console.WriteLine();
        Console.WriteLine("  Author (nested AuthorInfo):");
        Console.WriteLine($"    Id:         {bookDetail.Author?.Id}");
        Console.WriteLine($"    Name:       {bookDetail.Author?.Name}");
        Console.WriteLine($"    BirthYear:  {bookDetail.Author?.BirthYear}");
        Console.WriteLine($"    DeathYear:  {bookDetail.Author?.DeathYear}");
        Console.WriteLine($"    IsAlive:    {bookDetail.Author?.IsAlive}");
        Console.WriteLine($"    ProfileUrl: {bookDetail.Author?.ProfileUrl}");

        if (bookDetail.Description is not null)
        {
            var desc = bookDetail.Description.Length > 120
                ? bookDetail.Description.Substring(0, 120) + "..."
                : bookDetail.Description;
            Console.WriteLine($"\n  Description: {desc}");
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  work.MapToBookDetail()   — generated from WorkResponse → BookDetail");
        Console.WriteLine("  author.MapToAuthorInfo() — generated from AuthorResponse → AuthorInfo");
        Console.WriteLine("  bookDetail.Author = authorInfo; — nested object was set manually");
        Console.ResetColor();
    }

    private static WorkResponse FallbackWork() => new()
    {
        Key              = "/works/OL27448W",
        Title            = "The Lord of the Rings",
        Description      = "The Lord of the Rings is an epic high-fantasy novel by English author and scholar J. R. R. Tolkien.",
        Subjects         = ["Fantasy fiction", "Quests (Expeditions)", "Middle Earth (Imaginary place)"],
        FirstPublishDate = "29 July 1954",
        Covers           = [9255566]
    };

    private static AuthorResponse FallbackAuthor() => new()
    {
        Key       = "/authors/OL26320A",
        Name      = "J.R.R. Tolkien",
        BirthDate = "3 January 1892",
        DeathDate = "2 September 1973",
        Bio       = "John Ronald Reuel Tolkien was an English writer, poet, philologist, and university professor."
    };

    private static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('─', 60));
        Console.WriteLine($" {title}");
        Console.WriteLine(new string('─', 60));
        Console.ResetColor();
    }
}

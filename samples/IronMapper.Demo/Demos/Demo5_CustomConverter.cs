using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Profiles;
using IronMapper.Demo.Services;
using IronMapper.Generated;

namespace IronMapper.Demo.Demos;

/// <summary>
/// Demonstrates custom ITypeConverter: BioConverter strips HTML and truncates.
/// The converter is called from the compile-time generated MapToAuthorInfo() method.
/// </summary>
public static class Demo5_CustomConverter
{
    private const string TolkienAuthorId = "OL26320A";

    public static async Task RunAsync(OpenLibraryService api)
    {
        PrintHeader("DEMO 5 — Custom converter BioConverter");

        Console.WriteLine("Loading author's data from Open Library...\n");
        var author = await api.GetAuthorAsync(TolkienAuthorId) ?? FallbackAuthor();

        // ── Before conversion ─────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("BEFORE  AuthorResponse.Bio (raw data):");
        if (author.Bio is not null)
        {
            var preview = author.Bio.Length > 200 ? author.Bio.Substring(0, 200) + "..." : author.Bio;
            Console.WriteLine($"  \"{preview}\"");
            Console.WriteLine($"  Length: {author.Bio.Length} chars");
        }
        else
        {
            Console.WriteLine("  (null)");
        }

        // ── Mapping with custom converter ─────────────────────────────────
        // BookMappingProfile registers:
        //   .ForMember(d => d.Biography, o => o.MapFrom(s =>
        //       new BioConverter().Convert(s.Bio)))
        //
        // The Source Generator emitted inside MapToAuthorInfo():
        //   Biography = new global::IronMapper.Demo.Profiles.BioConverter().Convert(source.Bio),
        var authorInfo = author.MapToAuthorInfo();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nAFTER  AuthorInfo.Biography (after BioConverter):");
        if (authorInfo.Biography is not null)
        {
            Console.WriteLine($"  \"{authorInfo.Biography}\"");
            Console.WriteLine($"  Length: {authorInfo.Biography.Length} chars");
        }
        else
        {
            Console.WriteLine("  (null)");
        }

        // ── Show the converter in action directly ─────────────────────────
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  BioConverter : ITypeConverter<string?, string?>");
        Console.WriteLine("  - Removes HTML-tags with Regex.Replace");
        Console.WriteLine("  - Cuts to 300 chars");
        Console.WriteLine("  - Calls from generated code, with no reflection");

        // Demonstrate converter standalone
        var htmlSample = "This is <b>bold</b> and <i>italic</i> text with <a href=\"#\">a link</a>.";
        var converter = new BioConverter();
        var cleaned = converter.Convert(htmlSample);
        Console.WriteLine();
        Console.WriteLine($"  Example: \"{htmlSample}\"");
        Console.WriteLine($"  → \"{cleaned}\"");
        Console.ResetColor();
    }

    private static AuthorResponse FallbackAuthor() => new()
    {
        Key       = "/authors/OL26320A",
        Name      = "J.R.R. Tolkien",
        BirthDate = "3 January 1892",
        DeathDate = "2 September 1973",
        Bio       = "John Ronald Reuel Tolkien CBE FRSL was an <b>English writer</b>, poet, philologist, " +
                    "and university professor who is best known as the author of the classic " +
                    "<i>high-fantasy</i> works <i>The Hobbit</i> and <i>The Lord of the Rings</i>."
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

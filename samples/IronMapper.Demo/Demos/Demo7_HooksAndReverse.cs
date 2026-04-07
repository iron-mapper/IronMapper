using IronMapper.Configuration;
using IronMapper.Generated;

namespace IronMapper.Demo.Demos;

// ── Types for BeforeMap / AfterMap demo ─────────────────────────────────────

public class InvoiceEntity
{
    public int     Id     { get; set; }
    public string  Number { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class InvoiceDto
{
    public int      Id          { get; set; }
    public string   Number      { get; set; } = string.Empty;
    public decimal  Amount      { get; set; }
    /// <summary>Populated by BeforeMap — records when the mapping occurred.</summary>
    public DateTime MappedAt    { get; set; }
    /// <summary>Populated by AfterMap — true when Amount exceeds 1 000.</summary>
    public bool     IsHighValue { get; set; }
}

public class InvoiceProfile : MappingProfile
{
    public InvoiceProfile()
    {
        CreateMap<InvoiceEntity, InvoiceDto>()
            .BeforeMap((src, dest) => dest.MappedAt    = DateTime.UtcNow)
            .AfterMap( (src, dest) => dest.IsHighValue = dest.Amount > 1_000m);
    }
}

// ── Types for ReverseMap demo ────────────────────────────────────────────────

public class NoteEntity
{
    public int    Id      { get; set; }
    public string Title   { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class NoteDto
{
    public int    Id      { get; set; }
    public string Title   { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// ReverseMap() in a profile — the generator emits both
/// <c>MapToNoteDto()</c> and <c>MapToNoteEntity()</c> at compile time.
/// </summary>
public class NoteProfile : MappingProfile
{
    public NoteProfile()
    {
        CreateMap<NoteEntity, NoteDto>().ReverseMap();
    }
}

// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Demonstrates BeforeMap / AfterMap hooks and bidirectional ReverseMap.
/// </summary>
public static class Demo7_HooksAndReverse
{
    public static Task RunAsync()
    {
        // ── BeforeMap / AfterMap ──────────────────────────────────────────────
        PrintHeader("DEMO 7a — BeforeMap and AfterMap hooks");

        var invoice = new InvoiceEntity { Id = 101, Number = "INV-2024-001", Amount = 1_500m };

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("SOURCE  InvoiceEntity:");
        Console.WriteLine($"  Id:     {invoice.Id}");
        Console.WriteLine($"  Number: {invoice.Number}");
        Console.WriteLine($"  Amount: {invoice.Amount:C}");

        var dto = invoice.MapToInvoiceDto();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nRESULT  InvoiceDto:");
        Console.WriteLine($"  Id:          {dto.Id}");
        Console.WriteLine($"  Number:      {dto.Number}");
        Console.WriteLine($"  Amount:      {dto.Amount:C}");
        Console.WriteLine($"  MappedAt:    {dto.MappedAt:O}   ← set by BeforeMap");
        Console.WriteLine($"  IsHighValue: {dto.IsHighValue}           ← set by AfterMap");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  .BeforeMap((src, dest) => dest.MappedAt    = DateTime.UtcNow)");
        Console.WriteLine("  .AfterMap( (src, dest) => dest.IsHighValue = dest.Amount > 1000)");
        Console.ResetColor();

        // ── ReverseMap ────────────────────────────────────────────────────────
        PrintHeader("DEMO 7b — ReverseMap (bidirectional mapping)");

        var entity = new NoteEntity { Id = 1, Title = "Meeting notes", Content = "Discussed Q1 goals." };

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("SOURCE  NoteEntity → MapToNoteDto():");
        var noteDto = entity.MapToNoteDto();
        Console.WriteLine($"  Id={noteDto.Id}  Title=\"{noteDto.Title}\"");

        noteDto.Title   = "Meeting notes (updated)";
        noteDto.Content = "Added action items.";

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nEdited NoteDto → MapToNoteEntity():");
        var updated = noteDto.MapToNoteEntity();
        Console.WriteLine($"  Id:      {updated.Id}");
        Console.WriteLine($"  Title:   {updated.Title}");
        Console.WriteLine($"  Content: {updated.Content}");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  CreateMap<NoteEntity, NoteDto>().ReverseMap()");
        Console.WriteLine("  Both MapToNoteDto() and MapToNoteEntity() are generated.");
        Console.ResetColor();

        return Task.CompletedTask;
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

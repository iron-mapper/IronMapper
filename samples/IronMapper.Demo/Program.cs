using IronMapper.Demo.Demos;
using IronMapper.Demo.Services;

var api = new OpenLibraryService();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║             IronMapper Demo Application                  ║");
Console.WriteLine("║   Compile-time Object Mapper — zero reflection           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine("Using Open Library API (openlibrary.org)");
Console.WriteLine("Uses static data if API is unavailable.");

var running = true;
while (running)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("Choose scenario:");
    Console.WriteLine("  1. Simple attribute mapping [MapTo]");
    Console.WriteLine("  2. Fluent API — mapping profiles");
    Console.WriteLine("  3. Mapping Collections (lists of books)");
    Console.WriteLine("  4. Nested objects (book + author)");
    Console.WriteLine("  5. Custom converter (cleaning Bio)");
    Console.WriteLine("  6. Conditional mapping (only alive authors)");
    Console.WriteLine("  7. BeforeMap / AfterMap hooks + ReverseMap");
    Console.WriteLine("  8. IncludeMembers — flatten nested objects");
    Console.WriteLine("  0. Exit");
    Console.ResetColor();
    Console.Write("\nInput: ");

    var input = Console.ReadLine()?.Trim();
    Console.WriteLine();

    try
    {
        switch (input)
        {
            case "1": await Demo1_SimpleMapping.RunAsync(api);      break;
            case "2": await Demo2_FluentProfile.RunAsync(api);      break;
            case "3": await Demo3_Collections.RunAsync(api);        break;
            case "4": await Demo4_NestedObjects.RunAsync(api);      break;
            case "5": await Demo5_CustomConverter.RunAsync(api);    break;
            case "6": await Demo6_ConditionalMapping.RunAsync(api); break;
            case "7": await Demo7_HooksAndReverse.RunAsync();       break;
            case "8": await Demo8_IncludeMembers.RunAsync();        break;
            case "0":
                running = false;
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid input. Must be digit from 0 to 8.");
                Console.ResetColor();
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
    }
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("\nGood luck! IronMapper — compile-time mapping, zero reflection.");
Console.ResetColor();

using System;
using System.Threading.Tasks;
using IronMapper.Demo.Demos;
using IronMapper.Demo.Services;

var api = new OpenLibraryService();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║             IronMapper Demo Application                 ║");
Console.WriteLine("║   Compile-time Object Mapper — zero reflection          ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine("Использует Open Library API (openlibrary.org)");
Console.WriteLine("При недоступном API используются статические демо-данные.");

var running = true;
while (running)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("Выберите сценарий:");
    Console.WriteLine("  1. Простой маппинг атрибутами [MapTo]");
    Console.WriteLine("  2. Fluent API — профиль маппингов");
    Console.WriteLine("  3. Маппинг коллекций (список книг)");
    Console.WriteLine("  4. Вложенные объекты (книга + автор)");
    Console.WriteLine("  5. Кастомный конвертер (очистка Bio)");
    Console.WriteLine("  6. Условный маппинг (только живые авторы)");
    Console.WriteLine("  0. Выход");
    Console.ResetColor();
    Console.Write("\nВвод: ");

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
            case "0":
                running = false;
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Неверный выбор. Введите цифру от 0 до 6.");
                Console.ResetColor();
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Ошибка: {ex.Message}");
        Console.ResetColor();
    }
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("\nДо свидания! IronMapper — compile-time mapping, zero reflection.");
Console.ResetColor();

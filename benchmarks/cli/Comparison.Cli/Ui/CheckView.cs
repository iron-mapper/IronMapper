using Comparison.Cli.Parsing;
using Spectre.Console;

namespace Comparison.Cli.Ui;

/// <summary>The pre-flight checks of a script as a table (terminal) or as its own lines (plain).</summary>
internal static class CheckView
{
    public static string Mark(CheckMark mark) => mark switch
    {
        CheckMark.Ok => "[green]ok[/]",
        CheckMark.Warn => "[yellow]warn[/]",
        _ => "[red]FAIL[/]",
    };

    public static void Write(IReadOnlyList<PreflightCheck> checks)
    {
        if (checks.Count == 0)
            return;
        if (Terminal.Plain)
        {
            foreach (PreflightCheck check in checks)
                Console.WriteLine($"  [{check.Mark switch { CheckMark.Ok => "ok  ", CheckMark.Warn => "warn", _ => "FAIL" }}] {check.Name}: {check.Detail}");
            return;
        }
        var table = new Table().Border(TableBorder.Rounded).AddColumn("").AddColumn("Check").AddColumn("Detail");
        foreach (PreflightCheck check in checks)
            table.AddRow(Mark(check.Mark), Markup.Escape(check.Name), Markup.Escape(check.Detail));
        AnsiConsole.Write(table);
    }
}

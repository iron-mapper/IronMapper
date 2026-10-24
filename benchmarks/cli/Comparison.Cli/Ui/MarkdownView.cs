using Comparison.Cli.Artifacts;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Comparison.Cli.Ui;

/// <summary>Markdown of the scripts (summary.md, RESULTS.md) in the terminal: tables as tables, the rest as text.</summary>
internal static class MarkdownView
{
    // A rounded table takes 3 characters per column ("│ x ") plus the closing border.
    private const int ColumnOverhead = 3;

    /// <summary>
    /// A pipe table as terminal tables. Spectre squeezes a table wider than the terminal by wrapping cells character by
    /// character ("319032400" over three lines), so a table that does not fit is split instead: the first column is
    /// repeated, the others go into as many tables as needed (summary.md, 9 columns, becomes Mean/Ratio and Allocated at
    /// 120 columns).
    /// </summary>
    public static IRenderable ToTable(MarkdownTable source, Func<string, string>? firstColumn = null)
    {
        string Cell(IReadOnlyList<string> row, int i)
        {
            string text = i < row.Count ? Inline(row[i]) : "";
            return i == 0 && firstColumn is not null ? firstColumn(text) : text;
        }

        int columns = source.Headers.Count;
        int[] widths = Enumerable.Range(0, columns)
            .Select(i => source.Rows.Select(r => Cell(r, i).Length).Append(Inline(source.Headers[i]).Length).Max() + ColumnOverhead)
            .ToArray();
        IReadOnlyList<IReadOnlyList<int>> groups = SplitColumns(widths, AnsiConsole.Profile.Width);

        var tables = new List<IRenderable>();
        foreach (IReadOnlyList<int> group in groups)
        {
            var table = new Table().Border(TableBorder.Rounded);
            foreach (int i in group)
            {
                var column = new TableColumn(Markup.Escape(Inline(source.Headers[i])));
                ColumnAlignment alignment = i < source.Alignments.Count ? source.Alignments[i] : ColumnAlignment.Left;
                column.Alignment = alignment switch
                {
                    ColumnAlignment.Right => Justify.Right,
                    ColumnAlignment.Center => Justify.Center,
                    _ => Justify.Left,
                };
                table.AddColumn(column);
            }
            foreach (IReadOnlyList<string> row in source.Rows)
            {
                table.AddRow(group.Select(i => (IRenderable)new Markup(Markup.Escape(Cell(row, i)))).ToArray());
            }

            tables.Add(table);
        }
        return new Rows(tables);
    }

    /// <summary>
    /// Column indexes per table: the first column in every table, the others in order, as many per table as fit in
    /// <paramref name="width"/> (at least one).
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<int>> SplitColumns(IReadOnlyList<int> widths, int width)
    {
        if (widths.Count == 0)
        {
            return [];
        }

        var groups = new List<IReadOnlyList<int>>();
        var current = new List<int> { 0 };
        int used = widths[0] + 1;
        for (int i = 1; i < widths.Count; i++)
        {
            if (used + widths[i] > width && current.Count > 1)
            {
                groups.Add(current);
                current = [0];
                used = widths[0] + 1;
            }
            current.Add(i);
            used += widths[i];
        }
        groups.Add(current);
        return groups;
    }

    public static void Write(MarkdownDocument document)
    {
        foreach (MarkdownBlock block in document.Blocks)
        {
            switch (block)
            {
                case MarkdownHeading heading when heading.Level == 1:
                    AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(heading.Text)}[/]").LeftJustified());
                    break;
                case MarkdownHeading heading:
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[bold underline]{Markup.Escape(heading.Text)}[/]");
                    break;
                case MarkdownTable table:
                    AnsiConsole.Write(ToTable(table));
                    break;
                case MarkdownText text:
                    foreach (string line in text.Lines)
                    {
                        AnsiConsole.MarkupLine(Markup.Escape(Inline(line)));
                    }

                    break;
            }
        }
    }

    /// <summary>Drops the inline Markdown the scripts use (bold, code) for the terminal.</summary>
    public static string Inline(string text) => text.Replace("**", "").Replace("`", "");
}

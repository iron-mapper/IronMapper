namespace Comparison.Cli.Artifacts;

internal abstract record MarkdownBlock;

internal sealed record MarkdownHeading(int Level, string Text) : MarkdownBlock;

internal sealed record MarkdownText(IReadOnlyList<string> Lines) : MarkdownBlock;

internal enum ColumnAlignment
{
    Left,
    Right,
    Center,
}

internal sealed record MarkdownTable(IReadOnlyList<string> Headers, IReadOnlyList<ColumnAlignment> Alignments, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

/// <summary>
/// The Markdown the scripts write (summary.md, RESULTS.md), split into headings, tables and other text, so the terminal
/// can show tables as tables. Only what those files use: ATX headings and pipe tables; everything else is text.
/// </summary>
internal sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks)
{
    public static MarkdownDocument Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var text = new List<string>();
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        bool inFence = false;

        void FlushText()
        {
            while (text.Count > 0 && string.IsNullOrWhiteSpace(text[^1]))
            {
                text.RemoveAt(text.Count - 1);
            }

            if (text.Count > 0)
            {
                blocks.Add(new MarkdownText(text.ToList()));
            }

            text.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && line.StartsWith('#'))
            {
                int level = line.TakeWhile(c => c == '#').Count();
                if (level < line.Length && line[level] == ' ')
                {
                    FlushText();
                    blocks.Add(new MarkdownHeading(level, line[(level + 1)..].Trim()));
                    continue;
                }
            }
            if (!inFence && IsRow(line) && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                FlushText();
                IReadOnlyList<string> headers = Cells(line);
                IReadOnlyList<ColumnAlignment> alignments = Cells(lines[i + 1]).Select(Alignment).ToList();
                var rows = new List<IReadOnlyList<string>>();
                i += 2;
                for (; i < lines.Length && IsRow(lines[i]); i++)
                {
                    rows.Add(Cells(lines[i]));
                }

                i--;
                blocks.Add(new MarkdownTable(headers, alignments, rows));
                continue;
            }
            if (text.Count == 0 && string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            text.Add(line);
        }
        FlushText();
        return new MarkdownDocument(blocks);
    }

    /// <summary>The first table after the heading whose text starts with <paramref name="heading"/>.</summary>
    public MarkdownTable? TableAfter(string heading)
    {
        bool found = false;
        foreach (MarkdownBlock block in Blocks)
        {
            if (block is MarkdownHeading h)
            {
                if (found)
                {
                    return null;
                }

                found = h.Text.StartsWith(heading, StringComparison.OrdinalIgnoreCase);
            }
            else if (found && block is MarkdownTable table)
            {
                return table;
            }
        }
        return null;
    }

    private static bool IsRow(string line) => line.TrimStart().StartsWith('|');

    private static bool IsSeparator(string line) =>
        IsRow(line) && Cells(line).All(cell => cell.Length > 0 && cell.Trim(':').Length > 0 && cell.Trim(':').All(c => c == '-'));

    private static ColumnAlignment Alignment(string separator) =>
        separator.EndsWith(':') ? separator.StartsWith(':') ? ColumnAlignment.Center : ColumnAlignment.Right : ColumnAlignment.Left;

    /// <summary>The cells of a pipe-table row; <c>\|</c> is a pipe inside a cell.</summary>
    private static IReadOnlyList<string> Cells(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (trimmed[i] == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(trimmed[i]);
            }
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }
}

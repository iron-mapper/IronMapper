namespace Comparison.Cli.Artifacts;

/// <summary>
/// A RESULTS.md of a full run (results/RESULTS.md, results/&lt;run&gt;/RESULTS.md), written by generate-report.ps1. The
/// CLI only reads the rows "Run" and "Commit" of its Environment table and the Quick/Dry marker.
/// </summary>
internal sealed record ResultsRun(string Name, string Path, DateTime Modified, string? Run, string? Commit, string? Marker)
{
    public const string FileName = "RESULTS.md";

    /// <summary>results/RESULTS.md (the copy of the latest release run) and every results/*/RESULTS.md.</summary>
    public static IReadOnlyList<ResultsRun> List(string resultsDir)
    {
        if (!Directory.Exists(resultsDir))
        {
            return [];
        }

        var files = new List<(string Name, string Path)>();
        string top = System.IO.Path.Combine(resultsDir, FileName);
        if (File.Exists(top))
        {
            files.Add(("results", top));
        }

        foreach (string dir in Directory.GetDirectories(resultsDir).Order(StringComparer.Ordinal))
        {
            string file = System.IO.Path.Combine(dir, FileName);
            if (File.Exists(file))
            {
                files.Add(($"results/{System.IO.Path.GetFileName(dir)}", file));
            }
        }
        return files.Select(f => Load(f.Name, f.Path)).ToList();
    }

    public static ResultsRun Load(string name, string path)
    {
        string? run = null, commit = null, marker = null;
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("| Run |", StringComparison.Ordinal))
            {
                run = Cell(line);
            }
            else if (line.StartsWith("| Commit |", StringComparison.Ordinal))
            {
                commit = Cell(line).Trim('`').Replace("`", "");
            }
            else if (line.Contains("**Dry run**", StringComparison.Ordinal))
            {
                marker = "Dry run";
            }
            else if (line.Contains("**Quick run**", StringComparison.Ordinal))
            {
                marker = "Quick run";
            }

            if (line.StartsWith("## Summary", StringComparison.Ordinal))
            {
                break;
            }
        }
        return new ResultsRun(name, path, File.GetLastWriteTime(path), run, commit, marker);
    }

    private static string Cell(string line)
    {
        string[] parts = line.Split('|');
        return parts.Length > 2 ? parts[2].Trim() : "";
    }
}

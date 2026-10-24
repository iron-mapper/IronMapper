using Comparison.Cli.Artifacts;

namespace Comparison.Cli.Commands;

/// <summary>Finds the run folder a user names: a path, a quick-run stamp, or a folder below benchmarks/ or the repository.</summary>
internal static class RunFolders
{
    public static string? Resolve(string name, BenchPaths paths)
    {
        string[] candidates =
        [
            name,
            Path.Combine(paths.QuickArtifactsDir, name),
            Path.Combine(paths.BenchmarksDir, name),
            Path.Combine(paths.RepoDir, name),
        ];
        return candidates.Where(Directory.Exists).Select(Path.GetFullPath).FirstOrDefault();
    }

    public static bool IsQuickRun(string dir) =>
        File.Exists(Path.Combine(dir, "run-info.json")) ||
        QuickRun.Variants.Any(v => Directory.GetDirectories(dir, v + "-*").Length > 0);

    public static bool IsResultsRun(string dir) => File.Exists(Path.Combine(dir, ResultsRun.FileName));
}

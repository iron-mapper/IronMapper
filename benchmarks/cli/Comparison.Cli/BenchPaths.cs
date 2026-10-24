namespace Comparison.Cli;

/// <summary>
/// Folders of the suite. The benchmarks folder is the one with IronMapper.Comparison.slnx, found from the CLI's own
/// location (benchmarks/cli/Comparison.Cli/bin/...), then from the current directory.
/// </summary>
internal sealed class BenchPaths
{
    public const string SolutionFile = "IronMapper.Comparison.slnx";

    private BenchPaths(string benchmarksDir)
    {
        BenchmarksDir = benchmarksDir;
        RepoDir = Path.GetDirectoryName(benchmarksDir) ?? benchmarksDir;
    }

    public string BenchmarksDir { get; }

    public string RepoDir { get; }

    public string ScriptsDir => Path.Combine(BenchmarksDir, "scripts");

    public string RunQuickScript => Path.Combine(ScriptsDir, "run-quick.ps1");

    public string RunAllScript => Path.Combine(ScriptsDir, "run-all.ps1");

    /// <summary>run-quick.ps1 writes to &lt;repo&gt;/artifacts/quick/&lt;yyyyMMdd-HHmmss&gt;.</summary>
    public string QuickArtifactsDir => Path.Combine(RepoDir, "artifacts", "quick");

    public string ResultsDir => Path.Combine(BenchmarksDir, "results");

    public string BenchmarkSourcesDir => Path.Combine(BenchmarksDir, "src", "Comparison.Benchmarks");

    public static BenchPaths Locate()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
                    return new BenchPaths(dir.FullName);
                string nested = Path.Combine(dir.FullName, "benchmarks");
                if (File.Exists(Path.Combine(nested, SolutionFile)))
                    return new BenchPaths(nested);
            }
        }
        throw new InvalidOperationException($"Cannot find the benchmarks folder ({SolutionFile}) above {AppContext.BaseDirectory} or the current directory.");
    }

    public static BenchPaths ForTests(string benchmarksDir) => new(benchmarksDir);

    /// <summary>The folders of this process (set once at startup).</summary>
    public static BenchPaths Current
    {
        get => _current ?? throw new InvalidOperationException("BenchPaths.Current is not set.");
        set => _current = value;
    }

    private static BenchPaths? _current;

    /// <summary>A path relative to the benchmarks folder with '/' (the working directory of the scripts).</summary>
    public string RelativeToBenchmarks(string path) =>
        Path.GetRelativePath(BenchmarksDir, Path.GetFullPath(path)).Replace('\\', '/');

    /// <summary>A path relative to the current directory with '/', for messages.</summary>
    public static string Display(string path)
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path));
        return relative.StartsWith("..", StringComparison.Ordinal) && relative.Count(c => c is '/' or '\\') > 3
            ? Path.GetFullPath(path)
            : relative.Replace('\\', '/');
    }
}

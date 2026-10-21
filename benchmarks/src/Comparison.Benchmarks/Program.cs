using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace Comparison.Benchmarks;

public static class Program
{
    /// <summary>
    /// Runs all benchmarks or a filtered subset, e.g.
    /// <c>dotnet run -c Release -f net10.0 -- --filter *Collection*</c> or <c>-- --filter * --job short</c>.
    /// </summary>
    public static void Main(string[] args)
    {
        // DefaultConfig already exports Markdown (GitHub), CSV and HTML; JSON is added here.
        var config = DefaultConfig.Instance.AddExporter(JsonExporter.Full);

        // results/raw/bdn holds the .NET 10 results that generate-report.ps1 reads; other runtimes get their own
        // folder (bdn-net8.0) so they never overwrite them. An explicit --artifacts (e.g. the CI smoke run) wins.
        if (!args.Contains("--artifacts"))
        {
            var folder = Environment.Version.Major == 10 ? "bdn" : $"bdn-net{Environment.Version.Major}.0";
            // IRONMAPPER_RESULTS_DIR (absolute, or relative to benchmarks/; set by run-all.ps1 -ResultsDir) moves the whole
            // run to another folder, e.g. results/1.2.0, like the scripts (BenchmarkCommon.psm1, Get-ResultsDir).
            var results = Environment.GetEnvironmentVariable("IRONMAPPER_RESULTS_DIR");
            var resultsDir = string.IsNullOrWhiteSpace(results)
                ? Path.Combine(FindBenchmarksDirectory(), "results")
                : Path.Combine(FindBenchmarksDirectory(), results);
            config = config.WithArtifactsPath(Path.Combine(resultsDir, "raw", folder));
        }

        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToList();

        // Non-zero exit code when a benchmark threw or a filter matched nothing, so scripts and CI notice it
        // (--list and --help produce no summaries either and keep exit code 0).
        var filterMatchedNothing = summaries.Count == 0 && args.Contains("--filter");
        if (filterMatchedNothing || summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)))
            Environment.ExitCode = 1;
    }

    private static string FindBenchmarksDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronMapper.Comparison.slnx")))
                return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace Comparison.Benchmarks;

public static class Program
{
    /// <summary>
    /// Runs all benchmarks or a filtered subset, e.g.
    /// <c>dotnet run -c Release -- --filter *Collection*</c> or <c>-- --filter * --job short</c>.
    /// </summary>
    public static void Main(string[] args)
    {
        // DefaultConfig already exports Markdown (GitHub), CSV and HTML; JSON is added here.
        var config = DefaultConfig.Instance
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(Path.Combine(FindBenchmarksDirectory(), "results", "raw", "bdn"));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
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

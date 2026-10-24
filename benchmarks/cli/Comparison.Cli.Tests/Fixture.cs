namespace Comparison.Cli.Tests;

/// <summary>The recorded fixtures (Fixtures/README.md) and the real benchmarks folder of this checkout.</summary>
internal static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static string[] Lines(string name) => File.ReadAllLines(PathOf(name));

    public static string BenchmarksDir
    {
        get
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "IronMapper.Comparison.slnx")))
                    return dir.FullName;
            }
            throw new InvalidOperationException("benchmarks/ not found above the test output folder.");
        }
    }

    /// <summary>A fresh empty folder, deleted on dispose.</summary>
    public static TempDir NewTempDir() => new();

    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bench-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

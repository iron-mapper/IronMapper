namespace Comparison.Cli.Tests;

/// <summary>The recorded fixtures (Fixtures/README.md) and the real benchmarks folder of this checkout.</summary>
internal static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>
    /// The file as checked out: its line endings depend on the platform (.gitattributes: *.md CRLF everywhere, the
    /// other fixtures LF on Linux and macOS, CRLF on Windows). A test of a parser over whole text uses
    /// <see cref="Text(string, string)"/> with both endings.
    /// </summary>
    public static string Text(string name) => File.ReadAllText(PathOf(name));

    /// <summary>The file with every line ending replaced by <paramref name="newline"/>, the same on every platform.</summary>
    public static string Text(string name, string newline) => Text(name).ReplaceLineEndings(newline);

    /// <summary>The lines, without their endings (CRLF or LF, whichever the checkout has).</summary>
    public static string[] Lines(string name) => File.ReadAllLines(PathOf(name));

    /// <summary>
    /// The file name of a path recorded in a fixture. The fixtures were recorded on Windows, so their paths use '\';
    /// Path.GetFileName on Linux and macOS splits only at '/' and would return the whole path.
    /// </summary>
    public static string FileName(string recordedPath) => recordedPath[(recordedPath.LastIndexOfAny(['\\', '/']) + 1)..];

    public static string BenchmarksDir
    {
        get
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "IronMapper.Comparison.slnx")))
                {
                    return dir.FullName;
                }
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

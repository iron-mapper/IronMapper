using System.Text;
using Comparison.Cli.Parsing;

namespace Comparison.Cli.Tests;

public class LogTailTests
{
    private static readonly DateTime Long = DateTime.UtcNow.AddDays(-1);

    [Fact]
    public void MissingFile_GivesNothing()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        var tail = new LogTail(Path.Combine(dir.Path, "run.log"), Long);
        Assert.Empty(tail.Poll());
        Assert.False(tail.Exists);
    }

    [Fact]
    public void GrowingFile_GivesCompleteLinesOnly()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string path = Path.Combine(dir.Path, "run.log");
        var tail = new LogTail(path, Long);

        // The writer keeps the file open, as pwsh does with *> $log.
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        Write(writer, "// ***** BenchmarkRunner: Start   *****\r\n// ***** Found 6 bench");
        Assert.Equal(["// ***** BenchmarkRunner: Start   *****"], tail.Poll());
        Write(writer, "mark(s) in total *****\r\n");
        Assert.Equal(["// ***** Found 6 benchmark(s) in total *****"], tail.Poll());
        Assert.Empty(tail.Poll());
        Assert.NotNull(tail.LastGrowthUtc);
    }

    [Fact]
    public void MultiByteCharacters_SplitBetweenWrites()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string path = Path.Combine(dir.Path, "run.log");
        string line = Fixture.Lines("bdn-flat-dry.run.log").First(l => l.StartsWith("Setup power plan", StringComparison.Ordinal));
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        int cut = Array.IndexOf(bytes, (byte)0xD0) + 1; // inside the first Cyrillic letter

        var tail = new LogTail(path, Long);
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(bytes, 0, cut);
        writer.Flush();
        Assert.Empty(tail.Poll());
        writer.Write(bytes, cut, bytes.Length - cut);
        writer.Flush();
        Assert.Equal([line], tail.Poll());
        Assert.Contains("Высокая производительность", line);
    }

    [Fact]
    public void LogOfAnEarlierRun_IsIgnoredUntilRewritten()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string path = Path.Combine(dir.Path, "bdn-runtime.log");
        File.WriteAllText(path, "// ***** Found 99 benchmark(s) in total *****\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3));

        var tail = new LogTail(path, DateTime.UtcNow.AddMinutes(-1));
        Assert.Empty(tail.Poll());
        Assert.True(tail.Exists);

        // The new run truncates and writes it (pwsh *> $log).
        File.WriteAllText(path, "// ***** Found 6 benchmark(s) in total *****\n");
        Assert.Equal(["// ***** Found 6 benchmark(s) in total *****"], tail.Poll());
    }

    [Fact]
    public void ShorterFile_IsReadFromTheStart()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string path = Path.Combine(dir.Path, "run.log");
        File.WriteAllText(path, "first line of a long log\nsecond line of a long log\n");
        var tail = new LogTail(path, Long);
        Assert.Equal(2, tail.Poll().Count);

        File.WriteAllText(path, "new\n");
        Assert.Equal(["new"], tail.Poll());
    }

    [Fact]
    public void RealLog_ReadInChunks_GivesTheSameLines()
    {
        using Fixture.TempDir dir = Fixture.NewTempDir();
        string path = Path.Combine(dir.Path, "run.log");
        byte[] source = File.ReadAllBytes(Fixture.PathOf("bdn-two-classes-dry.run.log"));
        var tail = new LogTail(path, Long);
        var lines = new List<string>();
        using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            for (int offset = 0; offset < source.Length; offset += 3001)
            {
                writer.Write(source, offset, Math.Min(3001, source.Length - offset));
                writer.Flush();
                lines.AddRange(tail.Poll());
            }
        }
        Assert.Equal(Fixture.Lines("bdn-two-classes-dry.run.log"), lines);
    }

    private static void Write(FileStream stream, string text)
    {
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.Flush();
    }
}

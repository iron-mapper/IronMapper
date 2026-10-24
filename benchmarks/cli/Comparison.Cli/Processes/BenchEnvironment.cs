using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Comparison.Cli.Processes;

/// <summary>The git state of the checkout (for display only; the scripts record and check it themselves).</summary>
internal sealed record GitState(string? Branch, string? Head, bool Uncommitted)
{
    public bool TagExists(string repoDir, string tag) =>
        BenchEnvironment.Run("git", ["-C", repoDir, "rev-parse", "-q", "--verify", $"refs/tags/{tag}^{{commit}}"]) is { Length: > 0 };
}

/// <summary>What the menus show before a run: git state, available .NET runtimes, the suite's target frameworks.</summary>
internal static partial class BenchEnvironment
{
    public static GitState ReadGit(string repoDir) => new(
        Run("git", ["-C", repoDir, "rev-parse", "--abbrev-ref", "HEAD"]),
        Run("git", ["-C", repoDir, "rev-parse", "--short", "HEAD"]),
        Run("git", ["-C", repoDir, "status", "--porcelain"]) is { Length: > 0 });

    /// <summary>Major versions of the installed Microsoft.NETCore.App runtimes.</summary>
    public static IReadOnlySet<int> RuntimeMajors()
    {
        string output = Run("dotnet", ["--list-runtimes"]) ?? "";
        return RuntimeLine().Matches(output).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
    }

    /// <summary>The target frameworks of the suite (ComparisonTargetFrameworks in benchmarks/Directory.Build.props).</summary>
    public static IReadOnlyList<string> SuiteFrameworks(string benchmarksDir)
    {
        string props = Path.Combine(benchmarksDir, "Directory.Build.props");
        if (File.Exists(props) && TargetFrameworks().Match(File.ReadAllText(props)) is { Success: true } m)
            return m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ["net10.0", "net8.0"];
    }

    /// <summary>The major version of a TFM such as net8.0, or null.</summary>
    public static int? Major(string framework) =>
        FrameworkName().Match(framework) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    internal static string? Run(string tool, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(Tools.FindOnPath(tool) ?? tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in arguments)
                info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^Microsoft\.NETCore\.App (\d+)\.", RegexOptions.Multiline)]
    private static partial Regex RuntimeLine();

    [GeneratedRegex(@"<ComparisonTargetFrameworks>([^<]+)</ComparisonTargetFrameworks>")]
    private static partial Regex TargetFrameworks();

    [GeneratedRegex(@"^net(\d+)\.\d+$")]
    private static partial Regex FrameworkName();
}

using System.Diagnostics;

namespace Comparison.Cli.Processes;

/// <summary>Checks that pwsh 7+ and dotnet are on PATH before a script is started.</summary>
internal static class Tools
{
    /// <summary>The full path of an executable found on PATH (with PATHEXT on Windows), or null.</summary>
    public static string? FindOnPath(string name)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(dir.Trim('"'), name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>Null when pwsh 7+ and dotnet are available, otherwise a message saying what is missing.</summary>
    public static string? CheckPrerequisites()
    {
        var problems = new List<string>();
        if (FindOnPath("dotnet") is null)
        {
            problems.Add("dotnet was not found on PATH. Install the .NET 10 SDK: https://dotnet.microsoft.com/download");
        }

        string? pwsh = FindOnPath("pwsh");
        if (pwsh is null)
        {
            problems.Add("pwsh (PowerShell 7+) was not found on PATH. Install it: https://aka.ms/powershell " +
                "(Windows PowerShell 5.1, powershell.exe, cannot run the scripts).");
        }
        else
        {
            int? major = GetPwshMajorVersion(pwsh);
            if (major is null)
            {
                problems.Add($"Cannot determine the version of {pwsh}.");
            }
            else if (major < 7)
            {
                problems.Add($"{pwsh} is PowerShell {major}; the scripts need PowerShell 7 or later.");
            }
        }
        return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }

    private static int? GetPwshMajorVersion(string pwsh)
    {
        try
        {
            var info = new ProcessStartInfo(pwsh)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.Major" })
            {
                info.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return int.TryParse(output.Trim(), out int major) ? major : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

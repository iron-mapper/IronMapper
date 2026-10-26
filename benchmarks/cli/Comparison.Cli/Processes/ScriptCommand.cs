using System.Text;
using System.Text.RegularExpressions;

namespace Comparison.Cli.Processes;

/// <summary>
/// One script invocation: <c>pwsh -NoProfile -NonInteractive -File &lt;script&gt; &lt;arguments&gt;</c>, run in the
/// benchmarks folder. Only the parameters the user changed are in <see cref="Arguments"/>; the defaults stay in the script.
/// </summary>
internal sealed partial record ScriptCommand(string ScriptPath, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    public static readonly IReadOnlyList<string> PwshOptions = ["-NoProfile", "-NonInteractive", "-File"];

    /// <summary>Arguments of pwsh: the options, the script, then the script's parameters.</summary>
    public IEnumerable<string> PwshArguments => PwshOptions.Append(ScriptPath).Concat(Arguments);

    /// <summary>The same command as text to copy, with the script relative to the working directory.</summary>
    public string Display
    {
        get
        {
            string script = "./" + Path.GetRelativePath(WorkingDirectory, ScriptPath).Replace('\\', '/');
            var text = new StringBuilder("pwsh");
            foreach (string arg in PwshOptions.Append(script).Concat(Arguments))
                text.Append(' ').Append(Quote(arg));
            return text.ToString();
        }
    }

    /// <summary>
    /// Quotes an argument for pwsh and POSIX shells: plain words stay as they are, anything else (globs, commas, spaces)
    /// goes in single quotes, which mean the same in both. An apostrophe has no such form inside single quotes, so an
    /// argument with one goes in double quotes when nothing in it is special there; otherwise it is quoted for pwsh.
    /// </summary>
    public static string Quote(string arg)
    {
        if (PlainArgument().IsMatch(arg))
            return arg;
        if (!arg.Contains('\''))
            return "'" + arg + "'";
        return arg.IndexOfAny(['"', '$', '`', '\\', '!']) < 0 ? "\"" + arg + "\"" : "'" + arg.Replace("'", "''") + "'";
    }

    // No '\': unquoted, a POSIX shell takes it as an escape (..\artifacts\x would arrive as ..artifactsx).
    [GeneratedRegex(@"^[A-Za-z0-9_./:=+-]+$")]
    private static partial Regex PlainArgument();
}

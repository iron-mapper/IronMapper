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
    /// goes in single quotes.
    /// </summary>
    public static string Quote(string arg) =>
        PlainArgument().IsMatch(arg) ? arg : "'" + arg.Replace("'", "''") + "'";

    [GeneratedRegex(@"^[A-Za-z0-9_./:\\=+-]+$")]
    private static partial Regex PlainArgument();
}

using System.Text.RegularExpressions;

namespace Comparison.Cli.Parsing;

/// <summary>
/// The message of a script's terminating error (throw) from its stderr. pwsh prints it in ConciseView:
/// <code>
/// Exception: C:\...\run-quick.ps1:404
/// Line |
///  404 |  … nt -gt 0) { throw "Pre-flight failed:`n  " + ...
///      |                ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
///      | Pre-flight failed:   Branch: main (expected nope)
/// </code>
/// The message is the "|" lines after the "~~~" marker; without that layout, all non-empty stderr lines.
/// </summary>
internal static partial class ScriptError
{
    public static string Extract(IReadOnlyList<string> stderr)
    {
        int marker = -1;
        for (int i = stderr.Count - 1; i >= 0; i--)
        {
            if (Marker().IsMatch(stderr[i]))
            {
                marker = i;
                break;
            }
        }
        if (marker >= 0)
        {
            var message = stderr.Skip(marker + 1)
                .Select(line => MessageLine().Match(line))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value.Trim())
                .Where(text => text.Length > 0)
                .ToList();
            if (message.Count > 0)
                return string.Join(Environment.NewLine, message);
        }
        return string.Join(Environment.NewLine, stderr.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.TrimEnd()));
    }

    [GeneratedRegex(@"^\s*\|\s*~+\s*$")]
    private static partial Regex Marker();

    [GeneratedRegex(@"^\s*\|\s?(.*)$")]
    private static partial Regex MessageLine();
}

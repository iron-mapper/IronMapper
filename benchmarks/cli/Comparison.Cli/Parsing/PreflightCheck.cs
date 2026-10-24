using System.Text.RegularExpressions;

namespace Comparison.Cli.Parsing;

internal enum CheckMark
{
    Ok,
    Warn,
    Fail,
}

/// <summary>
/// One line of the pre-flight checks of run-quick.ps1 and run-all.ps1 (Write-Check in both):
/// <c>  [ok  ] Name: detail</c>, <c>[warn]</c> or <c>[FAIL]</c>.
/// </summary>
internal sealed partial record PreflightCheck(CheckMark Mark, string Name, string Detail)
{
    public static PreflightCheck? TryParse(string line)
    {
        Match m = Pattern().Match(line);
        if (!m.Success)
            return null;
        CheckMark mark = m.Groups["mark"].Value switch
        {
            "ok" => CheckMark.Ok,
            "warn" => CheckMark.Warn,
            _ => CheckMark.Fail,
        };
        return new PreflightCheck(mark, m.Groups["name"].Value.Trim(), m.Groups["detail"].Value.Trim());
    }

    [GeneratedRegex(@"^\s*\[(?<mark>ok|warn|FAIL)\s*\]\s+(?<name>[^:]+):\s?(?<detail>.*)$")]
    private static partial Regex Pattern();
}

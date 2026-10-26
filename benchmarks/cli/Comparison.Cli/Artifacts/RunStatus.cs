using System.Text.Json;

namespace Comparison.Cli.Artifacts;

internal enum RunState
{
    Ok,
    Failed,
    Invalid,
    NotRun,
    Unknown,
}

/// <summary>
/// Status of one run of run-quick.ps1 (one variant on one framework), by the rules of its Get-RunStatus: no
/// assemblies.json — not run; ExitCode other than 0 — FAILED; any Problems — INVALID; otherwise ok. Only these two
/// top-level fields are read: Host and ChildBuilds change with the script's checks (and older run folders have
/// another layout), so they are never looked at.
/// </summary>
internal sealed record RunStatus(RunState State, string Text)
{
    public const string AssembliesFile = "assemblies.json";

    public static RunStatus Read(string runDir)
    {
        string file = Path.Combine(runDir, AssembliesFile);
        if (!File.Exists(file))
        {
            return new RunStatus(RunState.NotRun, "not run");
        }

        try
        {
            return FromJson(File.ReadAllText(file), runDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RunStatus(RunState.Unknown, $"unknown ({AssembliesFile}: {ex.Message})");
        }
    }

    public static RunStatus FromJson(string json, string runDir)
    {
        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new RunStatus(RunState.Unknown, $"unknown ({AssembliesFile} is not valid JSON: {ex.Message})");
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new RunStatus(RunState.Unknown, $"unknown ({AssembliesFile} is not an object)");
        }

        if (!root.TryGetProperty("ExitCode", out JsonElement exitCode) || !exitCode.TryGetInt32(out int code))
        {
            return new RunStatus(RunState.Unknown, $"unknown (no ExitCode in {AssembliesFile})");
        }

        if (code != 0)
        {
            return new RunStatus(RunState.Failed, $"FAILED (exit code {code}, log: {Path.Combine(runDir, "run.log")})");
        }

        // ConvertTo-Json writes a one-element array as the element itself: accept an array, a string or nothing.
        var problems = new List<string>();
        if (root.TryGetProperty("Problems", out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                problems.AddRange(value.EnumerateArray().Select(ToText).Where(text => text.Length > 0));
            }
            else if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                problems.Add(ToText(value));
            }
        }
        problems.RemoveAll(string.IsNullOrEmpty);
        return problems.Count > 0
            ? new RunStatus(RunState.Invalid, "INVALID: " + string.Join("; ", problems))
            : new RunStatus(RunState.Ok, "ok");
    }

    /// <summary>The state of a status text the script printed (<c>[..] local on net8.0: &lt;status&gt;</c>).</summary>
    public static RunState Classify(string text) => text switch
    {
        "ok" => RunState.Ok,
        _ when text.StartsWith("FAILED", StringComparison.Ordinal) => RunState.Failed,
        _ when text.StartsWith("INVALID", StringComparison.Ordinal) => RunState.Invalid,
        _ when text.StartsWith("not run", StringComparison.Ordinal) => RunState.NotRun,
        _ => RunState.Unknown,
    };

    private static string ToText(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();
}

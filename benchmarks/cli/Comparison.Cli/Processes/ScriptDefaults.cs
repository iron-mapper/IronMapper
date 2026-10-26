using System.Diagnostics;
using System.Text.Json;

namespace Comparison.Cli.Processes;

/// <summary>
/// Default values of a script's param() block, read through the PowerShell parser (AST, SafeGetValue: constants
/// only, nothing is executed). The CLI shows them and preselects them, but never passes them: a parameter the user did
/// not change is left to the script. A default that is not a constant (or a failed read) is "script default".
/// </summary>
internal sealed class ScriptDefaults
{
    private const string Reader = """
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:BENCH_CLI_SCRIPT, [ref]$null, [ref]$null)
        $d = [ordered]@{}
        foreach ($p in $ast.ParamBlock.Parameters) {
            $v = $null
            if ($p.DefaultValue) { try { $v = $p.DefaultValue.SafeGetValue() } catch { $v = $null } }
            $d[$p.Name.VariablePath.UserPath] = $v
        }
        ConvertTo-Json -InputObject $d -Compress -Depth 3
        """;

    private readonly Dictionary<string, JsonElement> _values;

    private ScriptDefaults(Dictionary<string, JsonElement> values) => _values = values;

    public static ScriptDefaults Empty { get; } = new(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));

    public bool Known => _values.Count > 0;

    /// <summary>The defaults of <paramref name="scriptPath"/>, or <see cref="Empty"/> when they cannot be read.</summary>
    public static ScriptDefaults Read(string scriptPath)
    {
        try
        {
            var info = new ProcessStartInfo(Tools.FindOnPath("pwsh") ?? "pwsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = ScriptProcess.OutputEncoding,
            };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", Reader })
            {
                info.ArgumentList.Add(arg);
            }

            info.Environment["BENCH_CLI_SCRIPT"] = scriptPath;
            using Process process = Process.Start(info)!;
            string json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return Parse(json);
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    public static ScriptDefaults Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var values = document.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            return new ScriptDefaults(values);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>A string default, or null (unknown, not a constant, or not a string).</summary>
    public string? String(string name) =>
        _values.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A string[] default (a single string counts as one element), or null.</summary>
    public IReadOnlyList<string>? Strings(string name)
    {
        if (!_values.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList(),
            JsonValueKind.String => [value.GetString()!],
            _ => null,
        };
    }

    /// <summary>"2.0.0" or "script default", for prompts and help.</summary>
    public string Describe(string name)
    {
        IReadOnlyList<string>? values = Strings(name);
        return values is { Count: > 0 } ? string.Join(", ", values) : "script default";
    }
}

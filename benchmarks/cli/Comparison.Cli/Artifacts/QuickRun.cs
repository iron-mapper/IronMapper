using System.Globalization;
using System.Text.Json;

namespace Comparison.Cli.Artifacts;

/// <summary>One variant × framework of a quick run: the folder &lt;variant&gt;-&lt;framework&gt; and its status.</summary>
internal sealed record QuickVariantRun(string Variant, string Framework, string Dir, RunStatus Status);

/// <summary>
/// A run folder of run-quick.ps1 (artifacts/quick/&lt;yyyyMMdd-HHmmss&gt;): run-info.json, summary.md and one folder per
/// variant and framework. Read only; a missing or unreadable file leaves its fields empty.
/// </summary>
internal sealed record QuickRun
{
    /// <summary>The variants of run-quick.ps1, in the order it runs them ($variants).</summary>
    public static readonly IReadOnlyList<string> Variants = ["published", "local"];

    public const string StampFormat = "yyyyMMdd-HHmmss";

    public required string Dir { get; init; }

    public string Stamp => Path.GetFileName(Dir);

    /// <summary>Local time of the folder name (the script names it with Get-Date).</summary>
    public DateTime? Time => DateTime.TryParseExact(Stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t) ? t : null;

    public string? Branch { get; init; }

    public string? Commit { get; init; }

    public bool Uncommitted { get; init; }

    public string? PublishedVersion { get; init; }

    public IReadOnlyList<string> Filter { get; init; } = [];

    public IReadOnlyList<string> Frameworks { get; init; } = [];

    public bool Dry { get; init; }

    public string? Job { get; init; }

    /// <summary>Why run-info.json could not be read, or null.</summary>
    public string? InfoProblem { get; init; }

    public IReadOnlyList<QuickVariantRun> Runs { get; init; } = [];

    public string SummaryPath => Path.Combine(Dir, "summary.md");

    public bool HasSummary => File.Exists(SummaryPath);

    public string ShortCommit => Commit is { Length: > 7 } c ? c[..7] : Commit ?? "";

    /// <summary>The run folders below <paramref name="quickDir"/>, newest first.</summary>
    public static IReadOnlyList<QuickRun> List(string quickDir)
    {
        if (!Directory.Exists(quickDir))
            return [];
        return Directory.GetDirectories(quickDir)
            .Where(dir => DateTime.TryParseExact(Path.GetFileName(dir), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(dir => Path.GetFileName(dir), StringComparer.Ordinal)
            .Select(Load)
            .ToList();
    }

    public static QuickRun Load(string dir)
    {
        string infoPath = Path.Combine(dir, "run-info.json");
        string? problem = null;
        JsonElement info = default;
        if (!File.Exists(infoPath))
            problem = "no run-info.json";
        else
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(infoPath));
                info = document.RootElement.Clone();
                if (info.ValueKind != JsonValueKind.Object)
                    problem = "run-info.json is not an object";
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problem = $"run-info.json: {ex.Message}";
            }
        }

        bool ok = problem is null;
        IReadOnlyList<string> frameworks = ok ? Strings(info, "Frameworks") : [];
        if (frameworks.Count == 0)
            frameworks = DiscoverFrameworks(dir);

        var runs = new List<QuickVariantRun>();
        foreach (string framework in frameworks)
        {
            foreach (string variant in Variants)
            {
                string runDir = Path.Combine(dir, $"{variant}-{framework}");
                runs.Add(new QuickVariantRun(variant, framework, runDir, RunStatus.Read(runDir)));
            }
        }

        return new QuickRun
        {
            Dir = dir,
            Branch = ok ? String(info, "Branch") : null,
            Commit = ok ? String(info, "Commit") : null,
            Uncommitted = ok && Bool(info, "Uncommitted"),
            PublishedVersion = ok ? String(info, "PublishedVersion") : null,
            Filter = ok ? Strings(info, "Filter") : [],
            Frameworks = frameworks,
            Dry = ok && Bool(info, "Dry"),
            Job = ok ? String(info, "Job") : null,
            InfoProblem = problem,
            Runs = runs,
        };
    }

    /// <summary>Frameworks from the folder names &lt;variant&gt;-&lt;framework&gt; (when run-info.json is missing).</summary>
    private static IReadOnlyList<string> DiscoverFrameworks(string dir) =>
        Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .SelectMany(name => Variants.Where(v => name!.StartsWith(v + "-", StringComparison.Ordinal)).Select(v => name![(v.Length + 1)..]))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A string array, or a single string (ConvertTo-Json unwraps one-element arrays).</summary>
    private static IReadOnlyList<string> Strings(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement value))
            return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList(),
            JsonValueKind.String => [value.GetString()!],
            _ => [],
        };
    }
}

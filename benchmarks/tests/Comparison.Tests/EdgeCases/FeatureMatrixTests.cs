using System.Text;
using Comparison.Mappers;

namespace Comparison.Tests.EdgeCases;

/// <summary>Writes results/feature-matrix.md: how each scenario is configured + the observed edge-case facts.</summary>
public class FeatureMatrixTests
{
    private const string FileName = "feature-matrix.md";

    /// <summary>How each benchmarked scenario is expressed per library (taken from the code in Comparison.Mappers).</summary>
    private static readonly (string Scenario, Dictionary<string, string> Cells)[] Scenarios =
    [
        ("Flat (15 properties)", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "automatic (`CreateMap`; same code as `[MapTo]`)",
            [MapperNames.IronMapperIMapper] = "automatic",
            [MapperNames.Mapperly] = "automatic",
            [MapperNames.Mapster] = "automatic",
            [MapperNames.AutoMapper] = "automatic",
        }),
        ("Nested single objects", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "**manual setup**: `ForMember(d => d.X, o => o.MapFrom(s => s.X.MapToXDto()))` per nested property",
            [MapperNames.IronMapperIMapper] = "same profile as IronMapper",
            [MapperNames.Mapperly] = "automatic",
            [MapperNames.Mapster] = "automatic",
            [MapperNames.AutoMapper] = "automatic (`CreateMap` for every nested type pair)",
        }),
        ("`List<TEntity>` → `List<TDto>` (nested and top level)", new()
        {
            [MapperNames.Manual] = "`new List<T>(count)` + loop",
            [MapperNames.IronMapper] = "automatic; `list.MapToOrderDtoList()`; generated as `Enumerable.ToList(Enumerable.Select(...))`",
            [MapperNames.IronMapperIMapper] = "`MapCollection<TSource, TDest>` returns a lazy `IEnumerable` (materialized with `ToList()`); `Map<List<T>>` not supported (issue #8)",
            [MapperNames.Mapperly] = "automatic; `new List<T>(count)` + loop",
            [MapperNames.Mapster] = "automatic",
            [MapperNames.AutoMapper] = "automatic",
        }),
        ("Rename", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "`ForMember(...MapFrom(s => s.Email))`",
            [MapperNames.IronMapperIMapper] = "same profile as IronMapper",
            [MapperNames.Mapperly] = "`[MapProperty]`",
            [MapperNames.Mapster] = "`Map(d => ..., s => ...)`",
            [MapperNames.AutoMapper] = "`ForMember(...MapFrom(...))`",
        }),
        ("Ignore", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "`ForMember(...Ignore())`",
            [MapperNames.IronMapperIMapper] = "same profile as IronMapper",
            [MapperNames.Mapperly] = "`[MapperIgnoreTarget]` + `[MapperIgnoreSource]`",
            [MapperNames.Mapster] = "`Ignore(...)`",
            [MapperNames.AutoMapper] = "`ForMember(...Ignore())`",
        }),
        ("Converter (`decimal` → `string`)", new()
        {
            [MapperNames.Manual] = "static method call",
            [MapperNames.IronMapper] = "`UseConverter<T>()` (`ITypeConverter`); generated code creates `new T()` on every call",
            [MapperNames.IronMapperIMapper] = "same profile as IronMapper",
            [MapperNames.Mapperly] = "`[MapProperty(Use = ...)]` + `[UserMapping(Default = false)]` method",
            [MapperNames.Mapster] = "`Map(d => ..., s => Format(s.Salary))` (no separate converter type for a member)",
            [MapperNames.AutoMapper] = "`ConvertUsing(IValueConverter, s => s.Salary)` (one converter instance)",
        }),
        ("Computed property", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "`ForMember(...MapFrom(s => s.FirstName + \" \" + s.LastName))`",
            [MapperNames.IronMapperIMapper] = "same profile as IronMapper",
            [MapperNames.Mapperly] = "`[MapPropertyFromSource(Use = ...)]`",
            [MapperNames.Mapster] = "`Map(d => ..., s => ...)`",
            [MapperNames.AutoMapper] = "`ForMember(...MapFrom(...))`",
        }),
        ("In-place (existing destination)", new()
        {
            [MapperNames.Manual] = "hand-written",
            [MapperNames.IronMapper] = "`src.MapToUserDto(dest)`",
            [MapperNames.IronMapperIMapper] = "`mapper.Map<TSource, TDest>(src, dest)`",
            [MapperNames.Mapperly] = "`[MappingTarget]` parameter",
            [MapperNames.Mapster] = "`src.Adapt(dest, config)`",
            [MapperNames.AutoMapper] = "`mapper.Map(src, dest)`",
        }),
    ];

    private static readonly string[] Notes =
    [
        "Nullable annotations: the benchmark models declare nested objects and collections non-nullable (`= null!`), because the timed data never contains `null`. " +
        "Mapperly generates code from the nullable annotations: for a non-nullable member it emits no `null` check (a `null` there fails with `NullReferenceException`), " +
        "for a nullable member it does. The `null` rows are therefore shown for both declarations; the nullable variant uses `NullableOrderEntity`/`NullableOrderDto` " +
        "and `UserEntity?` → `UserDto?`. The other libraries do not read annotations; their results differ between the variants only where the configuration differs.",
        "IronMapper, nullable nested object: the documented workaround `MapFrom(s => s.Customer!.MapToCustomerDto())` has no `null` check; " +
        "a user who needs `null` → `null` has to write the check in the lambda (`s.Customer == null ? null : ...`).",
        "IronMapper_IMapper: a `null` nested object surfaces as `TargetInvocationException` (the generated method's `ArgumentNullException` wrapped by `MethodInfo.Invoke`).",
        "`List<string>` → `List<string>`: IronMapper and Mapperly (by default, without `UseDeepCloning`) assign the same list; Mapster and AutoMapper create a new list. " +
        "Collections with the same element type are kept out of the timed scenarios for this reason (BENCHMARK_SPEC.md, 4).",
        "IronMapper, flattening: in `MappingProfile` mappings unmatched destination properties are skipped silently (IM0001 is reported for `[MapTo]`/`[MapFrom]` only, docs/diagnostics.md). " +
        "This comparison configures IronMapper only through a profile, so the build gives no hint.",
        "IronMapper_IMapper, collections: `MapCollection<TSource, TDest>` returns a lazy `IEnumerable<TDest>`; the benchmark materializes it with `ToList()` " +
        "(no known capacity, so the list grows while filling). `Map<List<TDto>>(list)` throws `MappingException` (issue #8).",
        "IronMapper, positional record: the mapping is not declared in the code, because it breaks the build (checked with IronMapper 1.1.3: CS7036 in the generated file, no IronMapper diagnostic).",
    ];

    [Fact]
    public void Write_feature_matrix()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Feature matrix");
        sb.AppendLine();
        sb.AppendLine("> Generated by `tests/Comparison.Tests` (`FeatureMatrixTests`). Do not edit by hand.");
        sb.AppendLine();
        sb.AppendLine("## Benchmarked scenarios: how they are configured");
        sb.AppendLine();
        sb.AppendLine("All participants produce results equivalent to `Manual` in these scenarios (gate tests in `EquivalenceTests` and `DeepCopyTests`).");
        sb.AppendLine();
        AppendHeader(sb, "Scenario");
        foreach (var (scenario, cells) in Scenarios)
            AppendRow(sb, scenario, Mappers.Names.Select(n => cells[n]));

        sb.AppendLine();
        sb.AppendLine("## Edge cases: observed behavior");
        sb.AppendLine();
        sb.AppendLine("Default behavior of each library, without extra configuration for the case. " +
                      "Not part of the timed benchmarks (BENCHMARK_SPEC.md, 4 and 6.4). `Manual` shows one possible reference behavior.");
        sb.AppendLine();
        AppendHeader(sb, "Case");
        foreach (var edgeCase in EdgeCaseFacts.Cases)
            AppendRow(sb, edgeCase.Title, Mappers.Names.Select(edgeCase.Evaluate));

        sb.AppendLine();
        sb.AppendLine("Notes:");
        sb.AppendLine();
        foreach (var note in Notes)
            sb.AppendLine($"- {note}");

        // The committed matrix comes from the reference runtime (.NET 10); other runtimes write theirs to raw/.
        var path = Environment.Version.Major == 10
            ? Path.Combine(FindBenchmarksDirectory(), "results", FileName)
            : Path.Combine(FindBenchmarksDirectory(), "results", "raw", $"feature-matrix.net{Environment.Version.Major}.0.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // LF only and no BOM, like the script-generated reports (benchmarks/.gitattributes: results/** eol=lf).
        File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n").Replace("\r", ""), new UTF8Encoding(false));

        Assert.True(File.Exists(path));
    }

    private static void AppendHeader(StringBuilder sb, string first)
    {
        sb.AppendLine($"| {first} | {string.Join(" | ", Mappers.Names)} |");
        sb.AppendLine($"|---|{string.Concat(Mappers.Names.Select(_ => "---|"))}");
    }

    private static void AppendRow(StringBuilder sb, string first, IEnumerable<string> cells) =>
        sb.AppendLine($"| {first} | {string.Join(" | ", cells.Select(c => c.Replace("|", "\\|")))} |");

    private static string FindBenchmarksDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronMapper.Comparison.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("IronMapper.Comparison.slnx not found above " + AppContext.BaseDirectory);
    }
}

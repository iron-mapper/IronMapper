using Comparison.Mappers;
using Comparison.Mappers.EdgeCases;

namespace Comparison.Tests;

/// <summary>One instance of every participant for the whole test run (setup cost is paid once).</summary>
internal static class Mappers
{
    private static readonly Lazy<Dictionary<string, IComparisonMapper>> All =
        new(() => ComparisonMappers.CreateAll().ToDictionary(m => m.Name));

    private static readonly Lazy<Dictionary<string, IEdgeCaseMapper>> EdgeCases =
        new(() => EdgeCaseMappers.CreateAll().ToDictionary(m => m.Name));

    public static IComparisonMapper Manual => Get(MapperNames.Manual);

    public static IComparisonMapper Get(string name) => All.Value[name];

    public static IEdgeCaseMapper GetEdgeCase(string name) => EdgeCases.Value[name];

    /// <summary>Report order of the participants.</summary>
    public static readonly string[] Names =
    [
        MapperNames.Manual,
        MapperNames.IronMapper,
        MapperNames.IronMapperIMapper,
        MapperNames.Mapperly,
        MapperNames.Mapster,
        MapperNames.AutoMapper,
    ];

    /// <summary>Every library except the Manual baseline.</summary>
    public static TheoryData<string> Libraries => new(Names.Where(n => n != MapperNames.Manual));

    public static TheoryData<string> AllNames => new(Names);
}

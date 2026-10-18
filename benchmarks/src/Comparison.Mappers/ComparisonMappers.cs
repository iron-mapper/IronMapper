using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapperly;
using Comparison.Mappers.Mapster;

namespace Comparison.Mappers;

public static class ComparisonMappers
{
    /// <summary>Every participant, Manual first.</summary>
    public static IReadOnlyList<IComparisonMapper> CreateAll() =>
    [
        new ManualComparisonMapper(),
        new IronMapperComparisonMapper(),
        new IronMapperDiComparisonMapper(),
        new MapperlyComparisonMapper(),
        new MapsterComparisonMapper(),
        new AutoMapperComparisonMapper(),
    ];
}

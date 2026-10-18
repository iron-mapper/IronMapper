using Comparison.Models.EdgeCases;
using Comparison.Models.Nested;
using Riok.Mapperly.Abstractions;

namespace Comparison.Mappers.EdgeCases;

[Mapper]
public static partial class MapperlyEdgeCaseMappings
{
    public static partial TagsDto MapTags(TagsEntity src);
    public static partial PointRecordDto MapPoint(PointEntity src);

    [MapperIgnoreSource(nameof(OrderEntity.CreatedAt))]
    [MapperIgnoreSource(nameof(OrderEntity.Total))]
    [MapperIgnoreSource(nameof(OrderEntity.Lines))]
    public static partial OrderFlatDto MapFlattening(OrderEntity src);
}

public sealed class MapperlyEdgeCaseMapper : IEdgeCaseMapper
{
    public string Name => MapperNames.Mapperly;
    public TagsDto MapTags(TagsEntity src) => MapperlyEdgeCaseMappings.MapTags(src);
    public PointRecordDto MapPoint(PointEntity src) => MapperlyEdgeCaseMappings.MapPoint(src);
    public OrderFlatDto MapFlattening(OrderEntity src) => MapperlyEdgeCaseMappings.MapFlattening(src);
}

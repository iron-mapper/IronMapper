using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers.Mapperly;

public sealed class MapperlyComparisonMapper : IComparisonMapper
{
    public string Name => MapperNames.Mapperly;
    public UserDto MapFlat(UserEntity src) => ComparisonMapperlyMapper.MapFlat(src);
    public OrderDto MapNested(OrderEntity src) => ComparisonMapperlyMapper.MapNested(src);
    public List<OrderDto> MapCollection(List<OrderEntity> src) => ComparisonMapperlyMapper.MapCollection(src);
    public EmployeeDto MapCustom(EmployeeEntity src) => ComparisonMapperlyMapper.MapCustom(src);
    public void MapInPlace(UserEntity src, UserDto dest) => ComparisonMapperlyMapper.MapInPlace(src, dest);
}

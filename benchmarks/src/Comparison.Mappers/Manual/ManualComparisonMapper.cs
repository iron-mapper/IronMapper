using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers.Manual;

public sealed class ManualComparisonMapper : IComparisonMapper
{
    public string Name => MapperNames.Manual;
    public UserDto MapFlat(UserEntity src) => ManualMappings.MapFlat(src);
    public OrderDto MapNested(OrderEntity src) => ManualMappings.MapNested(src);
    public List<OrderDto> MapCollection(List<OrderEntity> src) => ManualMappings.MapCollection(src);
    public EmployeeDto MapCustom(EmployeeEntity src) => ManualMappings.MapCustom(src);
    public void MapInPlace(UserEntity src, UserDto dest) => ManualMappings.MapInPlace(src, dest);
}

using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using Mapster;

namespace Comparison.Mappers.Mapster;

public sealed class MapsterComparisonMapper : IComparisonMapper
{
    private readonly TypeAdapterConfig _config = MapsterSetup.CreateConfig();

    public string Name => MapperNames.Mapster;
    public UserDto MapFlat(UserEntity src) => src.Adapt<UserEntity, UserDto>(_config);
    public OrderDto MapNested(OrderEntity src) => src.Adapt<OrderEntity, OrderDto>(_config);
    public List<OrderDto> MapCollection(List<OrderEntity> src) => src.Adapt<List<OrderEntity>, List<OrderDto>>(_config);
    public EmployeeDto MapCustom(EmployeeEntity src) => src.Adapt<EmployeeEntity, EmployeeDto>(_config);
    public void MapInPlace(UserEntity src, UserDto dest) => src.Adapt(dest, _config);
}

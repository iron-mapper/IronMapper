using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using IronMapper.Generated;

namespace Comparison.Mappers.IronMapper;

/// <summary>Generated extension methods (<c>source.MapToXxx()</c>).</summary>
public sealed class IronMapperComparisonMapper : IComparisonMapper
{
    public string Name => MapperNames.IronMapper;
    public UserDto MapFlat(UserEntity src) => src.MapToUserDto();
    public OrderDto MapNested(OrderEntity src) => src.MapToOrderDto();
    public List<OrderDto> MapCollection(List<OrderEntity> src) => src.MapToOrderDtoList();
    public EmployeeDto MapCustom(EmployeeEntity src) => src.MapToEmployeeDto();
    public void MapInPlace(UserEntity src, UserDto dest) => src.MapToUserDto(dest);
}

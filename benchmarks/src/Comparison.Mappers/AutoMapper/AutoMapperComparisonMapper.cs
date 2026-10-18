using AutoMapper;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers.AutoMapper;

public sealed class AutoMapperComparisonMapper : IComparisonMapper
{
    private readonly IMapper _mapper = AutoMapperSetup.CreateMapper();

    public string Name => MapperNames.AutoMapper;
    public UserDto MapFlat(UserEntity src) => _mapper.Map<UserEntity, UserDto>(src);
    public OrderDto MapNested(OrderEntity src) => _mapper.Map<OrderEntity, OrderDto>(src);
    public List<OrderDto> MapCollection(List<OrderEntity> src) => _mapper.Map<List<OrderEntity>, List<OrderDto>>(src);
    public EmployeeDto MapCustom(EmployeeEntity src) => _mapper.Map<EmployeeEntity, EmployeeDto>(src);
    public void MapInPlace(UserEntity src, UserDto dest) => _mapper.Map(src, dest);
}

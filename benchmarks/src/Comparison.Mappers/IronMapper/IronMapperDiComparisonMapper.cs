using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Mappers.IronMapper;

/// <summary>The DI path: <see cref="IIronMapper"/> resolved once (reflection + MethodInfo.Invoke inside).</summary>
public sealed class IronMapperDiComparisonMapper : IComparisonMapper
{
    private readonly IIronMapper _mapper = IronMapperDiSetup.CreateMapper();

    public string Name => MapperNames.IronMapperIMapper;

    public UserDto MapFlat(UserEntity src) => _mapper.Map<UserEntity, UserDto>(src);
    public OrderDto MapNested(OrderEntity src) => _mapper.Map<OrderEntity, OrderDto>(src);

    // Map<List<T>> is not supported (issue #8); MapCollection is lazy, so it is materialized here.
    public List<OrderDto> MapCollection(List<OrderEntity> src) => _mapper.MapCollection<OrderEntity, OrderDto>(src).ToList();

    public EmployeeDto MapCustom(EmployeeEntity src) => _mapper.Map<EmployeeEntity, EmployeeDto>(src);
    public void MapInPlace(UserEntity src, UserDto dest) => _mapper.Map(src, dest);
}

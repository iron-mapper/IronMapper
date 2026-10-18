using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using IronMapper.Extensions.DI;
using Microsoft.Extensions.DependencyInjection;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Mappers.IronMapper;

/// <summary>The DI path: <see cref="IIronMapper"/> resolved once (reflection + MethodInfo.Invoke inside).</summary>
public sealed class IronMapperDiComparisonMapper : IComparisonMapper
{
    private readonly IIronMapper _mapper = CreateMapper();

    public string Name => MapperNames.IronMapperIMapper;

    public static IIronMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(o => o.AddProfile<ComparisonProfile>());
        return services.BuildServiceProvider().GetRequiredService<IIronMapper>();
    }

    public UserDto MapFlat(UserEntity src) => _mapper.Map<UserEntity, UserDto>(src);
    public OrderDto MapNested(OrderEntity src) => _mapper.Map<OrderEntity, OrderDto>(src);

    // Map<List<T>> is not supported (issue #8); MapCollection is lazy, so it is materialized here.
    public List<OrderDto> MapCollection(List<OrderEntity> src) => _mapper.MapCollection<OrderEntity, OrderDto>(src).ToList();

    public EmployeeDto MapCustom(EmployeeEntity src) => _mapper.Map<EmployeeEntity, EmployeeDto>(src);
    public void MapInPlace(UserEntity src, UserDto dest) => _mapper.Map(src, dest);
}

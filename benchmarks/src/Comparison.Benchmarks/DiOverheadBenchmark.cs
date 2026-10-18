using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using IronMapper.Generated;
using IAutoMapper = AutoMapper.IMapper;
using IIronMapper = IronMapper.Interfaces.IMapper;
using IMapsterMapper = MapsterMapper.IMapper;

namespace Comparison.Benchmarks;

/// <summary>
/// The cost of going through an <c>IMapper</c> resolved from DI instead of calling the mapping directly:
/// IronMapper extension method vs IronMapper <c>IMapper</c> (reflection + MethodInfo.Invoke),
/// AutoMapper <c>IMapper</c> from <c>AddAutoMapper</c>, Mapster <c>IMapper</c> (<c>ServiceMapper</c>).
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class DiOverheadBenchmark
{
    private UserEntity _user = null!;
    private OrderEntity _order = null!;
    private IIronMapper _ironMapper = null!;
    private IAutoMapper _autoMapper = null!;
    private IMapsterMapper _mapster = null!;

    [GlobalSetup]
    public void Setup()
    {
        var data = new TestDataGenerator();
        _user = data.CreateUser();
        _order = data.CreateOrder();
        _ironMapper = IronMapperDiComparisonMapper.CreateMapper();
        _autoMapper = AutoMapperSetup.CreateMapperFromServices();
        _mapster = MapsterSetup.CreateMapperFromServices();
    }

    [Benchmark(Baseline = true), BenchmarkCategory(Categories.Flat)]
    public UserDto Flat_Manual() => ManualMappings.MapFlat(_user);

    [Benchmark, BenchmarkCategory(Categories.Flat)]
    public UserDto Flat_IronMapper() => _user.MapToUserDto();

    [Benchmark, BenchmarkCategory(Categories.Flat)]
    public UserDto Flat_IronMapper_IMapper() => _ironMapper.Map<UserEntity, UserDto>(_user);

    [Benchmark, BenchmarkCategory(Categories.Flat)]
    public UserDto Flat_AutoMapper_IMapper() => _autoMapper.Map<UserEntity, UserDto>(_user);

    [Benchmark, BenchmarkCategory(Categories.Flat)]
    public UserDto Flat_Mapster_IMapper() => _mapster.Map<UserEntity, UserDto>(_user);

    [Benchmark(Baseline = true), BenchmarkCategory(Categories.Nested)]
    public OrderDto Nested_Manual() => ManualMappings.MapNested(_order);

    [Benchmark, BenchmarkCategory(Categories.Nested)]
    public OrderDto Nested_IronMapper() => _order.MapToOrderDto();

    [Benchmark, BenchmarkCategory(Categories.Nested)]
    public OrderDto Nested_IronMapper_IMapper() => _ironMapper.Map<OrderEntity, OrderDto>(_order);

    [Benchmark, BenchmarkCategory(Categories.Nested)]
    public OrderDto Nested_AutoMapper_IMapper() => _autoMapper.Map<OrderEntity, OrderDto>(_order);

    [Benchmark, BenchmarkCategory(Categories.Nested)]
    public OrderDto Nested_Mapster_IMapper() => _mapster.Map<OrderEntity, OrderDto>(_order);
}

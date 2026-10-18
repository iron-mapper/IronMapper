using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapperly;
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Nested;
using IronMapper.Generated;
using Mapster;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Benchmarks;

/// <summary>One order: Customer → Address (depth 3) and 5–20 lines, each with a Product.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory(Categories.Nested)]
public class NestedBenchmark
{
    private OrderEntity _source = null!;
    private IIronMapper _ironMapper = null!;
    private TypeAdapterConfig _mapsterConfig = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new TestDataGenerator().CreateOrder();
        _ironMapper = IronMapperDiComparisonMapper.CreateMapper();
        _mapsterConfig = MapsterSetup.CreateConfig();
        _autoMapper = AutoMapperSetup.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public OrderDto Manual() => ManualMappings.MapNested(_source);

    [Benchmark]
    public OrderDto IronMapper() => _source.MapToOrderDto();

    [Benchmark]
    public OrderDto IronMapper_IMapper() => _ironMapper.Map<OrderEntity, OrderDto>(_source);

    [Benchmark]
    public OrderDto Mapperly() => ComparisonMapperlyMapper.MapNested(_source);

    [Benchmark]
    public OrderDto Mapster() => _source.Adapt<OrderEntity, OrderDto>(_mapsterConfig);

    [Benchmark]
    public OrderDto AutoMapper() => _autoMapper.Map<OrderEntity, OrderDto>(_source);
}

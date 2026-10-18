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

/// <summary>
/// <c>List&lt;OrderEntity&gt;</c> → <c>List&lt;OrderDto&gt;</c>; every order as in <see cref="NestedBenchmark"/>.
/// The <c>ns/item</c> and <c>B/item</c> columns (per order) make the sizes comparable.
/// </summary>
[Config(typeof(PerItemConfig))]
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory(Categories.Collection)]
public class CollectionBenchmark
{
    private List<OrderEntity> _source = null!;
    private IIronMapper _ironMapper = null!;
    private TypeAdapterConfig _mapsterConfig = null!;
    private IMapper _autoMapper = null!;

    [Params(10, 1_000, 100_000)]
    public int Count { get; set; } // name used by PerItemColumn.CountParameter

    [GlobalSetup]
    public void Setup()
    {
        _source = new TestDataGenerator().CreateOrders(Count);
        _ironMapper = IronMapperDiSetup.CreateMapper();
        _mapsterConfig = MapsterSetup.CreateConfig();
        _autoMapper = AutoMapperSetup.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public List<OrderDto> Manual() => ManualMappings.MapCollection(_source);

    [Benchmark]
    public List<OrderDto> IronMapper() => _source.MapToOrderDtoList();

    /// <summary><c>Map&lt;List&lt;T&gt;&gt;</c> is not supported (issue #8); MapCollection is lazy and materialized here.</summary>
    [Benchmark]
    public List<OrderDto> IronMapper_IMapper() => _ironMapper.MapCollection<OrderEntity, OrderDto>(_source).ToList();

    [Benchmark]
    public List<OrderDto> Mapperly() => ComparisonMapperlyMapper.MapCollection(_source);

    [Benchmark]
    public List<OrderDto> Mapster() => _source.Adapt<List<OrderEntity>, List<OrderDto>>(_mapsterConfig);

    [Benchmark]
    public List<OrderDto> AutoMapper() => _autoMapper.Map<List<OrderEntity>, List<OrderDto>>(_source);
}

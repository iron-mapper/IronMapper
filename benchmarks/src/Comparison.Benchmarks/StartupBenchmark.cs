using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapperly;
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Nested;
using IronMapper.Generated;
using Mapster;

namespace Comparison.Benchmarks;

/// <summary>
/// In-process cold start (BENCHMARK_SPEC.md, 8.1): every launch is a new process that runs exactly one invocation.
/// The measured call creates the library's configuration from scratch (nothing for the source generators) and maps
/// the first order, so it includes expression-tree compilation, reflection and the JIT of the first call.
/// <see cref="Setup"/> only creates the data and must not touch any mapper type.
/// </summary>
[SimpleJob(RunStrategy.ColdStart, launchCount: 20, warmupCount: 0, iterationCount: 1)]
public class StartupBenchmark
{
    private OrderEntity _order = null!;

    [GlobalSetup]
    public void Setup() => _order = new TestDataGenerator().CreateOrder();

    [Benchmark(Baseline = true)]
    public OrderDto Manual() => ManualMappings.MapNested(_order);

    [Benchmark]
    public OrderDto IronMapper() => _order.MapToOrderDto();

    [Benchmark]
    public OrderDto IronMapper_IMapper() => IronMapperDiSetup.CreateMapper().Map<OrderEntity, OrderDto>(_order);

    [Benchmark]
    public OrderDto Mapperly() => ComparisonMapperlyMapper.MapNested(_order);

    [Benchmark]
    public OrderDto Mapster() => _order.Adapt<OrderEntity, OrderDto>(MapsterSetup.CreateConfig());

    [Benchmark]
    public OrderDto AutoMapper() => AutoMapperSetup.CreateMapper().Map<OrderEntity, OrderDto>(_order);
}

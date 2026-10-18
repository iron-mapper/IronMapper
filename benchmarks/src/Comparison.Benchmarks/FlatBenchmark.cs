using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapperly;
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Flat;
using IronMapper.Generated;
using Mapster;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Benchmarks;

/// <summary>One flat object, 15 properties.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory(Categories.Flat)]
public class FlatBenchmark
{
    private UserEntity _source = null!;
    private IIronMapper _ironMapper = null!;
    private TypeAdapterConfig _mapsterConfig = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new TestDataGenerator().CreateUser();
        _ironMapper = IronMapperDiSetup.CreateMapper();
        _mapsterConfig = MapsterSetup.CreateConfig();
        _autoMapper = AutoMapperSetup.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public UserDto Manual() => ManualMappings.MapFlat(_source);

    [Benchmark]
    public UserDto IronMapper() => _source.MapToUserDto();

    [Benchmark]
    public UserDto IronMapper_IMapper() => _ironMapper.Map<UserEntity, UserDto>(_source);

    [Benchmark]
    public UserDto Mapperly() => ComparisonMapperlyMapper.MapFlat(_source);

    [Benchmark]
    public UserDto Mapster() => _source.Adapt<UserEntity, UserDto>(_mapsterConfig);

    [Benchmark]
    public UserDto AutoMapper() => _autoMapper.Map<UserEntity, UserDto>(_source);
}

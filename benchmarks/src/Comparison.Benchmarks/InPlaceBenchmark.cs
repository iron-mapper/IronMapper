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

/// <summary>Updates an existing <see cref="UserDto"/> from a <see cref="UserEntity"/>. Every participant supports it.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory(Categories.InPlace)]
public class InPlaceBenchmark
{
    private UserEntity _source = null!;
    private UserDto _destination = null!;
    private IIronMapper _ironMapper = null!;
    private TypeAdapterConfig _mapsterConfig = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new TestDataGenerator().CreateUser();
        _destination = new UserDto();
        _ironMapper = IronMapperDiSetup.CreateMapper();
        _mapsterConfig = MapsterSetup.CreateConfig();
        _autoMapper = AutoMapperSetup.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public UserDto Manual()
    {
        ManualMappings.MapInPlace(_source, _destination);
        return _destination;
    }

    [Benchmark]
    public UserDto IronMapper()
    {
        _source.MapToUserDto(_destination);
        return _destination;
    }

    [Benchmark]
    public UserDto IronMapper_IMapper()
    {
        _ironMapper.Map(_source, _destination);
        return _destination;
    }

    [Benchmark]
    public UserDto Mapperly()
    {
        ComparisonMapperlyMapper.MapInPlace(_source, _destination);
        return _destination;
    }

    [Benchmark]
    public UserDto Mapster() => _source.Adapt(_destination, _mapsterConfig);

    [Benchmark]
    public UserDto AutoMapper() => _autoMapper.Map(_source, _destination);
}

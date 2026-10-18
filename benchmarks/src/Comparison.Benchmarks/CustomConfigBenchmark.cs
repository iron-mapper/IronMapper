extern alias StaticFormat;

using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapperly;
using Comparison.Mappers.Mapster;
using Comparison.Models.Custom;
using Comparison.Models.Data;
using IronMapper.Generated;
using Mapster;
using IIronMapper = IronMapper.Interfaces.IMapper;
using StaticFormatMappers = StaticFormat::IronMapper.Generated.GeneratedMappers;

namespace Comparison.Benchmarks;

/// <summary>Rename + ignore + converter (decimal → "F2" string) + computed property.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory(Categories.CustomConfig)]
public class CustomConfigBenchmark
{
    private EmployeeEntity _source = null!;
    private IIronMapper _ironMapper = null!;
    private TypeAdapterConfig _mapsterConfig = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new TestDataGenerator().CreateEmployee();
        _ironMapper = IronMapperDiSetup.CreateMapper();
        _mapsterConfig = MapsterSetup.CreateConfig();
        _autoMapper = AutoMapperSetup.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public EmployeeDto Manual() => ManualMappings.MapCustom(_source);

    /// <summary>What the user writes: <c>UseConverter&lt;SalaryConverter&gt;()</c> (a new converter on every call).</summary>
    [Benchmark]
    public EmployeeDto IronMapper() => _source.MapToEmployeeDto();

    /// <summary>
    /// Workaround, not the documented converter API: <c>MapFrom(s => SalaryFormat.Format(s.Salary))</c>.
    /// The difference to <see cref="IronMapper"/> is the cost of creating the converter.
    /// </summary>
    [Benchmark]
    public EmployeeDto IronMapper_StaticFormat() => StaticFormatMappers.MapToEmployeeDto(_source);

    [Benchmark]
    public EmployeeDto IronMapper_IMapper() => _ironMapper.Map<EmployeeEntity, EmployeeDto>(_source);

    [Benchmark]
    public EmployeeDto Mapperly() => ComparisonMapperlyMapper.MapCustom(_source);

    [Benchmark]
    public EmployeeDto Mapster() => _source.Adapt<EmployeeEntity, EmployeeDto>(_mapsterConfig);

    [Benchmark]
    public EmployeeDto AutoMapper() => _autoMapper.Map<EmployeeEntity, EmployeeDto>(_source);
}

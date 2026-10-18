extern alias StaticFormat;

using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using StaticFormatMappers = StaticFormat::IronMapper.Generated.GeneratedMappers;

namespace Comparison.Tests;

/// <summary>Mapping paths that only the benchmarks use (not part of IComparisonMapper) give the same results as Manual.</summary>
public class BenchmarkOnlyPathTests
{
    [Fact]
    public void IronMapper_StaticFormat_custom()
    {
        var src = new TestDataGenerator().CreateEmployee();
        src.Salary = 1234.5678m;

        var actual = StaticFormatMappers.MapToEmployeeDto(src);

        Assert.Equivalent(Mappers.Manual.MapCustom(src), actual, strict: true);
        Assert.Equal("1234.57", actual.Salary);
    }

    [Fact]
    public void AutoMapper_from_services()
    {
        var mapper = AutoMapperSetup.CreateMapperFromServices();
        var data = new TestDataGenerator();
        var user = data.CreateUser();
        var order = data.CreateOrder();

        Assert.Equivalent(Mappers.Manual.MapFlat(user), mapper.Map<UserEntity, UserDto>(user), strict: true);
        Assert.Equivalent(Mappers.Manual.MapNested(order), mapper.Map<OrderEntity, OrderDto>(order), strict: true);
    }

    [Fact]
    public void Mapster_from_services()
    {
        var mapper = MapsterSetup.CreateMapperFromServices();
        var data = new TestDataGenerator();
        var user = data.CreateUser();
        var order = data.CreateOrder();

        Assert.Equivalent(Mappers.Manual.MapFlat(user), mapper.Map<UserEntity, UserDto>(user), strict: true);
        Assert.Equivalent(Mappers.Manual.MapNested(order), mapper.Map<OrderEntity, OrderDto>(order), strict: true);
    }
}

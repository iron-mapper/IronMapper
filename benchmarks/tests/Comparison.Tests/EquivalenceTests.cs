using Comparison.Models.Data;
using Comparison.Models.Flat;

namespace Comparison.Tests;

/// <summary>Every library produces the same result as the Manual baseline (BENCHMARK_SPEC.md, 6.1).</summary>
public class EquivalenceTests
{
    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void Flat(string name)
    {
        var src = new TestDataGenerator().CreateUser();

        Assert.Equivalent(Mappers.Manual.MapFlat(src), Mappers.Get(name).MapFlat(src), strict: true);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void Flat_nullable_values_are_null(string name)
    {
        var src = new TestDataGenerator().CreateUser();
        src.Age = null;
        src.CreditLimit = null;

        var actual = Mappers.Get(name).MapFlat(src);

        Assert.Null(actual.Age);
        Assert.Null(actual.CreditLimit);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void Nested(string name)
    {
        var src = new TestDataGenerator().CreateOrder();

        Assert.Equivalent(Mappers.Manual.MapNested(src), Mappers.Get(name).MapNested(src), strict: true);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void Collection(string name)
    {
        var src = new TestDataGenerator().CreateOrders(50);

        var actual = Mappers.Get(name).MapCollection(src);

        Assert.Equal(src.Count, actual.Count);
        Assert.Equivalent(Mappers.Manual.MapCollection(src), actual, strict: true);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void Custom(string name)
    {
        var src = new TestDataGenerator().CreateEmployee();

        var actual = Mappers.Get(name).MapCustom(src);

        Assert.Equivalent(Mappers.Manual.MapCustom(src), actual, strict: true);
        Assert.Equal(src.Email, actual.EmailAddress);
        Assert.Null(actual.Notes);
        Assert.Equal(src.FirstName + " " + src.LastName, actual.FullName);
    }

    [Fact]
    public void Custom_manual_salary_format_is_F2_invariant()
    {
        var src = new TestDataGenerator().CreateEmployee();
        src.Salary = 1234.5678m;

        Assert.Equal("1234.57", Mappers.Manual.MapCustom(src).Salary);
    }

    [Fact]
    public void Test_data_is_deterministic()
    {
        Assert.Equivalent(new TestDataGenerator().CreateOrders(20), new TestDataGenerator().CreateOrders(20), strict: true);
    }

    [Fact]
    public void Test_data_orders_have_5_to_20_lines()
    {
        var orders = new TestDataGenerator().CreateOrders(1_000);

        Assert.All(orders, o => Assert.InRange(o.Lines.Count, TestDataGenerator.MinOrderLines, TestDataGenerator.MaxOrderLines));
        Assert.Contains(orders, o => o.Lines.Count == TestDataGenerator.MinOrderLines);
        Assert.Contains(orders, o => o.Lines.Count == TestDataGenerator.MaxOrderLines);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void InPlace_updates_the_same_instance(string name)
    {
        var src = new TestDataGenerator().CreateUser();
        var dest = new UserDto { Id = -1, UserName = "old", Age = -1 };

        Mappers.Get(name).MapInPlace(src, dest);

        Assert.Equivalent(Mappers.Manual.MapFlat(src), dest, strict: true);
    }

    [Theory]
    [MemberData(nameof(Mappers.Libraries), MemberType = typeof(Mappers))]
    public void InPlace_keeps_the_destination_reference(string name)
    {
        var src = new TestDataGenerator().CreateUser();
        var dest = new UserDto();
        var before = dest;

        Mappers.Get(name).MapInPlace(src, dest);

        Assert.Same(before, dest);
        Assert.Equal(src.Id, dest.Id);
    }
}

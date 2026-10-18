using Comparison.Models.Data;

namespace Comparison.Tests;

/// <summary>
/// No object or collection of the result is a reference into the source, and every call creates new nested
/// DTOs (BENCHMARK_SPEC.md, 6.2). Every mapped type differs from its source type, so this is all-or-nothing:
/// strings are immutable and shared on purpose, so they are not checked.
/// </summary>
public class DeepCopyTests
{
    [Theory]
    [MemberData(nameof(Mappers.AllNames), MemberType = typeof(Mappers))]
    public void Nested(string name)
    {
        var src = new TestDataGenerator().CreateOrder();
        var mapper = Mappers.Get(name);

        var first = mapper.MapNested(src);
        var second = mapper.MapNested(src);

        AssertDisjoint(ObjectGraph.Collect(src), ObjectGraph.Collect(first));
        AssertDisjoint(ObjectGraph.Collect(first), ObjectGraph.Collect(second));
        Assert.NotSame(first.Customer, second.Customer);
        Assert.NotSame(first.Customer.Address, second.Customer.Address);
        Assert.NotSame(first.Lines, second.Lines);
        Assert.NotSame(first.Lines[0].Product, second.Lines[0].Product);
    }

    [Theory]
    [MemberData(nameof(Mappers.AllNames), MemberType = typeof(Mappers))]
    public void Collection(string name)
    {
        var src = new TestDataGenerator().CreateOrders(10);
        var mapper = Mappers.Get(name);

        var first = mapper.MapCollection(src);
        var second = mapper.MapCollection(src);

        AssertDisjoint(ObjectGraph.Collect(src), ObjectGraph.Collect(first));
        AssertDisjoint(ObjectGraph.Collect(first), ObjectGraph.Collect(second));
    }

    [Theory]
    [MemberData(nameof(Mappers.AllNames), MemberType = typeof(Mappers))]
    public void Flat_and_custom(string name)
    {
        var data = new TestDataGenerator();
        var user = data.CreateUser();
        var employee = data.CreateEmployee();
        var mapper = Mappers.Get(name);

        AssertDisjoint(ObjectGraph.Collect(user), ObjectGraph.Collect(mapper.MapFlat(user)));
        Assert.NotSame(mapper.MapFlat(user), mapper.MapFlat(user));
        AssertDisjoint(ObjectGraph.Collect(employee), ObjectGraph.Collect(mapper.MapCustom(employee)));
    }

    [Fact]
    public void Object_graph_finds_shared_nested_instances()
    {
        var src = new TestDataGenerator().CreateOrder();
        var sharesProduct = new { Lines = new List<object> { new { Product = src.Lines[3].Product } } };

        Assert.Contains(src.Lines[3].Product, ObjectGraph.Collect(sharesProduct));
        Assert.Throws<Xunit.Sdk.TrueException>(() => AssertDisjoint(ObjectGraph.Collect(src), ObjectGraph.Collect(sharesProduct)));
    }

    private static void AssertDisjoint(HashSet<object> source, HashSet<object> result)
    {
        var shared = result.Where(source.Contains).Select(o => o.GetType().Name).ToList();
        Assert.True(shared.Count == 0, $"Result shares instances with the source: {string.Join(", ", shared)}");
    }
}

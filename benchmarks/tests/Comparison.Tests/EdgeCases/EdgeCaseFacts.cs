using Comparison.Models.Data;
using Comparison.Models.EdgeCases;

namespace Comparison.Tests.EdgeCases;

/// <summary>
/// Records what each library actually does in an edge case (BENCHMARK_SPEC.md, 6.4).
/// Exceptions are part of the outcome, not test failures.
/// </summary>
internal static class EdgeCaseFacts
{
    public sealed record Case(string Id, string Title, Func<string, string> Evaluate);

    public static readonly Case[] Cases =
    [
        new("null-source", "Top-level source is `null` (Flat)", NullSource),
        new("null-nested", "Nested object is `null` (`Order.Customer`)", NullNested),
        new("null-collection", "Nested collection is `null` (`Order.Lines`)", NullCollection),
        new("same-element-collection", "`List<string>` → `List<string>`", SameElementCollection),
        new("positional-record", "Positional record as destination", PositionalRecord),
        new("flattening", "Flattening by name (`Customer.Address.City` → `CustomerAddressCity`)", Flattening),
    ];

    public static Case Get(string id) => Cases.Single(c => c.Id == id);

    private static string NullSource(string name) => Run(() =>
    {
        var result = Mappers.Get(name).MapFlat(null!);
        return result is null ? "returns `null`" : "returns a default object";
    });

    private static string NullNested(string name) => Run(() =>
    {
        var src = new TestDataGenerator().CreateOrder();
        src.Customer = null!;
        var result = Mappers.Get(name).MapNested(src);
        return result.Customer is null ? "`null` → `null`" : "creates an empty object";
    });

    private static string NullCollection(string name) => Run(() =>
    {
        var src = new TestDataGenerator().CreateOrder();
        src.Lines = null!;
        var result = Mappers.Get(name).MapNested(src);
        return result.Lines switch
        {
            null => "`null` → `null`",
            { Count: 0 } => "`null` → empty list",
            _ => "`null` → non-empty list (?)",
        };
    });

    private static string SameElementCollection(string name) => Run(() =>
    {
        var src = new TagsEntity { Id = 1, Tags = ["a", "b", "c"] };
        var result = Mappers.GetEdgeCase(name).MapTags(src);
        if (ReferenceEquals(result.Tags, src.Tags))
            return "reference copied (same list)";
        return result.Tags.SequenceEqual(src.Tags) ? "new list (copy)" : "new list, different content (?)";
    });

    private static string PositionalRecord(string name) => Run(() =>
    {
        var result = Mappers.GetEdgeCase(name).MapPoint(new PointEntity { X = 1.5, Y = -2 });
        return result == new PointRecordDto(1.5, -2) ? "supported (constructor)" : $"wrong values: {result}";
    });

    private static string Flattening(string name) => Run(() =>
    {
        var src = new TestDataGenerator().CreateOrder();
        var result = Mappers.GetEdgeCase(name).MapFlattening(src);
        var city = result.CustomerAddressCity == src.Customer.Address.City;
        var customerName = result.CustomerName == src.Customer.Name;
        return (city, customerName) switch
        {
            (true, true) => "automatic (2 and 3 levels)",
            (false, true) => "2 levels only (`CustomerName`), `CustomerAddressCity` stays `null`",
            (true, false) => "3 levels only (?)",
            _ => "not supported: properties stay `null`",
        };
    });

    private static string Run(Func<string> evaluate)
    {
        try
        {
            return evaluate();
        }
        catch (NotSupportedException ex)
        {
            return $"N/A: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"throws `{ex.GetType().Name}`";
        }
    }
}

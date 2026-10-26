using Comparison.Mappers;
using Comparison.Models.Data;
using Comparison.Models.EdgeCases;

namespace Comparison.Tests.EdgeCases;

/// <summary>
/// Fact collection, not a gate (BENCHMARK_SPEC.md, 6.4): each test records the observed behavior and passes.
/// <see cref="FeatureMatrixTests"/> turns the same facts into results/feature-matrix.md.
/// </summary>
public class EdgeCaseTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> CaseAndMapper()
    {
        var data = new TheoryData<string, string>();
        foreach (var edgeCase in EdgeCaseFacts.Cases)
        {
            foreach (string name in Mappers.Names)
            {
                data.Add(edgeCase.Id, name);
            }
        }
        return data;
    }

    /// <summary>The nullable edge-case mappings are correct when nothing is null, so the null facts compare like with like.</summary>
    [Theory]
    [MemberData(nameof(Mappers.AllNames), MemberType = typeof(Mappers))]
    public void Nullable_order_without_nulls_matches_manual(string name)
    {
        var order = new TestDataGenerator().CreateOrder();
        var src = new NullableOrderEntity { Id = order.Id, Number = order.Number, Customer = order.Customer, Lines = order.Lines };

        var expected = Mappers.GetEdgeCase(MapperNames.Manual).MapNullableOrder(src);

        Assert.Equivalent(expected, Mappers.GetEdgeCase(name).MapNullableOrder(src), strict: true);
    }

    [Theory]
    [MemberData(nameof(Mappers.AllNames), MemberType = typeof(Mappers))]
    public void Nullable_user_without_null_matches_manual(string name)
    {
        var user = new TestDataGenerator().CreateUser();

        Assert.Equivalent(Mappers.Manual.MapFlat(user), Mappers.GetEdgeCase(name).MapNullableUser(user), strict: true);
    }

    [Theory]
    [MemberData(nameof(CaseAndMapper))]
    public void Record_behavior(string caseId, string name)
    {
        string outcome = EdgeCaseFacts.Get(caseId).Evaluate(name);
        output.WriteLine($"{caseId} | {name} | {outcome}");
    }
}

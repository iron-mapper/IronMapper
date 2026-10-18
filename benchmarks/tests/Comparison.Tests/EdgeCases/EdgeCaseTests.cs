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
            foreach (var name in Mappers.Names)
                data.Add(edgeCase.Id, name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseAndMapper))]
    public void Record_behavior(string caseId, string name)
    {
        var outcome = EdgeCaseFacts.Get(caseId).Evaluate(name);
        output.WriteLine($"{caseId} | {name} | {outcome}");
    }
}

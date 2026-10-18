using Comparison.Models.EdgeCases;
using Comparison.Models.Nested;

namespace Comparison.Mappers.EdgeCases;

/// <summary>
/// Mappings used only to collect facts for the feature matrix (BENCHMARK_SPEC.md, 6.4); never benchmarked.
/// Each mapping uses the library's default behavior, without extra configuration for the edge case.
/// A mapping the library cannot express (does not compile) throws <see cref="NotSupportedException"/>.
/// </summary>
public interface IEdgeCaseMapper
{
    string Name { get; }

    /// <summary><c>List&lt;string&gt;</c> → <c>List&lt;string&gt;</c>.</summary>
    TagsDto MapTags(TagsEntity src);

    /// <summary>Destination is a positional record.</summary>
    PointRecordDto MapPoint(PointEntity src);

    /// <summary><c>Customer.Address.City</c> → <c>CustomerAddressCity</c> by naming convention only.</summary>
    OrderFlatDto MapFlattening(OrderEntity src);
}

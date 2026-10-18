using Comparison.Models.Nested;

namespace Comparison.Models.EdgeCases;

/// <summary>
/// Edge case: the same shape as <see cref="OrderEntity"/>, but the nested object and the collection are declared
/// nullable. Libraries that read nullable annotations (Mapperly) generate different code for it.
/// </summary>
public class NullableOrderEntity
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public CustomerEntity? Customer { get; set; }
    public List<OrderLineEntity>? Lines { get; set; }
}

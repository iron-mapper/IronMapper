using Comparison.Models.Nested;

namespace Comparison.Models.EdgeCases;

/// <summary>Destination of <see cref="NullableOrderEntity"/>.</summary>
public class NullableOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public CustomerDto? Customer { get; set; }
    public List<OrderLineDto>? Lines { get; set; }
}

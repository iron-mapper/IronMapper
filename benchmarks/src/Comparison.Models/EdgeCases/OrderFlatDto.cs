namespace Comparison.Models.EdgeCases;

/// <summary>
/// Edge case: flattening of <see cref="Nested.OrderEntity"/>
/// (<c>Customer.Name</c> → <c>CustomerName</c>, <c>Customer.Address.City</c> → <c>CustomerAddressCity</c>).
/// </summary>
public class OrderFlatDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerAddressCity { get; set; }
}

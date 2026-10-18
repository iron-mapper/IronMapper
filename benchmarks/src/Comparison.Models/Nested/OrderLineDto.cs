namespace Comparison.Models.Nested;

public class OrderLineDto
{
    public int Id { get; set; }
    public ProductDto Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

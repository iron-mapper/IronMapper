namespace Comparison.Models.Nested;

public class OrderLineEntity
{
    public int Id { get; set; }
    public ProductEntity Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

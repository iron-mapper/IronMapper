namespace Comparison.Models.Nested;

public class OrderEntity
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public CustomerEntity Customer { get; set; } = null!;
    public List<OrderLineEntity> Lines { get; set; } = null!;
}

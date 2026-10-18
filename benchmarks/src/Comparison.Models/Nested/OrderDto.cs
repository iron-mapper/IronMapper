namespace Comparison.Models.Nested;

public class OrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public CustomerDto Customer { get; set; } = null!;
    public List<OrderLineDto> Lines { get; set; } = null!;
}

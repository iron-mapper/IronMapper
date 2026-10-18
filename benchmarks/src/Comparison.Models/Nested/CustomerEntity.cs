namespace Comparison.Models.Nested;

public class CustomerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public AddressEntity Address { get; set; } = null!;
}

namespace Comparison.Models.Flat;

public class UserEntity
{
    public int Id { get; set; }
    public long ExternalId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public double Rating { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTimeOffset LastLoginAt { get; set; }
    public Guid CorrelationId { get; set; }
    public UserStatus Status { get; set; }
    public int? Age { get; set; }
    public decimal? CreditLimit { get; set; }
}

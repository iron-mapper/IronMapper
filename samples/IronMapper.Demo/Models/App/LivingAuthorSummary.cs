namespace IronMapper.Demo.Models.App;

/// <summary>
/// Destination type used in Demo 6 to demonstrate conditional mapping.
/// Only populated when the source author has no death date.
/// </summary>
public class LivingAuthorSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BirthYear { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}

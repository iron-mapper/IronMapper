namespace IronMapper.Demo.Models.App;

public class AuthorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? BirthYear { get; set; }
    public string? DeathYear { get; set; }
    public bool IsAlive { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}

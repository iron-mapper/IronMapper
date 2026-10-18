namespace Comparison.Models.EdgeCases;

/// <summary>Destination of <see cref="TagsEntity"/>.</summary>
public class TagsDto
{
    public int Id { get; set; }
    public List<string> Tags { get; set; } = null!;
}

namespace Comparison.Models.EdgeCases;

/// <summary>Edge case: a collection with the same element type on both sides (copy or reference?).</summary>
public class TagsEntity
{
    public int Id { get; set; }
    public List<string> Tags { get; set; } = null!;
}

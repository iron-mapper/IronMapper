using System.Collections.Generic;

namespace IronMapper.Demo.Models.App;

public class BookDetail
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AuthorInfo? Author { get; set; }
    public List<string> Subjects { get; set; } = new();
    public string? FirstPublishDate { get; set; }
    public string CoverUrl { get; set; } = string.Empty;
}

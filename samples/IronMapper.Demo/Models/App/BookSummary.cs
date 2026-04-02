namespace IronMapper.Demo.Models.App;

public class BookSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AuthorsDisplay { get; set; } = string.Empty;
    public int? PublishedYear { get; set; }
    public string EditionsInfo { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public List<string> TopSubjects { get; set; } = [];
}

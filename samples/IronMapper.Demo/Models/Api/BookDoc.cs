using System.Text.Json.Serialization;

namespace IronMapper.Demo.Models.Api;

public class BookDoc
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author_name")]
    public List<string>? AuthorName { get; set; }

    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }

    [JsonPropertyName("edition_count")]
    public int? EditionCount { get; set; }

    [JsonPropertyName("subject")]
    public List<string>? Subject { get; set; }

    [JsonPropertyName("cover_i")]
    public int? CoverI { get; set; }
}

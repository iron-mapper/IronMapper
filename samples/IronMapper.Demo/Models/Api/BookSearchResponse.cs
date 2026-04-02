using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IronMapper.Demo.Models.Api;

public class BookSearchResponse
{
    [JsonPropertyName("numFound")]
    public int NumFound { get; set; }

    [JsonPropertyName("docs")]
    public List<BookDoc>? Docs { get; set; }
}

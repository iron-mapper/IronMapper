using System.Collections.Generic;
using System.Text.Json.Serialization;
using IronMapper.Demo.Services.Converters;

namespace IronMapper.Demo.Models.Api;

public class WorkResponse
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Description { get; set; }

    [JsonPropertyName("subjects")]
    public List<string>? Subjects { get; set; }

    [JsonPropertyName("first_publish_date")]
    public string? FirstPublishDate { get; set; }

    [JsonPropertyName("covers")]
    public List<int>? Covers { get; set; }
}

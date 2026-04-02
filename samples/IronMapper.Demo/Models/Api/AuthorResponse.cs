using System.Collections.Generic;
using System.Text.Json.Serialization;
using IronMapper.Demo.Services.Converters;

namespace IronMapper.Demo.Models.Api;

public class AuthorResponse
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("bio")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Bio { get; set; }

    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; set; }

    [JsonPropertyName("death_date")]
    public string? DeathDate { get; set; }

    [JsonPropertyName("photos")]
    public List<int>? Photos { get; set; }
}

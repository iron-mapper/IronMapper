using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronMapper.Demo.Services.Converters;

/// <summary>
/// Handles Open Library fields that can be either a plain string or {"type":..., "value":"..."}.
/// Used for "bio" on AuthorResponse and "description" on WorkResponse.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartObject:
                return ReadValueFromObject(ref reader);

            default:
                reader.Skip();
                return null;
        }
    }

    private static string? ReadValueFromObject(ref Utf8JsonReader reader)
    {
        string? result = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read(); // advance to value token
                if (propName == "value")
                    result = reader.GetString();
                else
                    reader.Skip();
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

/// <summary>Retains the evidence needed to reject duplicate input keys before dictionary materialization loses it.</summary>
public sealed class VariantOptionsJsonConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Options must be an object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("An option key was expected.");
            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new JsonException("Option values must be strings.");
            if (!result.TryAdd(key, reader.GetString()!)) throw new JsonException("Duplicate option key.");
            if (result.Count > 20) throw new JsonException("At most 20 options are allowed.");
        }
        throw new JsonException("Incomplete options object.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}

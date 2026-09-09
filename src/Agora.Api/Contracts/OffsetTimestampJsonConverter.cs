using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Agora.Api.Contracts;

/// <summary>Rejects local timestamps whose instant would depend on the server's timezone.</summary>
public sealed partial class OffsetTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetSuffix();
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !OffsetSuffix().IsMatch(reader.GetString()!) || !reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("Use an ISO timestamp with Z or an explicit timezone offset.");
        return value.ToUniversalTime();
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

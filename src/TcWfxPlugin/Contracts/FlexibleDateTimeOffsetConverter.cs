using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TcWfxPlugin.Contracts;

internal sealed class FlexibleDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string or null for DateTimeOffset, but found {reader.TokenType}.");
        }

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = NormalizeOffsetWithoutColon(raw.Trim());
        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        throw new JsonException($"Invalid DateTimeOffset value '{raw}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string NormalizeOffsetWithoutColon(string value)
    {
        // Alfresco can return offsets like +0000; .NET roundtrip parser expects +00:00.
        if (value.Length >= 5)
        {
            var suffix = value[^5..];
            if ((suffix[0] == '+' || suffix[0] == '-')
                && char.IsDigit(suffix[1])
                && char.IsDigit(suffix[2])
                && char.IsDigit(suffix[3])
                && char.IsDigit(suffix[4]))
            {
                return string.Concat(value.AsSpan(0, value.Length - 5), suffix.AsSpan(0, 3), ":", suffix.AsSpan(3, 2));
            }
        }

        return value;
    }
}

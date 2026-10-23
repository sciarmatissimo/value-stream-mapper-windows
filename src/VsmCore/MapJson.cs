// JSON (de)serialisation of .vsmap documents, compatible with the Apple app:
// camel-cased keys, enum raw values, uppercase UUIDs, ISO-8601 dates with
// second precision, and optional fields omitted when null.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VsmCore;

public static class MapJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
        };
        // MapState must come first: the generic enum converter would otherwise claim it.
        o.Converters.Add(new MapStateConverter());
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.Converters.Add(new UpperGuidConverter());
        o.Converters.Add(new Iso8601SecondsConverter());
        return o;
    }

    public static string Serialize(ValueStreamMap map) => JsonSerializer.Serialize(map, Options);

    public static ValueStreamMap Deserialize(string json) =>
        JsonSerializer.Deserialize<ValueStreamMap>(json, Options) ?? throw new JsonException("Empty document");

    public static ValueStreamMap Deserialize(byte[] data) =>
        JsonSerializer.Deserialize<ValueStreamMap>(data, Options) ?? throw new JsonException("Empty document");

    private sealed class MapStateConverter : JsonConverter<MapState>
    {
        public override MapState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            MapStateExtensions.FromRaw(reader.GetString() ?? "");

        public override void Write(Utf8JsonWriter writer, MapState value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.RawValue());
    }

    private sealed class UpperGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Guid.Parse(reader.GetString() ?? Guid.Empty.ToString());

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
    }

    /// <summary>Swift's .iso8601 strategy writes "2026-09-28T20:44:00Z" and rejects fractional seconds.</summary>
    private sealed class Iso8601SecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString() ?? "";
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? d : DateTime.UtcNow;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}

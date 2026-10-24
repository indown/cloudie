using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudie.Core.Netease;

/// <summary>
/// NetEase is loose with integers: <c>personalized/playlist</c> sends <c>"playCount": 1.2345678E7</c>, other
/// endpoints quote numbers. Reading every <see cref="long"/> and <see cref="int"/> through here (fractions
/// truncated, strings parsed, booleans as 0/1) keeps one odd field from failing a whole response.
/// </summary>
public sealed class LenientNumberConverter : JsonConverterFactory
{
    // A converter registered for long is not used for long?; the nullable forms have to be listed as well.
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(long) || typeToConvert == typeof(int) || typeToConvert == typeof(long?) || typeToConvert == typeof(int?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(long) ? new LongConverter()
        : typeToConvert == typeof(int) ? new IntConverter()
        : typeToConvert == typeof(long?) ? new NullableLongConverter()
        : new NullableIntConverter();

    internal static long ReadInteger(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var integer)) return integer;
                if (reader.TryGetDouble(out var number)) return Truncate(number);
                break;
            case JsonTokenType.String:
                var text = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) return 0;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return Truncate(real);
                break;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
        }
        throw new JsonException($"Expected an integer but found {reader.TokenType}.");
    }

    private static long Truncate(double value) =>
        double.IsNaN(value) ? 0 : value >= long.MaxValue ? long.MaxValue : value <= long.MinValue ? long.MinValue : (long)Math.Truncate(value);

    private sealed class LongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadInteger(ref reader);
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class IntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadInt32(ref reader);
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class NullableLongConverter : JsonConverter<long?>
    {
        public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : ReadInteger(ref reader);

        public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
        {
            if (value is { } number) writer.WriteNumberValue(number);
            else writer.WriteNullValue();
        }
    }

    private sealed class NullableIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : ReadInt32(ref reader);

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value is { } number) writer.WriteNumberValue(number);
            else writer.WriteNullValue();
        }
    }

    private static int ReadInt32(ref Utf8JsonReader reader) => (int)Math.Clamp(ReadInteger(ref reader), int.MinValue, int.MaxValue);
}

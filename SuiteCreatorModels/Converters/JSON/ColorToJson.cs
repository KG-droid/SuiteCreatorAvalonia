using Avalonia.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SuiteCreatorAvalonia.Converters;

public class ColorToJson : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Color) || objectType == typeof(Color?);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not Color color)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteValue($"#{color.A:x2}{color.R:x2}{color.G:x2}{color.B:x2}");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        // Current format: a hex string, e.g. "#ffd1ffc6" (also accepts named colors, rgb()/rgba(), hsl(), etc.)
        if (reader.TokenType == JsonToken.String)
        {
            if (reader.Value is string colorStr && Color.TryParse(colorStr, out Color color))
            {
                return color;
            }
            throw new JsonSerializationException($"Failed to parse color string: {reader.Value}, to a Color.");
        }

        // Legacy format: an object with A, R, G, B byte properties, e.g. { "A": 255, "R": 209, "G": 255, "B": 198 }
        if (reader.TokenType == JsonToken.StartObject)
        {
            JObject jsonObject = JObject.Load(reader);
            byte a = jsonObject[nameof(Color.A)]?.Value<byte>() ?? byte.MaxValue;
            byte r = jsonObject[nameof(Color.R)]?.Value<byte>() ?? byte.MinValue;
            byte g = jsonObject[nameof(Color.G)]?.Value<byte>() ?? byte.MinValue;
            byte b = jsonObject[nameof(Color.B)]?.Value<byte>() ?? byte.MinValue;
            return new Color(a, r, g, b);
        }

        throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for Color, expected a string or an object");
    }
}
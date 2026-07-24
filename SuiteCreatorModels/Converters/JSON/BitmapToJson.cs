using Newtonsoft.Json;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteCreatorAvalonia.Converters;
public class BitmapToJson : JsonConverter<Bitmap>
{
    public override void WriteJson(JsonWriter writer, Bitmap? value, JsonSerializer serializer)
    {
        if (value is Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms);
                string base64 = Convert.ToBase64String(ms.ToArray());
                writer.WriteValue(base64);
            }
        }
        else
        {
            throw new JsonSerializationException($"Expected a Bitmap to convert to Base64, but got a: {value.GetType().Name}");
        }
    }

    public override Bitmap? ReadJson(JsonReader reader, Type objectType, Bitmap? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }
        if (reader.TokenType != JsonToken.String)
        {
            throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for Bitmap, expected a string");
        }
        if (reader.Value is string base64Str)
        {
            try
            {
                if (base64Str.Length > 1_400_000)
                    throw new JsonSerializationException("Base64 Image string too large.");
                byte[] imageBytes = Convert.FromBase64String(base64Str);
                using (var ms = new MemoryStream(imageBytes))
                {
                    Bitmap bmp = new Bitmap(ms);
                    if (bmp.PixelSize.Width > 1024 || bmp.PixelSize.Height > 1024)
                        throw new JsonSerializationException("Bitmap dimensions too large.");
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException($"Failed to convert base64 string to Bitmap: {ex.Message}", ex);
            }
        }
        throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for Bitmap conversion, expected a string");
    }
}
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Newtonsoft.Json;

namespace SuiteCreatorAvalonia.Converters;
public class TextDocumentToJson : JsonConverter<TextDocument>
{
    public override void WriteJson(JsonWriter writer, TextDocument value, JsonSerializer serializer)
    {
        if (value is TextDocument txtDoc)
        {
            string text = Dispatcher.UIThread.Invoke(() => txtDoc.Text);
            writer.WriteValue(text);
        }
        else
        {
            throw new JsonSerializationException($"Unable to match Type: {value.GetType().Name}, to a MaterialIconKind");
        }
    }

    public override TextDocument? ReadJson(JsonReader reader, Type objectType, TextDocument existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.String && reader.TokenType != JsonToken.Null)
        {
            throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for TextDocument, expected a string or null");
        }
        TextDocument? txtDoc = null;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            txtDoc = new();
            if (reader.Value is string txt)
                txtDoc.Text = txt;
        }).Wait();
        return txtDoc;
    }
}
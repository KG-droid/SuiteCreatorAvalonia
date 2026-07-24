using Newtonsoft.Json;
using System.Security.Principal;

namespace SuiteCreatorAvalonia.Converters;
public class SecurityIdentifierToJson : JsonConverter<SecurityIdentifier>
{
    public override void WriteJson(JsonWriter writer, SecurityIdentifier? value, JsonSerializer serializer)
    {
        if (value is SecurityIdentifier sid)
        {
            writer.WriteValue(sid.Value);
        }
        else
        {
            writer.WriteNull();
        }
    }

    public override SecurityIdentifier? ReadJson(JsonReader reader, Type objectType, SecurityIdentifier? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        if (reader.TokenType != JsonToken.String)
            throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for SecurityIdentifier, expected a string");

        if (reader.Value is string sidStr)
            return new SecurityIdentifier(sidStr);

        throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}, for SecurityIdentifier, expected a string");
    }
}

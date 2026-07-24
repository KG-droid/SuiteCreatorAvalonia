using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuiteUserPopup.Models.Config;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = new[]
    {
        typeof(JsonStringEnumConverter<PopupAction>),
        typeof(JsonStringEnumConverter<SuiteAction>)
    }
)]
[JsonSerializable(typeof(PopConfig))]
internal partial class PopConfigJsonContext : JsonSerializerContext
{
}

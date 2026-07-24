using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuiteProgressPopup.Models;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(ProgressStatus))]
internal partial class ProgressStatusJsonContext : JsonSerializerContext
{
}

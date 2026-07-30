using System.Text.Json.Serialization;

namespace SuiteOperations.Events
{
    // Source-generated JSON (de)serialization for the pre-existence sidecar - reflection-based
    // JsonSerializer.Serialize/Deserialize is unavailable because the executor is AOT-published.
    [JsonSerializable(typeof(Dictionary<string, bool>))]
    internal partial class RegExecEventJsonContext : JsonSerializerContext
    {
    }
}

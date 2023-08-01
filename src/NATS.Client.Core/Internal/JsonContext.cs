using System.Text.Json.Serialization;

namespace NATS.Client.Core.Internal;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyFields = false,
    IgnoreReadOnlyProperties = false,
    IncludeFields = false,
    WriteIndented = false)]
[JsonSerializable(typeof(ServerInfo), GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ClientOptions), GenerationMode = JsonSourceGenerationMode.Serialization)]
internal sealed partial class JsonContext : JsonSerializerContext
{
}

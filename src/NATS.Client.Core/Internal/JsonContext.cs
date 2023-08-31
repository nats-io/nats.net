using System.Text.Json.Serialization;

namespace NATS.Client.Core.Internal;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServerInfo), GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ClientOpts), GenerationMode = JsonSourceGenerationMode.Serialization)]
internal sealed partial class JsonContext : JsonSerializerContext
{
}

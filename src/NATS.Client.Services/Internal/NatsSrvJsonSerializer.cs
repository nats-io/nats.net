using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.Services.Models;

namespace NATS.Client.Services.Internal;

internal class NatsSrvJsonSerializer
{
    public static readonly INatsSerializer Default = new NatsJsonContextSerializer(NatsSrvJsonSerializerContext.Default);
}

[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(EndpointInfo))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(EndpointStats))]
internal partial class NatsSrvJsonSerializerContext : JsonSerializerContext
{
}

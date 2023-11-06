using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.Services.Models;

namespace NATS.Client.Services.Internal;

internal static class NatsSrvJsonSerializer<T>
{
    private static readonly NatsJsonContextSerializer<T> Default = new(NatsSrvJsonSerializerContext.Default);

#pragma warning disable SA1202
    public static readonly INatsSerializer<T> DefaultSerializer = Default;

    public static readonly INatsDeserializer<T> DefaultDeserializer = Default;
#pragma warning restore SA1202
}

[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(EndpointInfo))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(EndpointStats))]
internal partial class NatsSrvJsonSerializerContext : JsonSerializerContext
{
}

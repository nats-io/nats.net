using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore.Internal;

internal static class NatsObjJsonSerializer
{
    public static readonly INatsSerializer Default = new NatsJsonContextSerializer(NatsObjJsonSerializerContext.Default);
}

[JsonSerializable(typeof(ObjectMetadata))]
[JsonSerializable(typeof(MetaDataOptions))]
[JsonSerializable(typeof(NatsObjLink))]
internal partial class NatsObjJsonSerializerContext : JsonSerializerContext
{
}

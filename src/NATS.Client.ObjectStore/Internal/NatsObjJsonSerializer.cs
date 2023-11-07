using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore.Internal;

internal static class NatsObjJsonSerializer<T>
{
    public static readonly INatsSerializer<T> Default = new NatsJsonContextSerializer<T>(NatsObjJsonSerializerContext.Default);
}

[JsonSerializable(typeof(ObjectMetadata))]
[JsonSerializable(typeof(MetaDataOptions))]
[JsonSerializable(typeof(NatsObjLink))]
internal partial class NatsObjJsonSerializerContext : JsonSerializerContext
{
}

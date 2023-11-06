using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore.Internal;

internal static class NatsObjJsonSerializer<T>
{
    private static readonly NatsJsonContextSerializer<T> Default = new(NatsObjJsonSerializerContext.Default);

#pragma warning disable SA1202
    public static readonly INatsSerializer<T> DefaultSerializer = Default;

    public static readonly INatsDeserializer<T> DefaultDeserializer = Default;
#pragma warning restore SA1202
}

[JsonSerializable(typeof(ObjectMetadata))]
[JsonSerializable(typeof(MetaDataOptions))]
[JsonSerializable(typeof(NatsObjLink))]
internal partial class NatsObjJsonSerializerContext : JsonSerializerContext
{
}

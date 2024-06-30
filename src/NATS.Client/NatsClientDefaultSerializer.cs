using NATS.Client.Core;
using NATS.Client.Serializers.Json;

namespace NATS.Client;

public static class NatsClientDefaultSerializer<T>
{
    public static readonly INatsSerializer<T> Default;

    static NatsClientDefaultSerializer()
    {
        Default = new NatsSerializerBuilder<T>()
            .Add(new NatsRawSerializer<T>())
            .Add(new NatsUtf8PrimitivesSerializer<T>())
            .Add(new NatsJsonSerializer<T>())
            .Build();
    }
}

public class NatsClientDefaultSerializerRegistry : INatsSerializerRegistry
{
    public static readonly NatsClientDefaultSerializerRegistry Default = new();

    public INatsSerialize<T> GetSerializer<T>() => NatsClientDefaultSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => NatsClientDefaultSerializer<T>.Default;
}

using NATS.Client.Core;
using NATS.Client.Serializers.Json;

namespace NATS.Net;

/// <summary>
/// Default serializer interface for NATS messages.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public static class NatsClientDefaultSerializer<T>
{
    /// <summary>
    /// Default serializer interface for NATS messages.
    /// </summary>
    public static readonly INatsSerializer<T> Default;

    static NatsClientDefaultSerializer()
    {
        Default = new NatsSerializerBuilder<T>()
            .Add(new NatsRawSerializer<T>())
            .Add(new NatsUtf8PrimitivesSerializer<T>())
            .Add(new NatsJsonSerializer<T>())
            .Build();
        Console.WriteLine($"Default serializer for {typeof(T).Name} is {Default.GetType().Name}");
    }
}

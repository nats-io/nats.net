using NATS.Client.Core;

namespace NATS.Net;

/// <summary>
/// Default implementation of the INatsSerializerRegistry interface.
/// It provides the default serializer and deserializer for primitive types,
/// binary data, and JSON serialization.
/// </summary>
public class NatsClientDefaultSerializerRegistry : INatsSerializerRegistry
{
    /// <summary>
    /// Default implementation of the INatsSerializerRegistry interface.
    /// It provides the default serializer and deserializer for primitive types,
    /// binary data, and JSON serialization.
    /// </summary>
    public static readonly NatsClientDefaultSerializerRegistry Default = new();

    /// <inheritdoc />
    public INatsSerialize<T> GetSerializer<T>() => NatsClientDefaultSerializer<T>.Default;

    /// <inheritdoc />
    public INatsDeserialize<T> GetDeserializer<T>() => NatsClientDefaultSerializer<T>.Default;
}

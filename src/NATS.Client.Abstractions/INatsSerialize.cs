using System.Buffers;

namespace NATS.Client.Core;

/// <summary>
/// Serializer interface for NATS messages.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public interface INatsSerializer<T> : INatsSerialize<T>, INatsDeserialize<T>
{
    /// <summary>
    /// Combines the current serializer with the specified serializer.
    /// </summary>
    /// <param name="next">The serializer to be combined with.</param>
    /// <returns>The combined serializer.</returns>
    INatsSerializer<T> CombineWith(INatsSerializer<T> next);
}

/// <summary>
/// Serializer interface for NATS messages.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public interface INatsSerialize<in T>
{
    /// <summary>
    /// Serialize value to buffer.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    void Serialize(IBufferWriter<byte> bufferWriter, T value);
}

/// <summary>
/// Deserializer interface for NATS messages.
/// </summary>
/// <typeparam name="T">Deserialized object type</typeparam>
public interface INatsDeserialize<out T>
{
    /// <summary>
    /// Deserialize value from buffer.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    T? Deserialize(in ReadOnlySequence<byte> buffer);
}

/// <summary>
/// Extended serializer interface that supports sending headers during serialization.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public interface INatsSerializeWithHeaders<in T> : INatsSerialize<T>
{
    /// <summary>
    /// Serialize value to buffer with headers.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
    void Serialize(IBufferWriter<byte> bufferWriter, T value, INatsHeaders? headers);
}

/// <summary>
/// Extended deserializer interface that supports receiving headers during deserialization.
/// </summary>
/// <typeparam name="T">Deserialized object type</typeparam>
public interface INatsDeserializeWithHeaders<out T> : INatsDeserialize<T>
{
    /// <summary>
    /// Deserialize value from buffer with headers.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
    /// <returns>Deserialized object</returns>
    T? Deserialize(in ReadOnlySequence<byte> buffer, INatsHeaders? headers);
}

/// <summary>
/// Registry for serializers and deserializers.
/// </summary>
public interface INatsSerializerRegistry
{
    INatsSerialize<T> GetSerializer<T>();

    INatsDeserialize<T> GetDeserializer<T>();
}

/// <summary>
/// Extension methods to support header-aware serialization with fallback to standard serialization.
/// </summary>
public static class NatsSerializationExtensions
{
    /// <summary>
    /// Serializes the value with header support, falling back to standard serialization if headers are not supported.
    /// </summary>
    public static void Serialize<T>(this INatsSerialize<T> serializer, IBufferWriter<byte> bufferWriter, T value, INatsHeaders? headers)
    {
        if (headers != null && serializer is INatsSerializeWithHeaders<T> withHeaders)
        {
            withHeaders.Serialize(bufferWriter, value, headers);
            return;
        }

        serializer.Serialize(bufferWriter, value);
    }

    /// <summary>
    /// Deserializes the value with header support, falling back to standard deserialization if headers are not supported.
    /// </summary>
    public static T? Deserialize<T>(this INatsDeserialize<T> deserializer, in ReadOnlySequence<byte> buffer, INatsHeaders? headers)
    {
        if (deserializer is INatsDeserializeWithHeaders<T> withHeaders)
            return withHeaders.Deserialize(buffer, headers);

        return deserializer.Deserialize(buffer);
    }
}

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
    [Obsolete("Use Serialize(IBufferWriter<byte>, T, INatsHeaders?) overload instead.")]
    void Serialize(IBufferWriter<byte> bufferWriter, T value);

    /// <summary>
    /// Serialize value to buffer.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
#if NET8_0_OR_GREATER
    void Serialize(IBufferWriter<byte> bufferWriter, T value, INatsHeaders? headers)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        Serialize(bufferWriter, value);
#pragma warning restore CS0618
    }
#else
    void Serialize(IBufferWriter<byte> bufferWriter, T value, INatsHeaders? headers);
#endif
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
    [Obsolete("Use Deserialize(in ReadOnlySequence<byte>, INatsHeaders?) overload instead.")]
    T? Deserialize(in ReadOnlySequence<byte> buffer);

    /// <summary>
    /// Deserialize value from buffer.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
#if NET8_0_OR_GREATER
    T? Deserialize(in ReadOnlySequence<byte> buffer, INatsHeaders? headers)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return Deserialize(buffer);
#pragma warning restore CS0618
    }
#else
    T? Deserialize(in ReadOnlySequence<byte> buffer, INatsHeaders? headers);
#endif
}

public interface INatsSerializerRegistry
{
    INatsSerialize<T> GetSerializer<T>();

    INatsDeserialize<T> GetDeserializer<T>();
}

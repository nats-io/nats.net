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
#if NET8_0_OR_GREATER
    void Serialize(IBufferWriter<byte> bufferWriter, T value)
    {
        if (!NatsSerializeGuard.TryEnter())
        {
            throw new NotImplementedException("You must implement either Serialize(IBufferWriter<byte>, T) or Serialize(IBufferWriter<byte>, T, INatsHeaders?).");
        }

        try
        {
            Serialize(bufferWriter, value, null);
        }
        finally
        {
            NatsSerializeGuard.Exit();
        }
    }
#else
    void Serialize(IBufferWriter<byte> bufferWriter, T value);
#endif

    /// <summary>
    /// Serialize value to buffer.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
#if NET8_0_OR_GREATER
    void Serialize(IBufferWriter<byte> bufferWriter, T value, INatsHeaders? headers)
    {
        if (!NatsSerializeGuard.TryEnter())
        {
            throw new NotImplementedException("You must implement either Serialize(IBufferWriter<byte>, T) or Serialize(IBufferWriter<byte>, T, INatsHeaders?).");
        }

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Serialize(bufferWriter, value);
#pragma warning restore CS0618
        }
        finally
        {
            NatsSerializeGuard.Exit();
        }
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
#if NET8_0_OR_GREATER
    T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (!NatsDeserializeGuard.TryEnter())
        {
            throw new NotImplementedException("You must implement either Deserialize(in ReadOnlySequence<byte>) or Deserialize(in ReadOnlySequence<byte>, INatsHeaders?).");
        }

        try
        {
            return Deserialize(buffer, null);
        }
        finally
        {
            NatsDeserializeGuard.Exit();
        }
    }
#else
    T? Deserialize(in ReadOnlySequence<byte> buffer);
#endif

    /// <summary>
    /// Deserialize value from buffer.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    /// <param name="headers">Optional NATS headers associated with the message.</param>
#if NET8_0_OR_GREATER
    T? Deserialize(in ReadOnlySequence<byte> buffer, INatsHeaders? headers)
    {
        if (!NatsDeserializeGuard.TryEnter())
        {
            throw new NotImplementedException("You must implement either Deserialize(in ReadOnlySequence<byte>) or Deserialize(in ReadOnlySequence<byte>, INatsHeaders?).");
        }

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Deserialize(buffer);
#pragma warning restore CS0618
        }
        finally
        {
            NatsDeserializeGuard.Exit();
        }
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

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
    /// <returns>Deserialized object</returns>
    T? Deserialize(in ReadOnlySequence<byte> buffer);
}

public interface INatsSerializerRegistry
{
    INatsSerialize<T> GetSerializer<T>();

    INatsDeserialize<T> GetDeserializer<T>();
}

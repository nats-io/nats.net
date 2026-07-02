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

/// <summary>
/// Extended serializer interface with access to message context during serialization.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public interface INatsSerializeWithContext<in T> : INatsSerialize<T>
{
    /// <summary>
    /// Serialize value to buffer with message context.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    /// <param name="context">Message envelope metadata.</param>
    void Serialize(IBufferWriter<byte> bufferWriter, T value, in NatsMsgContext context);
}

/// <summary>
/// Extended deserializer interface with access to message context during deserialization.
/// </summary>
/// <typeparam name="T">Deserialized object type</typeparam>
public interface INatsDeserializeWithContext<out T> : INatsDeserialize<T>
{
    /// <summary>
    /// Deserialize value from buffer with message context.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    /// <param name="context">Message envelope metadata.</param>
    /// <returns>Deserialized object</returns>
    T? Deserialize(in ReadOnlySequence<byte> buffer, in NatsMsgContext context);
}

/// <summary>
/// Combined context-aware serializer interface that supports both serialization and deserialization
/// with access to message context.
/// </summary>
/// <typeparam name="T">Object type</typeparam>
public interface INatsSerializerWithContext<T> : INatsSerializeWithContext<T>, INatsDeserializeWithContext<T>
{
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
/// Extension methods to support context-aware serialization with fallback to standard serialization.
/// </summary>
/// <remarks>
/// Context-aware serializers that want to mutate <see cref="NatsMsgContext.Headers"/> must check
/// for null before doing so: the library passes whatever headers the caller supplied to
/// <c>PublishAsync</c>, and does not allocate a new <see cref="INatsHeaders"/> for serializers that
/// opt in to context. Callers who want header-mutating behavior must pass a non-null
/// headers instance to <c>PublishAsync</c>.
/// </remarks>
public static class NatsSerializationExtensions
{
    /// <summary>
    /// Serializes the value with message context, falling back to standard serialization if not supported.
    /// </summary>
    public static void Serialize<T>(this INatsSerialize<T> serializer, IBufferWriter<byte> bufferWriter, T value, in NatsMsgContext context)
    {
        if (serializer is INatsSerializeWithContext<T> withContext)
        {
            withContext.Serialize(bufferWriter, value, in context);
            return;
        }

        serializer.Serialize(bufferWriter, value);
    }

    /// <summary>
    /// Deserializes the value with message context, falling back to standard deserialization if not supported.
    /// </summary>
    public static T? Deserialize<T>(this INatsDeserialize<T> deserializer, in ReadOnlySequence<byte> buffer, in NatsMsgContext context)
    {
        if (deserializer is INatsDeserializeWithContext<T> withContext)
            return withContext.Deserialize(buffer, in context);

        return deserializer.Deserialize(buffer);
    }
}

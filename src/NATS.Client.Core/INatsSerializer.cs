using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NATS.Client.Core;

/// <summary>
/// Serializer interface for NATS messages.
/// </summary>
public interface INatsSerializer
{
    /// <summary>
    /// Next serializer in the chain.
    /// </summary>
    /// <remarks>
    /// If the serializer can't handle the type, it may call the next serializer in the chain
    /// or throw an exception if there are no more serializers to use when <c>Next</c> is set to <c>null</c>.
    /// </remarks>
    public INatsSerializer? Next { get; }

    /// <summary>
    /// Serialize value to buffer.
    /// </summary>
    /// <param name="bufferWriter">Buffer to write the serialized data.</param>
    /// <param name="value">Object to be serialized.</param>
    /// <typeparam name="T">Serialized object type</typeparam>
    void Serialize<T>(IBufferWriter<byte> bufferWriter, T value);

    /// <summary>
    /// Deserialize value from buffer.
    /// </summary>
    /// <param name="buffer">Buffer with the serialized data.</param>
    /// <typeparam name="T">Serialized object type.</typeparam>
    /// <returns>Deserialized object</returns>
    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

/// <summary>
/// Default serializer for NATS messages.
/// </summary>
public static class NatsDefaultSerializer
{
    /// <summary>
    /// Combined serializer of <see cref="NatsRawSerializer"/> and <see cref="NatsUtf8PrimitivesSerializer"/> set
    /// as the default serializer for NATS messages.
    /// </summary>
    public static readonly INatsSerializer Default = new NatsRawSerializer(new NatsUtf8PrimitivesSerializer(default));
}

/// <summary>
/// UTF8 serializer for strings and numbers (<c>int</c> and <c>double</c>).
/// </summary>
public class NatsUtf8PrimitivesSerializer : INatsSerializer
{
    /// <summary>
    /// Creates a new instance of <see cref="NatsUtf8PrimitivesSerializer"/>.
    /// </summary>
    /// <param name="next">The next serializer in chain.</param>
    public NatsUtf8PrimitivesSerializer(INatsSerializer? next) => Next = next;

    /// <inheritdoc />
    public INatsSerializer? Next { get; }

    /// <inheritdoc />
    public void Serialize<T>(IBufferWriter<byte> bufferWriter, T value)
    {
        if (value is string str)
        {
            var count = Encoding.UTF8.GetByteCount(str);
            var buffer = bufferWriter.GetSpan(count);
            var bytes = Encoding.UTF8.GetBytes(str, buffer);
            bufferWriter.Advance(bytes);
            return;
        }

        var span = bufferWriter.GetSpan(128);

        // int
        {
            if (value is int input)
            {
                if (Utf8Formatter.TryFormat(input, span, out var written))
                {
                    bufferWriter.Advance(written);
                }
                else
                {
                    throw new NatsException($"Can't serialize {typeof(T)}, format error");
                }

                return;
            }
        }

        // double
        {
            if (value is double input)
            {
                if (Utf8Formatter.TryFormat(input, span, out var written))
                {
                    bufferWriter.Advance(written);
                }
                else
                {
                    throw new NatsException($"Can't serialize {typeof(T)}, format error");
                }

                return;
            }
        }

        if (Next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        Next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(string))
        {
            return (T)(object)Encoding.UTF8.GetString(buffer);
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            if (buffer.IsSingleSegment)
            {
                if (Utf8Parser.TryParse(buffer.FirstSpan, out int value, out _))
                {
                    return (T)(object)value;
                }
                else
                {
                    throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
                }
            }
            else
            {
                var bytes = buffer.ToArray();
                if (Utf8Parser.TryParse(bytes, out int value, out _))
                {
                    return (T)(object)value;
                }
                else
                {
                    throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
                }
            }
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            if (buffer.IsSingleSegment)
            {
                if (Utf8Parser.TryParse(buffer.FirstSpan, out double value, out _))
                {
                    return (T)(object)value;
                }
                else
                {
                    throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
                }
            }
            else
            {
                var bytes = buffer.ToArray();
                if (Utf8Parser.TryParse(bytes, out double value, out _))
                {
                    return (T)(object)value;
                }
                else
                {
                    throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
                }
            }
        }

        if (Next == null)
        {
            throw new NatsException($"Can't deserialize {typeof(T)}");
        }

        return Next.Deserialize<T>(buffer);
    }
}

/// <summary>
/// Serializer for binary data.
/// </summary>
public class NatsRawSerializer : INatsSerializer
{
    /// <summary>
    /// Creates a new instance of <see cref="NatsRawSerializer"/>.
    /// </summary>
    /// <param name="next">Next serializer in chain.</param>
    public NatsRawSerializer(INatsSerializer? next) => Next = next;

    /// <inheritdoc />
    public INatsSerializer? Next { get; }

    /// <inheritdoc />
    public void Serialize<T>(IBufferWriter<byte> bufferWriter, T? value)
    {
        if (value is byte[] bytes)
        {
            bufferWriter.Write(bytes);
            return;
        }

        if (value is Memory<byte> memory)
        {
            bufferWriter.Write(memory.Span);
            return;
        }

        if (value is ReadOnlyMemory<byte> readOnlyMemory)
        {
            bufferWriter.Write(readOnlyMemory.Span);
            return;
        }

        if (value is ReadOnlySequence<byte> readOnlySequence)
        {
            if (readOnlySequence.IsSingleSegment)
            {
                bufferWriter.Write(readOnlySequence.FirstSpan);
            }
            else
            {
                foreach (var source in readOnlySequence)
                {
                    bufferWriter.Write(source.Span);
                }
            }

            return;
        }

        if (value is IMemoryOwner<byte> memoryOwner)
        {
            using (memoryOwner)
            {
                var length = memoryOwner.Memory.Length;

                var buffer = bufferWriter.GetMemory(length);
                memoryOwner.Memory.CopyTo(buffer);

                bufferWriter.Advance(length);

                return;
            }
        }

        if (Next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        Next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(byte[]))
        {
            return (T)(object)buffer.ToArray();
        }

        if (typeof(T) == typeof(Memory<byte>))
        {
            return (T)(object)new Memory<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            return (T)(object)new ReadOnlyMemory<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(ReadOnlySequence<byte>))
        {
            return (T)(object)new ReadOnlySequence<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(IMemoryOwner<byte>) || typeof(T) == typeof(NatsMemoryOwner<byte>))
        {
            var memoryOwner = NatsMemoryOwner<byte>.Allocate((int)buffer.Length);
            buffer.CopyTo(memoryOwner.Memory.Span);
            return (T)(object)memoryOwner;
        }

        if (Next == null)
        {
            throw new NatsException($"Can't deserialize {typeof(T)}");
        }

        return Next.Deserialize<T>(buffer);
    }
}

/// <summary>
/// Serializer with support for <see cref="JsonSerializerContext"/>.
/// </summary>
public sealed class NatsJsonContextSerializer : INatsSerializer
{
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerContext _context;

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonContextSerializer"/>.
    /// </summary>
    /// <param name="context">Context to use for serialization.</param>
    /// <param name="next">Next serializer in chain.</param>
    public NatsJsonContextSerializer(JsonSerializerContext context, INatsSerializer? next = default)
    {
        Next = next;
        _context = context;
    }

    /// <inheritdoc />
    public INatsSerializer? Next { get; }

    /// <inheritdoc />
    public void Serialize<T>(IBufferWriter<byte> bufferWriter, T value)
    {
        if (_context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> jsonTypeInfo)
        {
            Utf8JsonWriter writer;
            if (_jsonWriter == null)
            {
                writer = _jsonWriter = new Utf8JsonWriter(bufferWriter, JsonWriterOpts);
            }
            else
            {
                writer = _jsonWriter;
                writer.Reset(bufferWriter);
            }

            JsonSerializer.Serialize(writer, value, jsonTypeInfo);

            writer.Reset(NullBufferWriter.Instance);
            return;
        }

        if (Next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        Next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (_context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> jsonTypeInfo)
        {
            var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
            return JsonSerializer.Deserialize(ref reader, jsonTypeInfo);
        }

        if (Next != null)
            return Next.Deserialize<T>(buffer);

        throw new NatsException($"Can't deserialize {typeof(T)}");
    }
}

internal sealed class NullBufferWriter : IBufferWriter<byte>
{
    internal static readonly IBufferWriter<byte> Instance = new NullBufferWriter();

    public void Advance(int count)
    {
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => Array.Empty<byte>();

    public Span<byte> GetSpan(int sizeHint = 0) => Array.Empty<byte>();
}

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
/// UTF8 serializer for strings and all the primitives.
/// </summary>
/// <remarks>
/// Supported types are <c>string</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>Guid</c>,
/// <c>TimeSpan</c>, <c>bool</c>, <c>byte</c>, <c>decimal</c>, <c>double</c>, <c>float</c>,
/// <c>int</c>, <c>long</c>, <c>sbyte</c>, <c>short</c>, <c>uint</c> and <c>ulong</c>.
/// </remarks>
public class NatsUtf8PrimitivesSerializer : INatsSerializer
{
    private readonly INatsSerializer? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsUtf8PrimitivesSerializer"/>.
    /// </summary>
    /// <param name="next">The next serializer in chain.</param>
    public NatsUtf8PrimitivesSerializer(INatsSerializer? next) => _next = next;

    /// <inheritdoc />
    public void Serialize<T>(IBufferWriter<byte> bufferWriter, T value)
    {
        if (value is string str)
        {
            Encoding.UTF8.GetBytes(str, bufferWriter);
            return;
        }

        var span = bufferWriter.GetSpan(128);

        // DateTime
        {
            if (value is DateTime input)
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

        // DateTimeOffset
        {
            if (value is DateTimeOffset input)
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

        // Guid
        {
            if (value is Guid input)
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

        // TimeSpan
        {
            if (value is TimeSpan input)
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

        // bool
        {
            if (value is bool input)
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

        // byte
        {
            if (value is byte input)
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

        // decimal
        {
            if (value is decimal input)
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

        // float
        {
            if (value is float input)
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

        // long
        {
            if (value is long input)
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

        // sbyte
        {
            if (value is sbyte input)
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

        // short
        {
            if (value is short input)
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

        // uint
        {
            if (value is uint input)
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

        // ulong
        {
            if (value is ulong input)
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

        if (_next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        _next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(string))
        {
            return (T)(object)Encoding.UTF8.GetString(buffer);
        }

        var span = buffer.IsSingleSegment ? buffer.FirstSpan : buffer.ToArray();

        if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
        {
            if (Utf8Parser.TryParse(span, out DateTime value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(DateTimeOffset?))
        {
            if (Utf8Parser.TryParse(span, out DateTimeOffset value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
        {
            if (Utf8Parser.TryParse(span, out Guid value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(TimeSpan) || typeof(T) == typeof(TimeSpan?))
        {
            if (Utf8Parser.TryParse(span, out TimeSpan value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
        {
            if (Utf8Parser.TryParse(span, out bool value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
        {
            if (Utf8Parser.TryParse(span, out byte value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
        {
            if (Utf8Parser.TryParse(span, out decimal value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            if (Utf8Parser.TryParse(span, out double value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
        {
            if (Utf8Parser.TryParse(span, out float value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            if (Utf8Parser.TryParse(span, out int value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            if (Utf8Parser.TryParse(span, out long value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
        {
            if (Utf8Parser.TryParse(span, out sbyte value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
        {
            if (Utf8Parser.TryParse(span, out short value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
        {
            if (Utf8Parser.TryParse(span, out uint value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
        {
            if (Utf8Parser.TryParse(span, out ulong value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (_next == null)
        {
            throw new NatsException($"Can't deserialize {typeof(T)}");
        }

        return _next.Deserialize<T>(buffer);
    }
}

/// <summary>
/// Serializer for binary data.
/// </summary>
public class NatsRawSerializer : INatsSerializer
{
    private readonly INatsSerializer? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsRawSerializer"/>.
    /// </summary>
    /// <param name="next">Next serializer in chain.</param>
    public NatsRawSerializer(INatsSerializer? next) => _next = next;

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

        if (_next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        _next.Serialize(bufferWriter, value);
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

        if (_next == null)
        {
            throw new NatsException($"Can't deserialize {typeof(T)}");
        }

        return _next.Deserialize<T>(buffer);
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
    private readonly INatsSerializer? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonContextSerializer"/>.
    /// </summary>
    /// <param name="context">Context to use for serialization.</param>
    /// <param name="next">Next serializer in chain.</param>
    public NatsJsonContextSerializer(JsonSerializerContext context, INatsSerializer? next = default)
    {
        _context = context;
        _next = next;
    }

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

        if (_next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        _next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (_context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> jsonTypeInfo)
        {
            var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
            return JsonSerializer.Deserialize(ref reader, jsonTypeInfo);
        }

        if (_next != null)
            return _next.Deserialize<T>(buffer);

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

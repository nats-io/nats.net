using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

/// <summary>
/// Serializer interface for NATS messages.
/// </summary>
/// <typeparam name="T">Serialized object type</typeparam>
public interface INatsSerializer<T> : INatsSerialize<T>, INatsDeserialize<T>
{
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

/// <summary>
/// Default serializer for NATS messages.
/// </summary>
public static class NatsDefaultSerializer<T>
{
    /// <summary>
    /// Combined serializer of <see cref="NatsRawSerializer{T}"/> and <see cref="NatsUtf8PrimitivesSerializer{T}"/> set
    /// as the default serializer for NATS messages.
    /// </summary>
    public static readonly INatsSerializer<T> Default = NatsRawSerializer<T>.Default;
}

public class NatsDefaultSerializerRegistry : INatsSerializerRegistry
{
    public static readonly NatsDefaultSerializerRegistry Default = new();

    public INatsSerialize<T> GetSerializer<T>() => NatsDefaultSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => NatsDefaultSerializer<T>.Default;
}

public class NatsSerializerBuilder<T>
{
    private readonly List<INatsSerializer<T>> _serializers = new();

    public NatsSerializerBuilder<T> Add(INatsSerializer<T> serializer)
    {
        _serializers.Add(serializer);
        return this;
    }

    public INatsSerializer<T> Build()
    {
        if (_serializers.Count == 0)
        {
            return NatsDefaultSerializer<T>.Default;
        }

        for (var i = _serializers.Count - 1; i > 0; i--)
        {
            _serializers[i - 1] = _serializers[i - 1].CombineWith(_serializers[i]);
        }

        return _serializers[0];
    }
}

/// <summary>
/// UTF8 serializer for strings and all the primitives.
/// </summary>
/// <remarks>
/// Supported types are <c>string</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>Guid</c>,
/// <c>TimeSpan</c>, <c>bool</c>, <c>byte</c>, <c>decimal</c>, <c>double</c>, <c>float</c>,
/// <c>int</c>, <c>long</c>, <c>sbyte</c>, <c>short</c>, <c>uint</c> and <c>ulong</c>.
/// </remarks>
public class NatsUtf8PrimitivesSerializer<T> : INatsSerializer<T>
{
    public static readonly NatsUtf8PrimitivesSerializer<T> Default = new(default);

    private readonly INatsSerializer<T>? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsUtf8PrimitivesSerializer{T}"/>.
    /// </summary>
    /// <param name="next">The next serializer in chain.</param>
    public NatsUtf8PrimitivesSerializer(INatsSerializer<T>? next = default) => _next = next;

    public INatsSerializer<T> CombineWith(INatsSerializer<T>? next) => new NatsUtf8PrimitivesSerializer<T>(next);

    /// <inheritdoc />
    public void Serialize(IBufferWriter<byte> bufferWriter, T value)
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
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(string))
        {
            if (buffer.Length == 0)
                return default;
            return (T)(object)Encoding.UTF8.GetString(buffer);
        }

        var span = buffer.IsSingleSegment ? buffer.GetFirstSpan() : buffer.ToArray();

        if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out DateTime value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(DateTimeOffset?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out DateTimeOffset value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out Guid value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(TimeSpan) || typeof(T) == typeof(TimeSpan?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out TimeSpan value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out bool value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out byte value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out decimal value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out double value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out float value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out int value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out long value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out sbyte value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out short value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
        {
            if (buffer.Length == 0)
                return default;

            if (Utf8Parser.TryParse(span, out uint value, out _))
            {
                return (T)(object)value;
            }

            throw new NatsException($"Can't deserialize {typeof(T)}. Parsing error");
        }

        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
        {
            if (buffer.Length == 0)
                return default;

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

        return _next.Deserialize(buffer);
    }
}

/// <summary>
/// Serializer for binary data.
/// </summary>
public class NatsRawSerializer<T> : INatsSerializer<T>
{
    public static readonly NatsRawSerializer<T> Default = new(NatsUtf8PrimitivesSerializer<T>.Default);

    private readonly INatsSerializer<T>? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsRawSerializer{T}"/>.
    /// </summary>
    /// <param name="next">Next serializer in chain.</param>
    public NatsRawSerializer(INatsSerializer<T>? next = default) => _next = next;

    public INatsSerializer<T> CombineWith(INatsSerializer<T>? next) => new NatsRawSerializer<T>(next);

    /// <inheritdoc />
    public void Serialize(IBufferWriter<byte> bufferWriter, T value)
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
                bufferWriter.Write(readOnlySequence.GetFirstSpan());
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
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(byte[]))
        {
            if (buffer.Length == 0)
                return default;
            return (T)(object)buffer.ToArray();
        }

        if (typeof(T) == typeof(Memory<byte>))
        {
            if (buffer.Length == 0)
                return default;
            return (T)(object)new Memory<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            if (buffer.Length == 0)
                return default;
            return (T)(object)new ReadOnlyMemory<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(ReadOnlySequence<byte>))
        {
            if (buffer.Length == 0)
                return default;
            return (T)(object)new ReadOnlySequence<byte>(buffer.ToArray());
        }

        if (typeof(T) == typeof(IMemoryOwner<byte>) || typeof(T) == typeof(NatsMemoryOwner<byte>))
        {
            if (buffer.Length == 0)
                return default;
            var memoryOwner = NatsMemoryOwner<byte>.Allocate((int)buffer.Length);
            buffer.CopyTo(memoryOwner.Memory.Span);
            return (T)(object)memoryOwner;
        }

        if (_next == null)
        {
            throw new NatsException($"Can't deserialize {typeof(T)}");
        }

        return _next.Deserialize(buffer);
    }
}

public sealed class NatsJsonContextSerializerRegistry : INatsSerializerRegistry
{
    private readonly JsonSerializerContext[] _contexts;

    public NatsJsonContextSerializerRegistry(params JsonSerializerContext[] contexts) => _contexts = contexts;

    public INatsSerialize<T> GetSerializer<T>() => new NatsJsonContextSerializer<T>(_contexts);

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsJsonContextSerializer<T>(_contexts);
}

/// <summary>
/// Serializer with support for <see cref="JsonSerializerContext"/>.
/// </summary>
public sealed class NatsJsonContextSerializer<T> : INatsSerializer<T>
{
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerContext[] _contexts;
    private readonly INatsSerializer<T>? _next;

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonContextSerializer{T}"/>.
    /// </summary>
    /// <param name="contexts">Context to use for serialization.</param>
    /// <param name="next">Next serializer in chain.</param>
    public NatsJsonContextSerializer(JsonSerializerContext[] contexts, INatsSerializer<T>? next = default)
    {
        _contexts = contexts;
        _next = next;
    }

    public NatsJsonContextSerializer(JsonSerializerContext context, INatsSerializer<T>? next = default)
        : this(new[] { context }, next)
    {
    }

    public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => new NatsJsonContextSerializer<T>(_contexts, next);

    /// <inheritdoc />
    public void Serialize(IBufferWriter<byte> bufferWriter, T value)
    {
        foreach (var context in _contexts)
        {
            if (context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> jsonTypeInfo)
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
        }

        if (_next == null)
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }

        _next.Serialize(bufferWriter, value);
    }

    /// <inheritdoc />
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return default;
        }

        foreach (var context in _contexts)
        {
            if (context.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> jsonTypeInfo)
            {
                var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
                return JsonSerializer.Deserialize(ref reader, jsonTypeInfo);
            }
        }

        if (_next != null)
            return _next.Deserialize(buffer);

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

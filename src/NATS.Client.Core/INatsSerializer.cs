using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NATS.Client.Core;

public interface INatsSerializer
{
    public INatsSerializer? Next { get; }

    int Serialize<T>(IBufferWriter<byte> bufferWriter, T value);

    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

public static class NatsDefaultSerializer
{
    public static readonly INatsSerializer Default = new NatsRawSerializer(NatsJsonSerializer.Default);
}

public class NatsRawSerializer : INatsSerializer
{
    public NatsRawSerializer(INatsSerializer? next) => Next = next;

    public INatsSerializer? Next { get; }

    public int Serialize<T>(IBufferWriter<byte> bufferWriter, T? value)
    {
        if (value is byte[] bytes)
        {
            bufferWriter.Write(bytes);
            return bytes.Length;
        }

        if (value is Memory<byte> memory)
        {
            bufferWriter.Write(memory.Span);
            return memory.Length;
        }

        if (value is ReadOnlyMemory<byte> readOnlyMemory)
        {
            bufferWriter.Write(readOnlyMemory.Span);
            return readOnlyMemory.Length;
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

            return (int)readOnlySequence.Length;
        }

        if (value is IMemoryOwner<byte> memoryOwner)
        {
            using (memoryOwner)
            {
                var length = memoryOwner.Memory.Length;

                var buffer = bufferWriter.GetMemory(length);
                memoryOwner.Memory.CopyTo(buffer);

                bufferWriter.Advance(length);

                return length;
            }
        }

        if (Next != null)
            return Next.Serialize(bufferWriter, value);

        throw new NatsException($"Can't serialize {typeof(T)}");
    }

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

        if (Next != null)
            return Next.Deserialize<T>(buffer);

        throw new NatsException($"Can't deserialize {typeof(T)}");
    }
}

public sealed class NatsJsonSerializer : INatsSerializer
{
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerOptions _opts;

    public NatsJsonSerializer(JsonSerializerOptions opts) => _opts = opts;

    public static NatsJsonSerializer Default { get; } =
        new(new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, });

    public INatsSerializer? Next => default;

    public int Serialize<T>(IBufferWriter<byte> bufferWriter, T? value)
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

        JsonSerializer.Serialize(writer, value, _opts);

        var bytesCommitted = (int)writer.BytesCommitted;
        writer.Reset(NullBufferWriter.Instance);
        return bytesCommitted;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize<T>(ref reader, _opts);
    }
}

public sealed class NatsJsonContextSerializer : INatsSerializer
{
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerContext _context;

    public NatsJsonContextSerializer(JsonSerializerContext context, INatsSerializer? next = default)
    {
        Next = next;
        _context = context;
    }

    public INatsSerializer? Next { get; }

    public int Serialize<T>(IBufferWriter<byte> bufferWriter, T value)
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

            var bytesCommitted = (int)writer.BytesCommitted;
            writer.Reset(NullBufferWriter.Instance);
            return bytesCommitted;
        }

        if (Next != null)
            return Next.Serialize(bufferWriter, value);

        throw new NatsException($"Can't serialize {typeof(T)}");
    }

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

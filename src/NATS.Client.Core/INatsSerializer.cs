using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NATS.Client.Core;

public interface INatsSerializer
{
    public INatsSerializer? Next { get; }

    int Serialize<T>(ICountableBufferWriter bufferWriter, T? value);

    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

public interface ICountableBufferWriter : IBufferWriter<byte>
{
    int WrittenCount { get; }
}

public static class NatsDefaultSerializer
{
    public static readonly INatsSerializer Default = new NatsRawSerializer(NatsJsonSerializer.Default);
}

public class NatsRawSerializer : INatsSerializer
{
    public NatsRawSerializer(INatsSerializer? next) => Next = next;

    public INatsSerializer? Next { get; }

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
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
                bufferWriter.Write(memoryOwner.Memory.Span);
                return memoryOwner.Memory.Length;
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
    private static readonly JsonWriterOptions JsonWriterOpts = new JsonWriterOptions { Indented = false, SkipValidation = true, };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerOptions _opts;

    public NatsJsonSerializer(JsonSerializerOptions opts) => _opts = opts;

    public static NatsJsonSerializer Default { get; } =
        new(new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, });

    public INatsSerializer? Next => default;

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
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

    private sealed class NullBufferWriter : IBufferWriter<byte>
    {
        internal static readonly IBufferWriter<byte> Instance = new NullBufferWriter();

        public void Advance(int count)
        {
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => Array.Empty<byte>();

        public Span<byte> GetSpan(int sizeHint = 0) => Array.Empty<byte>();
    }
}

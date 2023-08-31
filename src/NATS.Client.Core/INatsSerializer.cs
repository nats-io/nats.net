using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NATS.Client.Core;

public interface INatsSerializer
{
    int Serialize<T>(ICountableBufferWriter bufferWriter, T? value);

    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);

    object? Deserialize(in ReadOnlySequence<byte> buffer, Type type);
}

public interface ICountableBufferWriter : IBufferWriter<byte>
{
    int WrittenCount { get; }
}

public sealed class NatsJsonSerializer : INatsSerializer
{
    private static readonly JsonWriterOptions JsonWriterOpts = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true,
    };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerOptions _opts;

    public NatsJsonSerializer(JsonSerializerOptions opts) => _opts = opts;

    public static NatsJsonSerializer Default { get; } =
        new(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

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

    public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type)
    {
        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize(ref reader, type, _opts);
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

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NATS.Client.Core;

public interface INatsSerializer
{
    int Serialize<T>(ICountableBufferWriter bufferWriter, T? value);

    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

public interface ICountableBufferWriter : IBufferWriter<byte>
{
    int WrittenCount { get; }
}

public sealed class JsonNatsSerializer : INatsSerializer
{
    private static readonly JsonWriterOptions JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true,
    };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerOptions _options;

    public JsonNatsSerializer(JsonSerializerOptions options) => _options = options;

    public static JsonNatsSerializer Default { get; } =
        new(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        Utf8JsonWriter writer;
        if (_jsonWriter == null)
        {
            writer = _jsonWriter = new Utf8JsonWriter(bufferWriter, JsonWriterOptions);
        }
        else
        {
            writer = _jsonWriter;
            writer.Reset(bufferWriter);
        }

        JsonSerializer.Serialize(writer, value, _options);

        var bytesCommitted = (int)writer.BytesCommitted;
        writer.Reset(NullBufferWriter.Instance);
        return bytesCommitted;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize<T>(ref reader, _options);
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

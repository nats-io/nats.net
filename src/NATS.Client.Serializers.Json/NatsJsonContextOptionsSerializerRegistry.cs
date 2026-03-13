using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace NATS.Client.Serializers.Json;

public class NatsJsonContextOptionsSerializerRegistry(JsonSerializerOptions options) : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => new NatsJsonOptionsSerializer<T>(options);

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsJsonOptionsSerializer<T>(options);
}

public class NatsJsonOptionsSerializer<T>(JsonSerializerOptions options) : INatsSerializer<T>
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true };

    // ReSharper disable once StaticMemberInGenericType
    [ThreadStatic]
    private static Utf8JsonWriter? jsonWriter;

    public void Serialize(IBufferWriter<byte> bufferWriter, T value)
    {
        Utf8JsonWriter writer;
        if (jsonWriter == null)
        {
            writer = jsonWriter = new Utf8JsonWriter(bufferWriter, JsonWriterOpts);
        }
        else
        {
            writer = jsonWriter;
            writer.Reset(bufferWriter);
        }

        JsonSerializer.Serialize(writer, value, options);

        writer.Reset(NullBufferWriter.Instance);
    }

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return default;
        }

        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    public INatsSerializer<T> CombineWith(INatsSerializer<T> next)
    {
        throw new NotSupportedException();
    }

    private sealed class NullBufferWriter : IBufferWriter<byte>
    {
        public static readonly IBufferWriter<byte> Instance = new NullBufferWriter();

        public void Advance(int count)
        {
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => Array.Empty<byte>();

        public Span<byte> GetSpan(int sizeHint = 0) => [];
    }
}

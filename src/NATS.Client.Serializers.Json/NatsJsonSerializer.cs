using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;

namespace NATS.Client.Serializers.Json;

/// <summary>
/// Reflection based JSON serializer for NATS.
/// </summary>
/// <remarks>
/// This serializer is not suitable for native AOT deployments since it might rely on reflection
/// </remarks>
public sealed class NatsJsonSerializer<T> : INatsSerializer<T>
{
    private static readonly JsonWriterOptions JsonWriterOpts = new() { Indented = false, SkipValidation = true, };

    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private readonly JsonSerializerOptions _opts;

    public NatsJsonSerializer()
        : this(new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonSerializer{T}"/> with the specified options.
    /// </summary>
    /// <param name="opts">Serialization options</param>
    public NatsJsonSerializer(JsonSerializerOptions opts) => _opts = opts;

    /// <summary>
    /// Default instance of <see cref="NatsJsonSerializer{T}"/> with option set to ignore <c>null</c> values when writing.
    /// </summary>
    public static NatsJsonSerializer<T> Default { get; } = new();

    public INatsSerializer<T> CombineWith(INatsSerializer<T> next)
        => throw new NotSupportedException();

    /// <inheritdoc />flush
    public void Serialize(IBufferWriter<byte> bufferWriter, T? value)
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

        writer.Reset(NullBufferWriter.Instance);
    }

    /// <inheritdoc />
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return default;
        }

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

public sealed class NatsJsonSerializerRegistry : INatsSerializerRegistry
{
    public static readonly NatsJsonSerializerRegistry Default = new();

    public INatsSerialize<T> GetSerializer<T>() => NatsJsonSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => NatsJsonSerializer<T>.Default;
}

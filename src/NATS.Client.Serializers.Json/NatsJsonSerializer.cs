using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace NATS.Client.Serializers.Json;

/// <summary>
/// Reflection based JSON serializer for NATS.
/// </summary>
/// <remarks>
/// A new <see cref="Utf8JsonWriter"/> is allocated on every serialization.
/// This serializer is not suitable for native AOT deployments since it might rely on reflection.
/// </remarks>
public sealed class NatsJsonSerializer<T> : INatsSerializer<T>
{
    private readonly JsonSerializerOptions _opts;
    private readonly JsonWriterOptions _writerOpts;

    /// <summary>
    /// Reflection-based JSON serializer for NATS.
    /// A shared <see cref="Utf8JsonWriter"/> is reused and reset on every serialization.
    /// </summary>
    /// <remarks>
    /// This serializer is not suitable for native AOT deployments since it might rely on reflection
    /// </remarks>
    public NatsJsonSerializer()
#if NET6_0
        : this(new JsonSerializerOptions())
#else
        : this(JsonSerializerOptions.Default)
#endif
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonSerializer{T}"/> with the specified options.
    /// </summary>
    /// <param name="opts">Serialization options</param>
    public NatsJsonSerializer(JsonSerializerOptions opts)
        : this(opts, new JsonWriterOptions { Encoder = opts.Encoder, Indented = false, SkipValidation = true })
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="NatsJsonSerializer{T}"/> with the specified options and writer options.
    /// </summary>
    /// <param name="opts">Serialization options</param>
    /// <param name="writerOpts">Writer options</param>
    public NatsJsonSerializer(JsonSerializerOptions opts, JsonWriterOptions writerOpts)
    {
        _opts = opts;
        _writerOpts = writerOpts;
    }

    /// <summary>
    /// Default instance of <see cref="NatsJsonSerializer{T}"/> with option set to ignore <c>null</c> values when writing.
    /// </summary>
    public static NatsJsonSerializer<T> Default { get; } = new();

    /// <inheritdoc />
    public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Serialize(IBufferWriter<byte> bufferWriter, T? value)
    {
        using var writer = new Utf8JsonWriter(bufferWriter, _writerOpts);

        JsonSerializer.Serialize(writer, value, _opts);
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
}

public sealed class NatsJsonSerializerRegistry : INatsSerializerRegistry
{
    public static readonly NatsJsonSerializerRegistry Default = new();

    public INatsSerialize<T> GetSerializer<T>() => NatsJsonSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => NatsJsonSerializer<T>.Default;
}

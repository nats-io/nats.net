#if NET8_0_OR_GREATER
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using NATS.Net;

namespace MicroBenchmark;

[MemoryDiagnoser]
public class SerializerBench
{
    private readonly ArrayBufferWriter<byte> _bufferWriter = new();

    private readonly Payload _payload = new() { Id = 42, Name = "hello", Tag = "bench" };
    private readonly byte[] _bytes = new byte[64];

    private readonly INatsSerialize<Payload> _json = new NatsJsonSerializer<Payload>(new JsonSerializerOptions());
    private readonly INatsSerialize<Payload> _jsonOptions = new NatsJsonOptionsSerializer<Payload>(new JsonSerializerOptions());
    private readonly INatsSerialize<Payload> _jsonContext = new NatsJsonContextSerializer<Payload>(BenchJsonContext.Default);
    private readonly INatsSerialize<Payload> _clientDefault = NatsClientDefaultSerializer<Payload>.Default;
    private readonly INatsSerialize<byte[]> _raw = new NatsRawSerializer<byte[]>();
    private readonly INatsSerialize<string> _utf8Primitives = new NatsUtf8PrimitivesSerializer<string>();

    [Benchmark(Baseline = true)]
    public int JsonReflection() => Serialize(_json, _payload);

    [Benchmark]
    public int JsonReflectionOptions() => Serialize(_jsonOptions, _payload);

    [Benchmark]
    public int JsonSourceGenerated() => Serialize(_jsonContext, _payload);

    [Benchmark]
    public int ClientDefaultChain() => Serialize(_clientDefault, _payload);

    [Benchmark]
    public int Raw() => Serialize(_raw, _bytes);

    [Benchmark]
    public int Utf8Primitives() => Serialize(_utf8Primitives, "hello");

    private int Serialize<T>(INatsSerialize<T> serializer, T value)
    {
        _bufferWriter.Clear();
        serializer.Serialize(_bufferWriter, value);
        return _bufferWriter.WrittenCount;
    }

    public class Payload
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Tag { get; set; }
    }
}

[JsonSerializable(typeof(SerializerBench.Payload))]
internal partial class BenchJsonContext : JsonSerializerContext;
#endif

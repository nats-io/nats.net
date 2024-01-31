using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class PublishParallelBench
{
    public const int TotalMsgs = 1_000_000;

    private static readonly PublishParallelObj Data = new PublishParallelObj
    {
        String = new('a', 32),
        Bool = true,
        Int = 42,
        Dictionary = new Dictionary<string, string>
        {
            { new('a', 32), new('a', 32) },
            { new('b', 32), new('b', 32) },
            { new('c', 32), new('c', 32) },
        },
        List = new List<string>
        {
            new('a', 32),
            new('b', 32),
            new('c', 32),
        },
    };

    private NatsConnection _nats;

    [Params(1, 2, 4)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var registry = new NatsJsonContextSerializerRegistry(PublishParallelJsonContext.Default);
        _nats = new NatsConnection(NatsOpts.Default with { SerializerRegistry = registry });
        await _nats.ConnectAsync();
    }

    [Benchmark]
    public async Task PublishParallelAsync()
    {
        var tasks = new List<Task>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < TotalMsgs / Concurrency; j++)
                {
                    await _nats.PublishAsync("test", Data);
                }
            }));
        }

        await Task.WhenAll(tasks);
        await _nats.PingAsync();
    }
}

internal record PublishParallelObj
{
    [JsonPropertyName("string")]
    public string String { get; set; }

    [JsonPropertyName("bool")]
    public bool Bool { get; set; }

    [JsonPropertyName("int")]
    public int Int { get; set; }

    [JsonPropertyName("dictionary")]
    public Dictionary<string, string> Dictionary { get; set; }

    [JsonPropertyName("list")]
    public List<string> List { get; set; }
}

[JsonSerializable(typeof(PublishParallelObj))]
internal partial class PublishParallelJsonContext : JsonSerializerContext
{
}

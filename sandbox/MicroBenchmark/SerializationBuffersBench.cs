using System.Buffers;
using BenchmarkDotNet.Attributes;
using NATS.Client.Core;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class SerializationBuffersBench
{
    private static readonly string Data = new('0', 126);

    private NatsConnection _nats;

    // [Params(8, 16, 32, 64, 512, 1024)]
    // [Params(8, 64)]
    [Params(64, 512, 1024)]
    public int Iter { get; set; }

    [GlobalSetup]
    public async ValueTask Setup()
    {
        _nats = new NatsConnection(new NatsOpts
        {
            Url = Environment.GetEnvironmentVariable("NATS_URL") ?? "127.0.0.1", ObjectPoolSize = 1024,
        });
        //
        // await _nats.ConnectAsync();
        //
        // var pool = ArrayPool<byte>.Shared;
        // for (var i = 0; i < 9000; i++)
        //     pool.Return(pool.Rent(i));

        // var t = 0.0;
        // for (var i = 0; i < 9000; i++)
        // {
        //     var b = ArrayPool<byte>.Shared.Rent(i);
        //     Random.Shared.NextBytes(b);
        //     t += b.Average(b => b);
        //     ArrayPool<byte>.Shared.Return(b);
        // }
        // Console.WriteLine(t);

    }

    [Benchmark]
    public async ValueTask<TimeSpan> PublishAsync()
    {
        for (var i = 0; i < Iter; i++)
        {
            await _nats.PublishAsync("foo", Data);
        }

        return await _nats.PingAsync();
    }

    // [Benchmark]
    // public async ValueTask PublishNoPingAsync()
    // {
    //     for (var i = 0; i < Iter; i++)
    //     {
    //         await _nats.PublishAsync("foo", Data);
    //     }
    // }
    //
    // [Benchmark]
    // public async ValueTask<TimeSpan> PingAsync()
    // {
    //     var t = TimeSpan.Zero;
    //
    //     for (var i = 0; i < Iter; i++)
    //     {
    //         t += await _nats.PingAsync();
    //     }
    //
    //     return t;
    // }
}

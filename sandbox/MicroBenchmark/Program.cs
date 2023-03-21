#pragma warning disable IDE0044

using NATS.Client.Core;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZLogger;

var config = ManualConfig.CreateMinimumViable()
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(DefaultExporters.Plain)
    .AddJob(Job.ShortRun);

BenchmarkDotNet.Running.BenchmarkRunner.Run<DefaultRun>(config, args);

public struct MyVector3
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

// var run = new DefaultRun();
// await run.SetupAsync();
// await run.RunBenchmark();
// await run.RunStackExchangeRedis();

// await run.CleanupAsync();
public class DefaultRun
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private NatsConnection _connection;
    private NatsKey _key;
    private ConnectionMultiplexer _redis;
    private object _gate;
    private Handler _handler;
    private IDisposable _subscription = default!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var provider = new ServiceCollection()
           .AddLogging(x =>
           {
               x.ClearProviders();
               x.SetMinimumLevel(LogLevel.Information);
               x.AddZLoggerConsole();
           })
           .BuildServiceProvider();

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ILogger<DefaultRun>>();
        var options = NatsOptions.Default with
        {
            LoggerFactory = loggerFactory,
            Echo = true,
            Verbose = false,
        };

        _connection = new NATS.Client.Core.NatsConnection(options);
        _key = new NatsKey("foobar");
        await _connection.ConnectAsync();
        _gate = new object();
        _redis = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");

        _handler = new Handler();

        // subscription = connection.Subscribe<MyVector3>(key, handler.Handle);
    }

    // [Benchmark]
    public async Task Nop()
    {
        await Task.Yield();
    }

    [Benchmark]
    public async Task PublishAsync()
    {
        for (int i = 0; i < 1; i++)
        {
            await _connection.PublishAsync(_key, default(MyVector3));
        }
    }

    // [Benchmark]
    public async Task PublishAsyncRedis()
    {
        for (int i = 0; i < 1; i++)
        {
            await _redis.GetDatabase().PublishAsync(_key.Key, JsonSerializer.Serialize(default(MyVector3)));
        }
    }

    // [Benchmark]
    public void RunBenchmark()
    {
        const int count = 10000;
        _handler.Gate = _gate;
        _handler.Called = 0;
        _handler.Max = count;

        for (int i = 0; i < count; i++)
        {
            _connection.PostPublish(_key, default(MyVector3));
        }

        lock (_gate)
        {
            // Monitor.Wait(gate);
            Thread.Sleep(1000);
        }
    }

    // [Benchmark]
    // public async Task RunStackExchangeRedis()
    // {
    //    var tcs = new TaskCompletionSource();
    //    var called = 0;
    //    redis.GetSubscriber().Subscribe(key.Key, (channel, v) =>
    //    {
    //        if (Interlocked.Increment(ref called) == 1000)
    //        {
    //            tcs.TrySetResult();
    //        }
    //    });

    // for (int i = 0; i < 1000; i++)
    //    {
    //        _ = redis.GetDatabase().PublishAsync(key.Key, JsonSerializer.Serialize(new MyVector3()), StackExchange.Redis.CommandFlags.FireAndForget);
    //    }

    // await tcs.Task;
    // }
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _subscription?.Dispose();
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        _redis?.Dispose();
    }

    private class Handler
    {
        public int Called;
        public int Max;
        public object Gate;

        public void Handle(MyVector3 vec)
        {
            if (Interlocked.Increment(ref Called) == Max)
            {
                lock (Gate)
                {
                    Monitor.PulseAll(Gate);
                }
            }
        }
    }
}

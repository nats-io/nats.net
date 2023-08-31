#pragma warning disable IDE0044

using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;
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
    private string _subject;
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
        var options = NatsOpts.Default with
        {
            LoggerFactory = loggerFactory,
            Echo = true,
            Verbose = false,
        };

        _connection = new NATS.Client.Core.NatsConnection(options);
        _subject = "foobar";
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
        for (var i = 0; i < 1; i++)
        {
            await _connection.PublishAsync(_subject, default(MyVector3));
        }
    }

    // [Benchmark]
    public async Task PublishAsyncRedis()
    {
        for (var i = 0; i < 1; i++)
        {
            await _redis.GetDatabase().PublishAsync(_subject, JsonSerializer.Serialize(default(MyVector3)));
        }
    }

    // [Benchmark]
    public void RunBenchmark()
    {
        const int count = 10000;
        _handler.Gate = _gate;
        _handler.Called = 0;
        _handler.Max = count;

        for (var i = 0; i < count; i++)
        {
            _connection.PublishAsync(_subject, default(MyVector3));
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
#pragma warning disable SA1401
        public int Called;
        public int Max;
        public object Gate;
#pragma warning restore SA1401

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

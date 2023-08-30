using System.Diagnostics;
using System.Runtime;
using System.Text;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client;
using NATS.Client.Core;
using NatsBenchmark;
using ZLogger;

var isPortableThreadPool = await IsRunOnPortableThreadPoolAsync();
Console.WriteLine($"RunOnPortableThreadPool:{isPortableThreadPool}");
Console.WriteLine(new { GCSettings.IsServerGC, GCSettings.LatencyMode });

ThreadPool.SetMinThreads(1000, 1000);

// var p = PublishCommand<Vector3>.Create(key, new Vector3(), serializer);
// (p as ICommand).Return();
try
{
    // use only pubsub suite
    new Benchmark(args);
}
catch (Exception e)
{
    Console.WriteLine("Error: " + e.Message);
    Console.WriteLine(e);
}

// COMPlus_ThreadPool_UsePortableThreadPool=0 -> false
static Task<bool> IsRunOnPortableThreadPoolAsync()
{
    var tcs = new TaskCompletionSource<bool>();
    ThreadPool.QueueUserWorkItem(_ =>
    {
        var st = new StackTrace().ToString();
        tcs.TrySetResult(st.Contains("PortableThreadPool"));
    });
    return tcs.Task;
}

namespace NatsBenchmark
{
    public partial class Benchmark
    {
        private void RunPubSubBenchmark(string testName, long testCount, long testSize, bool disableShow = false)
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
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < testCount; i++)
            {
                pubConn.PublishAsync(_subject, payload);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, testSize);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private void RunPubSubBenchmarkBatch(string testName, long testCount, long testSize, bool disableShow = false)
        {
            var provider = new ServiceCollection()
                .AddLogging(x =>
                {
                    x.ClearProviders();
                    x.SetMinimumLevel(LogLevel.Trace);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            var data = Enumerable.Range(0, 1000)
                .Select(x => (_subject, payload))
                .ToArray();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();

            var to = testCount / data.Length;
            for (var i = 0; i < to; i++)
            {
                // pubConn.PostPublishBatch(data!);
                foreach (var (subject, bytes) in data)
                {
                    pubConn.PublishAsync(subject, bytes);
                }
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, testSize);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private void ProfilingRunPubSubBenchmarkAsync(string testName, long testCount, long testSize, bool disableShow = false)
        {
            var provider = new ServiceCollection()
                .AddLogging(x =>
                {
                    x.ClearProviders();
                    x.SetMinimumLevel(LogLevel.Trace);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
            JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
            JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before");
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < testCount; i++)
            {
                pubConn.PublishAsync(_subject, payload);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();
            JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Finished");

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, testSize);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private void RunPubSubBenchmarkBatchRaw(string testName, long testCount, long testSize, int batchSize = 1000, bool disableShow = false)
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
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            var command = new NATS.Client.Core.Commands.DirectWriteCommand(BuildCommand(testSize), batchSize);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();

            var to = testCount / batchSize;
            for (var i = 0; i < to; i++)
            {
                pubConn.PostDirectWrite(command);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, testSize);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private string BuildCommand(long testSize)
        {
            var sb = new StringBuilder();
            sb.Append("PUB ");
            sb.Append(_subject);
            sb.Append(" ");
            sb.Append(testSize);
            if (testSize > 0)
            {
                sb.AppendLine();
                for (var i = 0; i < testSize; i++)
                {
                    sb.Append('a');
                }
            }

            return sb.ToString();
        }

        private void RunPubSubBenchmarkPubSub2(string testName, long testCount, long testSize, bool disableShow = false)
        {
            var provider = new ServiceCollection()
                .AddLogging(x =>
                {
                    x.ClearProviders();
                    x.SetMinimumLevel(LogLevel.Trace);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var pubSubLock2 = new object();
            var finished = false;
            var finished2 = false;
            var subCount = 0;
            var subCount2 = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);
            var pubConn2 = new NATS.Client.Core.NatsConnection(options);
            var subConn2 = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();
            pubConn2.ConnectAsync().AsTask().Wait();
            subConn2.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });
            var d2 = subConn2.SubscribeAsync(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount2);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount2 == testCount)
                {
                    lock (pubSubLock2)
                    {
                        finished2 = true;
                        Monitor.Pulse(pubSubLock2);
                    }
                }
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();
            var sw2 = Stopwatch.StartNew();
            var publishCount = testCount / 2;
            for (var i = 0; i < publishCount; i++)
            {
                pubConn.PublishAsync(_subject, payload);
                pubConn2.PublishAsync(_subject, payload);
            }

            var t1 = Task.Run(() =>
            {
                lock (pubSubLock)
                {
                    if (!finished)
                    {
                        Monitor.Wait(pubSubLock);
                        sw.Stop();
                    }
                }
            });

            var t2 = Task.Run(() =>
            {
                lock (pubSubLock2)
                {
                    if (!finished2)
                    {
                        Monitor.Wait(pubSubLock2);
                        sw2.Stop();
                    }
                }
            });

            Task.WaitAll(t1, t2);

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, testSize);
                PrintResults(testName, sw2, testCount, testSize);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private void RunPubSubBenchmarkVector3(string testName, long testCount, bool disableShow = false)
        {
            var provider = new ServiceCollection()
                .AddLogging(x =>
                {
                    x.ClearProviders();
                    x.SetMinimumLevel(LogLevel.Trace);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOpts.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                Echo = false,
                Verbose = false,
            };

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            // byte[] payload = generatePayload(testSize);
            var pubConn = new NATS.Client.Core.NatsConnection(options);
            var subConn = new NATS.Client.Core.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var d = subConn.SubscribeAsync<Vector3>(_subject).AsTask().Result.Register(_ =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here:{0}", subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;

                        // JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After");
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            MessagePackSerializer.Serialize(default(Vector3));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();

            // JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
            // JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
            // JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before");
            for (var i = 0; i < testCount; i++)
            {
                pubConn.PublishAsync(_subject, default(Vector3));
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            if (!disableShow)
            {
                PrintResults(testName, sw, testCount, 16);
            }

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        private void RunPubSubVector3(string testName, long testCount)
        {
            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            // byte[] payload = generatePayload(testSize);
            var cf = new ConnectionFactory();

            var o = ConnectionFactory.GetDefaultOptions();
            o.ClosedEventHandler = (_, __) => { };
            o.DisconnectedEventHandler = (_, __) => { };

            o.Url = _url;
            o.SubChannelLength = 10000000;
            if (_creds != null)
            {
                o.SetUserCredentials(_creds);
            }

            o.AsyncErrorEventHandler += (sender, obj) =>
            {
                Console.WriteLine("Error: " + obj.Error);
            };

            var subConn = cf.CreateConnection(o);
            var pubConn = cf.CreateConnection(o);

            var s = subConn.SubscribeAsync(_subject, (sender, args) =>
            {
                MessagePackSerializer.Deserialize<Vector3>(args.Message.Data);

                subCount++;
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });
            s.SetPendingLimits(10000000, 1000000000);
            subConn.Flush();

            MessagePackSerializer.Serialize(default(Vector3));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < testCount; i++)
            {
                pubConn.Publish(_subject, MessagePackSerializer.Serialize(default(Vector3)));
            }

            pubConn.Flush();

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            PrintResults(testName, sw, testCount, 16);

            pubConn.Close();
            subConn.Close();
        }

        private void RunPubSubRedis(string testName, long testCount, long testSize)
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
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();

            var pubSubLock = new object();
            var finished = false;
            var subCount = 0;

            var payload = GeneratePayload(testSize);

            var pubConn = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");
            var subConn = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");

            subConn.GetSubscriber().Subscribe(_subject, (channel, v) =>
            {
                Interlocked.Increment(ref subCount);

                // logger.LogInformation("here?:" + subCount);
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            var sw = Stopwatch.StartNew();

            for (var i = 0; i < testCount; i++)
            {
                _ = pubConn.GetDatabase().PublishAsync(_subject, payload, StackExchange.Redis.CommandFlags.FireAndForget);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);
            }

            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            Console.WriteLine("COMPLETE");

            pubConn.Dispose();
            subConn.Dispose();
        }

        private void RunSuite()
        {
            // RunPubSubBenchmark("Benchmark8b", 10000000, 8, disableShow: true);
            // RunPubSubBenchmarkVector3("BenchmarkV3", 10000000, disableShow: true);

            // ProfilingRunPubSubBenchmarkAsync("BenchmarkProfiling", 10000000, 0);

            //
            // RunPubSubBenchmarkBatchRaw("Benchmark", 10000000, 8, disableShow: true); // warmup
            // RunPubSubBenchmarkBatchRaw("Benchmark", 500000, 1024 * 4, disableShow: true); // warmup
            // RunPubSubBenchmarkBatchRaw("Benchmark", 100000, 1024 * 8, disableShow: true); // warmup
            // RunPubSubBenchmarkBatchRaw("Benchmark8b_Opt", 10000000, 8);
            // RunPubSubBenchmarkBatchRaw("Benchmark4k_Opt", 500000, 1024 * 4);
            RunPubSubBenchmarkBatchRaw("Benchmark8k_Opt", 100000, 1024 * 8, batchSize: 10, disableShow: true);
            RunPubSubBenchmarkBatchRaw("Benchmark8k_Opt", 100000, 1024 * 8, batchSize: 10);
            RunPubSubBenchmark("Benchmark8k", 100000, 1024 * 8, disableShow: true);
            RunPubSubBenchmark("Benchmark8k", 100000, 1024 * 8);

            // RunPubSubBenchmark("Benchmark8b", 10000000, 8, disableShow: true);
            // RunPubSubBenchmark("Benchmark8b", 10000000, 8);

            // runPubSubVector3("PubSubVector3", 10000000);
            // runPubSub("PubSubNo", 10000000, 0);
            // runPubSub("PubSub8b", 10000000, 8);

            // runPubSub("PubSub8b", 10000000, 8);

            // runPubSub("PubSub32b", 10000000, 32);
            // runPubSub("PubSub100b", 10000000, 100);
            // runPubSub("PubSub256b", 10000000, 256);
            // runPubSub("PubSub512b", 500000, 512);
            // runPubSub("PubSub1k", 500000, 1024);
            // runPubSub("PubSub4k", 500000, 1024 * 4);
            RunPubSub("PubSub8k", 100000, 1024 * 8);

            // RunPubSubBenchmarkVector3("BenchmarkV3", 10000000);
            // RunPubSubBenchmark("BenchmarkNo", 10000000, 0);
            // RunPubSubBenchmark("Benchmark8b", 10000000, 8);
            // RunPubSubBenchmarkBatch("Benchmark8bBatch", 10000000, 8);
            // RunPubSubBenchmarkPubSub2("Benchmark8b 2", 10000000, 8);

            // RunPubSubBenchmark("Benchmark32b", 10000000, 32);
            // RunPubSubBenchmark("Benchmark100b", 10000000, 100);
            // RunPubSubBenchmark("Benchmark256b", 10000000, 256);
            // RunPubSubBenchmark("Benchmark512b", 500000, 512);
            // RunPubSubBenchmark("Benchmark1k", 500000, 1024);
            // RunPubSubBenchmark("Benchmark4k", 500000, 1024 * 4);
            // RunPubSubBenchmark("Benchmark8k", 100000, 1024 * 8);

            // Redis?
            // RunPubSubRedis("StackExchange.Redis", 10000000, 8);
            // RunPubSubRedis("Redis 100", 10000000, 100);

            // These run significantly slower.
            // req->server->reply->server->req
            // runReqReply("ReqReplNo", 20000, 0);
            // runReqReply("ReqRepl8b", 10000, 8);
            // runReqReply("ReqRepl32b", 10000, 32);
            // runReqReply("ReqRepl256b", 5000, 256);
            // runReqReply("ReqRepl512b", 5000, 512);
            // runReqReply("ReqRepl1k", 5000, 1024);
            // runReqReply("ReqRepl4k", 5000, 1024 * 4);
            // runReqReply("ReqRepl8k", 5000, 1024 * 8);

            // runReqReplyAsync("ReqReplAsyncNo", 20000, 0).Wait();
            // runReqReplyAsync("ReqReplAsync8b", 10000, 8).Wait();
            // runReqReplyAsync("ReqReplAsync32b", 10000, 32).Wait();
            // runReqReplyAsync("ReqReplAsync256b", 5000, 256).Wait();
            // runReqReplyAsync("ReqReplAsync512b", 5000, 512).Wait();
            // runReqReplyAsync("ReqReplAsync1k", 5000, 1024).Wait();
            // runReqReplyAsync("ReqReplAsync4k", 5000, 1024 * 4).Wait();
            // runReqReplyAsync("ReqReplAsync8k", 5000, 1024 * 8).Wait();

            // runPubSubLatency("LatNo", 500, 0);
            // runPubSubLatency("Lat8b", 500, 8);
            // runPubSubLatency("Lat32b", 500, 32);
            // runPubSubLatency("Lat256b", 500, 256);
            // runPubSubLatency("Lat512b", 500, 512);
            // runPubSubLatency("Lat1k", 500, 1024);
            // runPubSubLatency("Lat4k", 500, 1024 * 4);
            // runPubSubLatency("Lat8k", 500, 1024 * 8);
        }
    }
}

[MessagePackObject]
public struct Vector3
{
    [Key(0)]
    public float X;
    [Key(1)]
    public float Y;
    [Key(2)]
    public float Z;
}

internal static class NatsMsgTestUtils
{
    internal static INatsSub<T>? Register<T>(this INatsSub<T>? sub, Action<NatsMsg<T?>> action)
    {
        if (sub == null)
            return null;
        Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                action(natsMsg);
            }
        });
        return sub;
    }

    internal static INatsSub? Register(this INatsSub? sub, Action<NatsMsg> action)
    {
        if (sub == null)
            return null;
        Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                action(natsMsg);
            }
        });
        return sub;
    }
}

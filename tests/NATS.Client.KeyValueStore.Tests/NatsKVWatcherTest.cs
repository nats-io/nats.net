using System.Buffers.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.KeyValueStore.Internal;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

[Collection("nats-server")]
public class NatsKVWatcherTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public NatsKVWatcherTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Watcher_reconnect_with_history()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var nats1 = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        await nats1.ConnectRetryAsync();
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);

        var bucket = $"{prefix}b1";
        var config = new NatsKVConfig(bucket) { History = 10 };

        var store1 = await kv1.CreateStoreAsync(config, cancellationToken: cancellationToken);

        var proxy = new NatsProxy(_server.Port);
        await using var nats2 = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}" });
        await nats1.ConnectRetryAsync();
        var js2 = new NatsJSContext(nats2);
        var kv2 = new NatsKVContext(js2);
        var store2 = (NatsKVStore)await kv2.CreateStoreAsync(config, cancellationToken: cancellationToken);
        var watcher = await store2.WatchInternalAsync<NatsMemoryOwner<byte>>(["k1.*"], cancellationToken: cancellationToken);

        await store1.PutAsync("k1.p1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 3, cancellationToken: cancellationToken);

        var count = 0;

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken))
        {
            using (entry.Value)
            {
                if (Utf8Parser.TryParse(entry.Value.Memory.Span, out int value, out _))
                {
                    Assert.Equal(++count, value);
                    if (value == 3)
                        break;
                }
                else
                {
                    Assert.Fail("Not a number (1)");
                }
            }
        }

        var signal = new WaitSignal();
        nats2.ConnectionDisconnected += (_, _) =>
        {
            signal.Pulse();
            return default;
        };

        proxy.Reset();

        await signal;

        // Check that default history config is deep enough
        await store1.PutAsync("k1.p1", 4, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 5, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 6, cancellationToken: cancellationToken);

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken))
        {
            if (entry.Value is { } memoryOwner)
            {
                using (memoryOwner)
                {
                    if (Utf8Parser.TryParse(memoryOwner.Memory.Span, out int value, out _))
                    {
                        Assert.Equal(++count, value);
                        if (value == 6)
                            break;
                    }
                    else
                    {
                        Assert.Fail("Not a number (2)");
                    }
                }
            }
            else
            {
                throw new Exception("Null value (2)");
            }
        }
    }

    [Fact]
    public async Task Watch_all()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = $"{prefix}b1";
        var store = await kv.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v1", cancellationToken: cancellationToken);

        var signal = new WaitSignal(timeout);
        var watchTask = Task.Run(
            async () =>
            {
                await foreach (var entry in store.WatchAsync<string>("*", cancellationToken: cancellationToken))
                {
                    signal.Pulse();
                    _output.WriteLine($"WATCH: {entry.Key} ({entry.Revision}): {entry.Value}");
                    if (entry.Value == "v3")
                        break;
                }
            },
            cancellationToken);

        await signal;

        Assert.Equal("v1", (await store.GetEntryAsync<string>("k1", cancellationToken: cancellationToken)).Value);

        await store.PutAsync("k1", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k1", "v3", cancellationToken: cancellationToken);

        Assert.Equal("v3", (await store.GetEntryAsync<string>("k1", cancellationToken: cancellationToken)).Value);

        await watchTask;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Watch_subset()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = $"{prefix}b1";
        var store = await kv.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v1", cancellationToken: cancellationToken);
        await store.PutAsync("k2", "v1", cancellationToken: cancellationToken);
        await store.PutAsync("k3", "v1", cancellationToken: cancellationToken);

        var signal = new WaitSignal(timeout);
        var watchTask = Task.Run(
            async () =>
            {
                HashSet<string> keys = new();

                // Multiple keys are only supported in NATS Server 2.10 and later
                await foreach (var entry in store.WatchAsync<string>(["k1", "k2"], cancellationToken: cancellationToken))
                {
                    signal.Pulse();
                    _output.WriteLine($"WATCH: {entry.Key} ({entry.Revision}): {entry.Value}");

                    if (entry is { Value: "end" })
                        break;

                    keys.Add(entry.Key);
                }

                Assert.Equal(["k1", "k2"], keys.OrderBy(x => x));
            },
            cancellationToken);

        await signal;

        Assert.Equal("v1", (await store.GetEntryAsync<string>("k1", cancellationToken: cancellationToken)).Value);
        Assert.Equal("v1", (await store.GetEntryAsync<string>("k2", cancellationToken: cancellationToken)).Value);
        Assert.Equal("v1", (await store.GetEntryAsync<string>("k3", cancellationToken: cancellationToken)).Value);

        await store.PutAsync("k1", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k2", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k3", "v2", cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v3", cancellationToken: cancellationToken);
        await store.PutAsync("k2", "v3", cancellationToken: cancellationToken);
        await store.PutAsync("k3", "v3", cancellationToken: cancellationToken);

        Assert.Equal("v3", (await store.GetEntryAsync<string>("k1", cancellationToken: cancellationToken)).Value);
        Assert.Equal("v3", (await store.GetEntryAsync<string>("k2", cancellationToken: cancellationToken)).Value);
        Assert.Equal("v3", (await store.GetEntryAsync<string>("k3", cancellationToken: cancellationToken)).Value);

        await store.PutAsync("k1", "end", cancellationToken: cancellationToken);
        await watchTask;
    }

    [Fact]
    public async Task Watcher_timeout_reconnect()
    {
        var timeout = TimeSpan.FromSeconds(30);

        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var nats1 = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var bucket = $"{prefix}b1";
        var store1 = await kv1.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        var signal = new WaitSignal(timeout);
        await using var nats2 = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            LoggerFactory = new InMemoryTestLoggerFactory(
                level: LogLevel.Debug,
                logger: log =>
                {
                    if (log is { Category: "NATS.Client.KeyValueStore.Internal.NatsKVWatcher", LogLevel: LogLevel.Debug })
                    {
                        if (log.EventId == NatsKVLogEvents.IdleTimeout)
                            signal.Pulse();
                    }
                }),
        });
        var proxy = new NatsProxy(_server.Port);
        var js2 = new NatsJSContext(nats2);
        var kv2 = new NatsKVContext(js2);
        var store2 = (NatsKVStore)await kv2.CreateStoreAsync(bucket, cancellationToken: cancellationToken);
        var watcher = await store2.WatchInternalAsync<NatsMemoryOwner<byte>>(["k1.*"], cancellationToken: cancellationToken);

        // Swallow heartbeats
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        await store1.PutAsync("k1.p1", 1, cancellationToken: cancellationToken);

        var e1 = await watcher.Entries.ReadAsync(cancellationToken);
        Assert.Equal(1, (int)e1.Revision);
        var count = 1;

        await store1.PutAsync("k1.p1", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 3, cancellationToken: cancellationToken);

        var consumer1 = ((NatsKVWatcher<NatsMemoryOwner<byte>>)watcher).Consumer;

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken))
        {
            using (entry.Value)
            {
                if (Utf8Parser.TryParse(entry.Value.Memory.Span, out int value, out _))
                {
                    Assert.Equal(++count, value);
                    if (value == 3)
                        break;
                }
                else
                {
                    Assert.Fail("Not a number (1)");
                }
            }
        }

        await store1.PutAsync("k1.p1", 4, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 5, cancellationToken: cancellationToken);
        await store1.PutAsync("k1.p1", 6, cancellationToken: cancellationToken);

        await Task.Delay(10_000, cancellationToken);

        await signal;

        await Retry.Until(
            reason: "consumer changed",
            condition: () => consumer1 != ((NatsKVWatcher<NatsMemoryOwner<byte>>)watcher).Consumer,
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: timeout);

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken))
        {
            if (entry.Value is { } memoryOwner)
            {
                using (memoryOwner)
                {
                    if (Utf8Parser.TryParse(memoryOwner.Memory.Span, out int value, out _))
                    {
                        Assert.Equal(++count, value);
                        if (value == 6)
                            break;
                    }
                    else
                    {
                        Assert.Fail("Not a number (2)");
                    }
                }
            }
            else
            {
                throw new Exception("Null value (2)");
            }
        }
    }

    [Fact]
    public async Task Watch_push_consumer_flow_control()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var timeout = TimeSpan.FromSeconds(60);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        var bucket = $"{prefix}b1";
        var store = await kv.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        // with large number of entries we'd receive the flow control messages
        const int max = 50_000;
        for (var i = 0; i < max; i++)
        {
            await store.PutAsync($"k{i}", i, cancellationToken: cancellationToken);
        }

        HashSet<string> keys = new();
        var count = 0;
        await foreach (var entry in store.WatchAsync<int>(cancellationToken: cancellationToken))
        {
            Assert.True(keys.Add(entry.Key));
            if (++count == max)
                break;
        }

        Assert.Equal(max, count);
        Assert.Equal(max, keys.Count);
    }

    [Fact]
    public async Task Watch_empty_bucket_for_end_of_data()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = $"{prefix}b1";
        var store = await kv.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        var signal = new WaitSignal(timeout);
        var endOfDataHit = false;
        var watchTask = Task.Run(
            async () =>
            {
                var opts = new NatsKVWatchOpts
                {
                    OnNoData = async (cancellationToken) =>
                    {
                        await Task.CompletedTask;
                        endOfDataHit = true;
                        signal.Pulse();
                        return true;
                    },
                };

                await foreach (var entry in store.WatchAsync<string>("*", opts: opts, cancellationToken: cancellationToken))
                {
                }
            },
            cancellationToken);

        await signal;

        Assert.True(endOfDataHit, "End of Current Data not set");

        await watchTask;
    }

    [Fact]
    public async Task Serialization_errors()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var store = await kv.CreateStoreAsync($"{prefix}b1", cancellationToken: cts.Token);

        await store.PutAsync($"k1", "not an int", cancellationToken: cts.Token);

        await foreach (var entry in store.WatchAsync<int>(cancellationToken: cts.Token))
        {
            Assert.NotNull(entry.Error);
            Assert.IsType<NatsDeserializeException>(entry.Error);
            Assert.Equal("Exception during deserialization", entry.Error.Message);
            Assert.Contains("Can't deserialize System.Int32", entry.Error.InnerException!.Message);
            Assert.Throws<NatsDeserializeException>(() => entry.EnsureSuccess());
            break;
        }
    }

    [Fact]
    public async Task Watch_with_empty_filter()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var store = await kv.CreateStoreAsync($"{prefix}b1", cancellationToken: cts.Token);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var unused in store.WatchAsync<int>(keys: Array.Empty<string>(), cancellationToken: cts.Token))
            {
            }
        });
    }

    [SkipIfNatsServer(versionLaterThan: "2.9.999")]
    public async Task Watch_with_multiple_filter_on_old_server()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var store = await kv.CreateStoreAsync($"{prefix}b1", cancellationToken: cts.Token);

        // If we try to watch with multiple keys on an old server, we will have to
        // let the request go through since there is no way to know the server version
        // as the server hosting JetStream is not necessarily be the server we're connected to.
        var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
        {
            await foreach (var unused in store.WatchAsync<int>(keys: ["1", "2"], cancellationToken: cts.Token))
            {
            }
        });

        Assert.Equal(400, exception.Error.Code);
        Assert.Equal(10094, exception.Error.ErrCode);
        Assert.Equal("consumer delivery policy is deliver last per subject, but optional filter subject is not set", exception.Error.Description);
    }

    // Test that watch can resume from a specific revision
    [Fact]
    public async Task Watch_resume_at_revision()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var bucket = $"{prefix}Watch_resume_at_revision";
        var config = new NatsKVConfig(bucket) { History = 10 };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);
        var store = await kv.CreateStoreAsync(config, cancellationToken: cancellationToken);

        await store.PutAsync("k1", 1, cancellationToken: cancellationToken);
        await store.PutAsync("k2", 2, cancellationToken: cancellationToken);
        var revK3 = await store.PutAsync("k3", 3, cancellationToken: cancellationToken);
        await store.PutAsync("k4", 3, cancellationToken: cancellationToken);

        // Watch all
        var watchOps = new NatsKVWatchOpts() { MetaOnly = true, };
        var watchAll = store.WatchAsync<int>(opts: watchOps, cancellationToken: cancellationToken);

        // Expect to see k1, k2, k3 and k4
        var allEntries = new List<(ulong Revision, string key)>();
        await foreach (var key in watchAll)
        {
            allEntries.Add((key.Revision, key.Key));
            if (key.Delta == 0)
            {
                break;
            }
        }

        // Expects k1, k2, k3 and k4
        allEntries.Should().HaveCount(4);

        // Watch from the revision of k3
        var watchOpsFromRevK3 = watchOps with { ResumeAtRevision = revK3, };

        var watchFromRevision = store.WatchAsync<int>(opts: watchOpsFromRevK3, cancellationToken: cancellationToken);

        // Expect to see k2 and k3, and k4
        var fromRevisionEntries = new List<(ulong Revision, string key)>();
        await foreach (var key in watchFromRevision)
        {
            fromRevisionEntries.Add((key.Revision, key.Key));
            if (key.Delta == 0)
            {
                break;
            }
        }

        // Expects k2, k3 and k4
        fromRevisionEntries.Should().HaveCount(2);

        // Watch from none existing revision
        var noData = false;
        var watchOpsNoneExisting = watchOps with
        {
            ResumeAtRevision = 9999,
            OnNoData = (_) =>
            {
                noData = true;
                return ValueTask.FromResult(true);
            },
        };

        var watchFromNoneExistingRevision =
            store.WatchAsync<int>(opts: watchOpsNoneExisting, cancellationToken: cancellationToken);

        // Expect to see no data
        await foreach (var key in watchFromNoneExistingRevision)
        {
            // We should not see any entries, if we get here something is wrong
            Assert.Fail("Should not return any entries, and OnNoData should have been called to bail out");
        }

        noData.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_watch_options()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var bucket = prefix + nameof(Validate_watch_options);
        var config = new NatsKVConfig(bucket) { History = 10 };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);
        var store = await kv.CreateStoreAsync(config, cancellationToken: cancellationToken);

        for (var i = 0; i < 10; i++)
        {
            await store.PutAsync("x", i, cancellationToken: cancellationToken);
        }

        // Valid options
        foreach (var opts in new[]
                 {
                     new NatsKVWatchOpts { IncludeHistory = false, UpdatesOnly = false, ResumeAtRevision = 5 },
                     new NatsKVWatchOpts { IncludeHistory = true, UpdatesOnly = false, ResumeAtRevision = 0 },
                     new NatsKVWatchOpts { IncludeHistory = false, UpdatesOnly = true, ResumeAtRevision = 0 },
                 })
        {
            var count = 0;
            var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (opts.UpdatesOnly)
            {
                cts2.Cancel();
                count++;
            }

            try
            {
                await foreach (var entry in store.WatchAsync<int>([">"], opts: opts, cancellationToken: cts2.Token))
                {
                    count++;
                    _output.WriteLine($"entry: {entry.Key} ({entry.Revision}): {entry.Value}");
                    if (entry.Value == 9)
                        break;
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            count.Should().BeGreaterThan(0);
        }

        // Invalid options
        foreach (var opts in new[]
                 {
                     new NatsKVWatchOpts { IncludeHistory = true, UpdatesOnly = false, ResumeAtRevision = 5 },
                     new NatsKVWatchOpts { IncludeHistory = true, UpdatesOnly = true, ResumeAtRevision = 5 },
                     new NatsKVWatchOpts { IncludeHistory = false, UpdatesOnly = true, ResumeAtRevision = 5 },
                     new NatsKVWatchOpts { IncludeHistory = true, UpdatesOnly = true, ResumeAtRevision = 0 },
                 })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var entry in store.WatchAsync<int>([">"], opts: opts, cancellationToken: cancellationToken))
                {
                }
            });
        }
    }

    [Fact]
    public async Task ReadAfterDelete()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync($"{prefix}b1");

        // Read all entries, should be empty.
        List<NatsKVEntry<string>> results = new();
        await foreach (var entry in store.WatchAsync<string>(">", opts: new NatsKVWatchOpts
        {
            OnNoData = (_) => ValueTask.FromResult(true),
        }))
        {
            results.Add(entry);
        }

        // Should be no results here.
        Assert.False(results.Any());

        // Add k1
        await store.PutAsync("k1", "v1");

        // Check if there, should be true
        var result1 = await store.TryGetEntryAsync<string>("k1");
        Assert.True(result1.Success);

        // Remove k1
        await store.DeleteAsync("k1");

        // Check if there, should be false
        var result = await store.TryGetEntryAsync<string>("k1");
        Assert.False(result.Success);

        // Read all entries.
        results.Clear();
        await foreach (var entry in store.WatchAsync<string>(">", opts: new NatsKVWatchOpts
        {
            OnNoData = (_) => ValueTask.FromResult(true),
        }))
        {
            results.Add(entry);
            if (entry.Delta == 0)
            {
                break;
            }
        }

        // Should be 1 entry, which is the deleted OP
        Assert.Single(results);
        Assert.Equal(NatsKVOperation.Del, results[0].Operation);

        // Watch and ignore deletes....  Really OnNoData should execute as we're excluding deletes and there should be no entries coming back, but this just times out.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancellationToken = cts.Token;

        results.Clear();

        try
        {
            await foreach (var entry in store.WatchAsync<string>(
            ">",
            opts: new NatsKVWatchOpts
            {
                IgnoreDeletes = true,
                OnNoData = (_) => ValueTask.FromResult(true),
            },
            cancellationToken: cancellationToken))
            {
                results.Add(entry);
                if (entry.Delta == 0)
                {
                    break;
                }
            }
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("Task was cancelled waiting for OnNoData");
        }

        // Should be no results here.
        Assert.False(results.Any());
    }

    [Fact]
    public async Task Watcher_cancellation_no_warn_logs()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var loggerFactory = new InMemoryTestLoggerFactory(LogLevel.Information, log =>
        {
            _output.WriteLine($"LOG:{log.LogLevel}: {log.Message}");
        });

        await using var nats1 = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            LoggerFactory = loggerFactory,
        });
        var prefix = _server.GetNextId();
        await nats1.ConnectRetryAsync();
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1, new NatsKVOpts
        {
            WatcherThrowOnCancellation = false,
        });
        var s = await kv1.CreateStoreAsync($"{prefix}b1", cancellationToken);

        var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var e in s.WatchAsync<string>(cancellationToken: cts2.Token).ConfigureAwait(false))
        {
        }

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            var kv2 = new NatsKVContext(js1, new NatsKVOpts
            {
                // WatcherThrowOnCancellation = true, // Default
            });
            var s2 = await kv2.CreateStoreAsync($"{prefix}b1", cancellationToken);
            var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await foreach (var e in s2.WatchAsync<string>(cancellationToken: cts3.Token).ConfigureAwait(false))
            {
            }
        });
    }
}

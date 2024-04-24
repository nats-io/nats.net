using System.Buffers.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.KeyValueStore.Internal;

namespace NATS.Client.KeyValueStore.Tests;

public class NatsKVWatcherTest
{
    private readonly ITestOutputHelper _output;

    public NatsKVWatcherTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Watcher_reconnect_with_history()
    {
        const string bucket = "b1";
        var config = new NatsKVConfig(bucket) { History = 10 };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats1 = server.CreateClientConnection();
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var store1 = await kv1.CreateStoreAsync(config, cancellationToken: cancellationToken);

        var (nats2, proxy) = server.CreateProxiedClientConnection();
        var js2 = new NatsJSContext(nats2);
        var kv2 = new NatsKVContext(js2);
        var store2 = (NatsKVStore)await kv2.CreateStoreAsync(config, cancellationToken: cancellationToken);
        var watcher = await store2.WatchInternalAsync<NatsMemoryOwner<byte>>("k1.*", cancellationToken: cancellationToken);

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

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = "b1";
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

    [Fact]
    public async Task Watcher_timeout_reconnect()
    {
        const string bucket = "b1";
        var timeout = TimeSpan.FromSeconds(30);

        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJSWithTrace(_output);
        await using var nats1 = server.CreateClientConnection();
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var store1 = await kv1.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        var (nats2, proxy) = server.CreateProxiedClientConnection();
        var js2 = new NatsJSContext(nats2);
        var kv2 = new NatsKVContext(js2);
        var store2 = (NatsKVStore)await kv2.CreateStoreAsync(bucket, cancellationToken: cancellationToken);
        var watcher = await store2.WatchInternalAsync<NatsMemoryOwner<byte>>("k1.*", cancellationToken: cancellationToken);

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

        var signal = new WaitSignal(timeout);
        server.OnLog += log =>
        {
            if (log is { Category: "NATS.Client.KeyValueStore.Internal.NatsKVWatcher", LogLevel: LogLevel.Debug })
            {
                if (log.EventId == NatsKVLogEvents.IdleTimeout)
                    signal.Pulse();
            }
        };

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
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var timeout = TimeSpan.FromSeconds(60);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        var bucket = "b1";
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

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = "b1";
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
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var store = await kv.CreateStoreAsync("b1", cancellationToken: cts.Token);

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

    // Test that watch can resume from a specific revision
    [Fact]
    public async Task Watch_resume_at_revision()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        const string bucket = "Watch_resume_at_revision";
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
}

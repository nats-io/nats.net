using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore.Tests;

public class KeyValueStoreTest
{
    private readonly ITestOutputHelper _output;

    public KeyValueStoreTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Simple_create_put_get_test()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        await store.PutAsync("k1", "v1");

        var entry = await store.GetEntryAsync<string>("k1");
        _output.WriteLine($"got:{entry}");
        Assert.True(entry.UsedDirectGet);
        Assert.Equal("v1", entry.Value);
    }

    [Fact]
    public async Task Handle_non_direct_gets()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        const string bucket = "b1";

        // You can't create a 'non-direct' KV store using the API
        // We're using the stream API directly to create a stream
        // that doesn't allow direct gets.
        await js.CreateStreamAsync(new StreamConfiguration
        {
            Name = $"KV_{bucket}",
            Subjects = new[] { $"$KV.{bucket}.>" },
            AllowDirect = false, // this property makes the switch
            Discard = StreamConfigurationDiscard.@new,
            DenyDelete = true,
            DenyPurge = false,
            NumReplicas = 1,
            MaxMsgsPerSubject = 10,
            Retention = StreamConfigurationRetention.limits,
            DuplicateWindow = 120000000000,
        });

        var store = await kv.GetStoreAsync("b1");

        await store.PutAsync("k1", "v1");

        var entry = await store.GetEntryAsync<string>("k1");
        _output.WriteLine($"got:{entry}");
        Assert.False(entry.UsedDirectGet);
        Assert.Equal("v1", entry.Value);
    }

    [Fact]
    public async Task Watch()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10_0000));
        var cancellationToken = cts.Token;

        // await using var server = NatsServer.StartJS();
        // await using var nats = server.CreateClientConnection();
        await using var nats = new NatsConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var bucket = "b1";
        var store = await kv.CreateStoreAsync(bucket, cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v1", cancellationToken: cancellationToken);

        var signal = new WaitSignal();
        var watchTask = Task.Run(async () =>
        {
            await foreach (var entry in store.WatchAll<string>("*", cancellationToken))
            {
                signal.Pulse();
                _output.WriteLine($"WATCH: {entry.Key} ({entry.Revision}): {entry.Value}");
                if (entry.Value == "v3")
                    break;
            }
        });

        await signal;

        await store.PutAsync("k1", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k1", "v3", cancellationToken: cancellationToken);

        await watchTask;
    }
}

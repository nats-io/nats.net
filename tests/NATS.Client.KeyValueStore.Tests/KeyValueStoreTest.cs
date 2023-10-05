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
        var entry = await store.GetEntryAsync<string>("k1", cancellationToken: cancellationToken);

        _output.WriteLine($"Get: {entry.Value}");

        var inbox = js.NewInbox();
        await using var sub = await nats.SubscribeAsync<string>(inbox, cancellationToken: cancellationToken);

        var consumer = await js.CreateConsumerAsync(
            new ConsumerCreateRequest
            {
                StreamName = $"KV_{bucket}",
                Config = new ConsumerConfiguration
                {
                    AckPolicy = ConsumerConfigurationAckPolicy.none,
                    DeliverPolicy = ConsumerConfigurationDeliverPolicy.@new,
                    DeliverSubject = inbox,
                    Description = "KV watch consumer",
                    FilterSubject = $"$KV.{bucket}.*",
                    FlowControl = true,
                    IdleHeartbeat = TimeSpan.FromSeconds(5).ToNanos(),
                    InactiveThreshold = TimeSpan.FromSeconds(30).ToNanos(),
                    MaxDeliver = 1,
                    MemStorage = true,
                    NumReplicas = 1,
                    ReplayPolicy = ConsumerConfigurationReplayPolicy.instant,
                },
            },
            cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v2", cancellationToken: cancellationToken);

        var msg = await sub.Msgs.ReadAsync(cancellationToken);
        _output.WriteLine($"WATCH: {msg.Subject}: {msg.Data}");

    }
}

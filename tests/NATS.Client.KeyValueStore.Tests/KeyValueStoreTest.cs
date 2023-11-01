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
    public async Task Get_keys()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("kv1", cancellationToken: cancellationToken);

        const int total = 100;

        for (var i = 0; i < total; i++)
        {
            await store.PutAsync($"k{i}", i, cancellationToken: cancellationToken);
        }

        for (var i = 0; i < total; i++)
        {
            var entry1 = await store.GetEntryAsync<int>($"k{i}", cancellationToken: cancellationToken);
            Assert.Equal(i, entry1.Value);
        }

        var count = 0;
        await foreach (var key in store.GetKeysAsync(cancellationToken: cancellationToken))
        {
            Assert.Equal($"k{count}", key);
            count++;
        }

        Assert.Equal(total, count);
    }

    [Fact]
    public async Task Get_key_revisions()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10000));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        var seq1 = await store.PutAsync($"k1", "v1-1", cancellationToken: cancellationToken);
        var seq2 = await store.PutAsync($"k2", "v2-1", cancellationToken: cancellationToken);
        var seq3 = await store.PutAsync($"k1", "v1-2", cancellationToken: cancellationToken);
        var seq4 = await store.PutAsync($"k2", "v2-2", cancellationToken: cancellationToken);
        var seq5 = await store.PutAsync($"k1", "v1-3", cancellationToken: cancellationToken);
        var seq6 = await store.PutAsync($"k2", "v2-3", cancellationToken: cancellationToken);

        // k1
        {
            var entry = await store.GetEntryAsync<string>($"k1", cancellationToken: cancellationToken);
            var entry1 = await store.GetEntryAsync<string>($"k1", revision: seq1, cancellationToken: cancellationToken);
            var entry2 = await store.GetEntryAsync<string>($"k1", revision: seq3, cancellationToken: cancellationToken);
            var entry3 = await store.GetEntryAsync<string>($"k1", revision: seq5, cancellationToken: cancellationToken);

            Assert.Equal("v1-3", entry.Value);
            Assert.Equal(seq5, entry.Revision);

            Assert.Equal("v1-1", entry1.Value);
            Assert.Equal(seq1, entry1.Revision);
            Assert.Equal("v1-2", entry2.Value);
            Assert.Equal(seq3, entry2.Revision);
            Assert.Equal("v1-3", entry3.Value);
            Assert.Equal(seq5, entry3.Revision);
        }

        // k2
        {
            var entry = await store.GetEntryAsync<string>($"k2", cancellationToken: cancellationToken);
            var entry1 = await store.GetEntryAsync<string>($"k2", revision: seq1, cancellationToken: cancellationToken);
            var entry2 = await store.GetEntryAsync<string>($"k2", revision: seq3, cancellationToken: cancellationToken);
            var entry3 = await store.GetEntryAsync<string>($"k2", revision: seq5, cancellationToken: cancellationToken);

            Assert.Equal("v2-3", entry.Value);
            Assert.Equal(seq6, entry.Revision);

            Assert.Equal("v2-1", entry1.Value);
            Assert.Equal(seq2, entry1.Revision);
            Assert.Equal("v2-2", entry2.Value);
            Assert.Equal(seq4, entry2.Revision);
            Assert.Equal("v2-3", entry3.Value);
            Assert.Equal(seq6, entry3.Revision);
        }
    }
}

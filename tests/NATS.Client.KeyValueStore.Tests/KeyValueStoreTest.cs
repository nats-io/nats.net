using NATS.Client.Core.Tests;
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
        await js.CreateStreamAsync(new StreamConfig
        {
            Name = $"KV_{bucket}",
            Subjects = new[] { $"$KV.{bucket}.>" },
            AllowDirect = false, // this property makes the switch
            Discard = StreamConfigDiscard.@new,
            DenyDelete = true,
            DenyPurge = false,
            NumReplicas = 1,
            MaxMsgsPerSubject = 10,
            Retention = StreamConfigRetention.limits,
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
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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

    [Fact]
    public async Task Delete_and_purge()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        // delete non existing
        {
            await store.DeleteAsync($"k1", cancellationToken: cancellationToken);

            await Assert.ThrowsAsync<NatsKVWrongLastRevisionException>(async () =>
                await store.DeleteAsync($"k1", new NatsKVDeleteOpts { Revision = 1234 }, cancellationToken: cancellationToken));

            await Assert.ThrowsAsync<NatsKVKeyDeletedException>(async () =>
                await store.GetEntryAsync<string>($"k1", cancellationToken: cancellationToken));
        }

        // delete existing with revision
        {
            await store.PutAsync($"k2", "v2-1", cancellationToken: cancellationToken);
            await store.DeleteAsync($"k2", cancellationToken: cancellationToken);

            await Assert.ThrowsAsync<NatsKVKeyDeletedException>(async () =>
                await store.GetEntryAsync<string>($"k2", cancellationToken: cancellationToken));
        }

        // delete existing with revision
        {
            var seq1 = await store.PutAsync($"k3", "v3-1", cancellationToken: cancellationToken);
            var seq2 = await store.PutAsync($"k3", "v3-1", cancellationToken: cancellationToken);

            await Assert.ThrowsAsync<NatsKVWrongLastRevisionException>(async () =>
                await store.DeleteAsync($"k3", new NatsKVDeleteOpts { Revision = seq1 }, cancellationToken: cancellationToken));

            await store.DeleteAsync($"k3", new NatsKVDeleteOpts { Revision = seq2 }, cancellationToken: cancellationToken);

            await Assert.ThrowsAsync<NatsKVKeyDeletedException>(async () =>
                await store.GetEntryAsync<string>($"k3", cancellationToken: cancellationToken));

            var entry = await store.GetEntryAsync<string>($"k3", revision: seq1, cancellationToken: cancellationToken);
            Assert.Equal("v3-1", entry.Value);
        }

        // purge
        {
            var seq0 = await store.PutAsync($"k4", "v4-0", cancellationToken: cancellationToken);
            var seq1 = await store.PutAsync($"k4", "v4-1", cancellationToken: cancellationToken);
            var seq2 = await store.PutAsync($"k4", "v4-2", cancellationToken: cancellationToken);

            // initial state
            {
                var list = new List<NatsKVEntry<string>>();
                await foreach (var entry in store.HistoryAsync<string>("k4", cancellationToken: cancellationToken))
                {
                    list.Add(entry);
                }

                Assert.Equal(3, list.Count);

                Assert.Equal(seq0, list[0].Revision);
                Assert.Equal("v4-0", list[0].Value);
                Assert.Equal(NatsKVOperation.Put, list[0].Operation);

                Assert.Equal(seq1, list[1].Revision);
                Assert.Equal("v4-1", list[1].Value);
                Assert.Equal(NatsKVOperation.Put, list[1].Operation);

                Assert.Equal(seq2, list[2].Revision);
                Assert.Equal("v4-2", list[2].Value);
                Assert.Equal(NatsKVOperation.Put, list[2].Operation);
            }

            // deleted state
            {
                await store.DeleteAsync("k4", cancellationToken: cancellationToken);

                var list = new List<NatsKVEntry<string>>();
                await foreach (var entry in store.HistoryAsync<string>("k4", cancellationToken: cancellationToken))
                {
                    list.Add(entry);
                }

                Assert.Equal(4, list.Count);

                Assert.Equal(seq0, list[0].Revision);
                Assert.Equal("v4-0", list[0].Value);
                Assert.Equal(NatsKVOperation.Put, list[0].Operation);

                Assert.Equal(seq1, list[1].Revision);
                Assert.Equal("v4-1", list[1].Value);
                Assert.Equal(NatsKVOperation.Put, list[1].Operation);

                Assert.Equal(seq2, list[2].Revision);
                Assert.Equal("v4-2", list[2].Value);
                Assert.Equal(NatsKVOperation.Put, list[2].Operation);

                Assert.Null(list[3].Value);
                Assert.Equal(NatsKVOperation.Del, list[3].Operation);
            }

            // purged state
            {
                await store.PurgeAsync("k4", cancellationToken: cancellationToken);

                var list = new List<NatsKVEntry<string>>();
                await foreach (var entry in store.HistoryAsync<string>("k4", cancellationToken: cancellationToken))
                {
                    list.Add(entry);
                }

                Assert.Single(list);

                Assert.Null(list[0].Value);
                Assert.Equal(NatsKVOperation.Purge, list[0].Operation);
            }
        }
    }

    [Fact]
    public async Task Purge_deletes()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        await store.PutAsync("k1", "v1", cancellationToken: cancellationToken);
        await store.PutAsync("k1", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k1", "v3", cancellationToken: cancellationToken);
        await store.DeleteAsync("k1", cancellationToken: cancellationToken);

        await store.PutAsync("k2", "v1", cancellationToken: cancellationToken);
        await store.PutAsync("k2", "v2", cancellationToken: cancellationToken);
        await store.PutAsync("k2", "v3", cancellationToken: cancellationToken);
        await store.DeleteAsync("k2", cancellationToken: cancellationToken);

        var count = 0;
        await foreach (var entry in store.HistoryAsync<string>("k1", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        await foreach (var entry in store.HistoryAsync<string>("k2", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        _output.WriteLine($"COUNT={count}");
        Assert.Equal(8, count);

        _output.WriteLine("PURGE DELETES");
        await store.PurgeDeletesAsync(cancellationToken: cancellationToken);

        count = 0;
        await foreach (var entry in store.HistoryAsync<string>("k1", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        await foreach (var entry in store.HistoryAsync<string>("k2", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        _output.WriteLine($"COUNT={count}");
        Assert.Equal(2, count);

        _output.WriteLine("PURGE ALL DELETES");
        await store.PurgeDeletesAsync(opts: new NatsKVPurgeOpts { DeleteMarkersThreshold = TimeSpan.Zero }, cancellationToken: cancellationToken);

        count = 0;
        await foreach (var entry in store.HistoryAsync<string>("k1", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        await foreach (var entry in store.HistoryAsync<string>("k2", cancellationToken: cancellationToken))
        {
            count++;
            _output.WriteLine($"{entry}");
        }

        _output.WriteLine($"COUNT={count}");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Update_with_revisions()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        // update new
        {
            var seq = await store.UpdateAsync($"k1", "v1-new", 0, cancellationToken: cancellationToken);
            var entry = await store.GetEntryAsync<string>($"k1", cancellationToken: cancellationToken);
            Assert.Equal("v1-new", entry.Value);
            Assert.Equal(seq, entry.Revision);

            await Assert.ThrowsAsync<NatsKVWrongLastRevisionException>(async () =>
                await store.UpdateAsync($"k1", "v1-new", 0, cancellationToken: cancellationToken));
        }

        // update existing
        {
            var seq1 = await store.PutAsync($"k1", "v1-1", cancellationToken: cancellationToken);
            var seq2 = await store.UpdateAsync($"k1", "v1-2", seq1, cancellationToken: cancellationToken);
            var entry = await store.GetEntryAsync<string>($"k1", cancellationToken: cancellationToken);
            Assert.Equal("v1-2", entry.Value);
            Assert.Equal(seq2, entry.Revision);

            await Assert.ThrowsAsync<NatsKVWrongLastRevisionException>(async () =>
                await store.UpdateAsync($"k1", "v1-3", seq1, cancellationToken: cancellationToken));
        }
    }

    [Fact]
    public async Task Create()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        // create new
        {
            var seq = await store.CreateAsync($"k1", "new", cancellationToken: cancellationToken);
            var entry = await store.GetEntryAsync<string>($"k1", cancellationToken: cancellationToken);
            Assert.Equal("new", entry.Value);
            Assert.Equal(seq, entry.Revision);

            await Assert.ThrowsAsync<NatsKVCreateException>(async () =>
                await store.CreateAsync($"k1", "new", cancellationToken: cancellationToken));
        }

        // try create existing
        {
            await store.PutAsync($"k2", "v1", cancellationToken: cancellationToken);
            await Assert.ThrowsAsync<NatsKVCreateException>(async () =>
                await store.CreateAsync($"k2", "new", cancellationToken: cancellationToken));
        }

        // recreate deleted
        {
            var seq1 = await store.CreateAsync($"k3", "new", cancellationToken: cancellationToken);

            await store.DeleteAsync("k3", cancellationToken: cancellationToken);

            var seq2 = await store.CreateAsync($"k3", "renew", cancellationToken: cancellationToken);

            var entry = await store.GetEntryAsync<string>($"k3", cancellationToken: cancellationToken);
            Assert.Equal("renew", entry.Value);
            Assert.Equal(seq2, entry.Revision);

            var entry1 = await store.GetEntryAsync<string>($"k3", seq1, cancellationToken: cancellationToken);
            Assert.Equal("new", entry1.Value);

            await Assert.ThrowsAsync<NatsKVCreateException>(async () =>
                await store.CreateAsync($"k3", "again", cancellationToken: cancellationToken));
        }
    }

    [Fact]
    public async Task History()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        var nonExistentKeyCount = 0;
        await foreach (var unused in store.HistoryAsync<string>("non-existing-key", cancellationToken: cancellationToken))
        {
            nonExistentKeyCount++;
        }

        Assert.Equal(0, nonExistentKeyCount);

        var seq = new ulong[5];
        seq[0] = await store.CreateAsync($"k1", "v0", cancellationToken: cancellationToken);
        seq[1] = await store.UpdateAsync($"k1", "v1", seq[0], cancellationToken: cancellationToken);
        seq[2] = await store.UpdateAsync($"k1", "v2", seq[1], cancellationToken: cancellationToken);
        seq[3] = await store.UpdateAsync($"k1", "v3", seq[2], cancellationToken: cancellationToken);
        seq[4] = await store.UpdateAsync($"k1", "v4", seq[3], cancellationToken: cancellationToken);

        var list = new List<NatsKVEntry<string>>();
        await foreach (var entry in store.HistoryAsync<string>("k1", cancellationToken: cancellationToken))
        {
            list.Add(entry);
        }

        Assert.Equal(seq.Length, list.Count);

        for (var i = 0; i < seq.Length; i++)
        {
            Assert.Equal(seq[i], list[i].Revision);
            Assert.Equal($"v{i}", list[i].Value);
        }
    }

    [Fact]
    public async Task Status()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { History = 10 }, cancellationToken: cancellationToken);

        Assert.Equal("kv1", store.Bucket);

        var status = await store.GetStatusAsync(cancellationToken);

        Assert.Equal("kv1", status.Bucket);
        Assert.Equal("KV_kv1", status.Info.Config.Name);
        Assert.Equal(10, status.Info.Config.MaxMsgsPerSubject);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Compressed_storage()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var store1 = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { Compression = false }, cancellationToken: cancellationToken);
        var store2 = await kv.CreateStoreAsync(new NatsKVConfig("kv2") { Compression = true }, cancellationToken: cancellationToken);

        Assert.Equal("kv1", store1.Bucket);
        Assert.Equal("kv2", store2.Bucket);

        var status1 = await store1.GetStatusAsync(cancellationToken);
        Assert.Equal("kv1", status1.Bucket);
        Assert.Equal("KV_kv1", status1.Info.Config.Name);
        Assert.Equal(StreamConfigurationCompression.none, status1.Info.Config.Compression);

        var status2 = await store2.GetStatusAsync(cancellationToken);
        Assert.Equal("kv2", status2.Bucket);
        Assert.Equal("KV_kv2", status2.Info.Config.Name);
        Assert.Equal(StreamConfigurationCompression.s2, status2.Info.Config.Compression);
    }
}

using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

public class KeyValueStoreTest
{
    private readonly ITestOutputHelper _output;

    public KeyValueStoreTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Simple_create_put_get_test(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        await store.PutAsync("k1", "v1");

        var entry = await store.GetEntryAsync<string>("k1");
        _output.WriteLine($"got:{entry}");
        Assert.True(entry.UsedDirectGet);
        Assert.Equal("v1", entry.Value);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Handle_non_direct_gets(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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
            Discard = StreamConfigDiscard.New,
            DenyDelete = true,
            DenyPurge = false,
            NumReplicas = 1,
            MaxMsgsPerSubject = 10,
            Retention = StreamConfigRetention.Limits,
            DuplicateWindow = TimeSpan.FromMinutes(2), // 120_000_000_000ns
        });

        var store = await kv.GetStoreAsync("b1");

        await store.PutAsync("k1", "v1");

        var entry = await store.GetEntryAsync<string>("k1");
        _output.WriteLine($"got:{entry}");
        Assert.False(entry.UsedDirectGet);
        Assert.Equal("v1", entry.Value);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Get_keys(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Get_key_revisions(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Delete_and_purge(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Purge_deletes(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

        _output.WriteLine("PURGE ALL DELETES ON EMPTY BUCKET");
        await store.PurgeDeletesAsync(opts: new NatsKVPurgeOpts { DeleteMarkersThreshold = TimeSpan.Zero }, cancellationToken: cancellationToken);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Update_with_revisions(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [SkipIfNatsServer(versionLaterThan: "2.11")]
    public async Task TestMessageTTLApiNotSupportedupport()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        // Config validation
        var exception = await Assert.ThrowsAsync<NatsKVException>(() => kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(10) }, cancellationToken: cancellationToken).AsTask());
        _output.WriteLine(exception.Message);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.11")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task TestMessageTTL(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        // Check TTL support
        var exception = await Assert.ThrowsAsync<NatsKVException>(async () =>
        {
            var store1 = await kv.CreateStoreAsync(new NatsKVConfig("kv0") { LimitMarkerTTL = TimeSpan.Zero }, cancellationToken: cancellationToken);
            await store1.CreateAsync("k1", "v1", ttl: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken);
        });
        Assert.Equal("This store does not support TTL", exception.Message);

        // Check API version
        var info = await js.JSRequestResponseAsync<object, AccountInfoResponse>("$JS.API.INFO", null, cancellationToken);
        Assert.True(info.Api.Level >= 1);

        // Config validation
        await Assert.ThrowsAsync<NatsKVException>(() => kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(-1) }, cancellationToken: cancellationToken).AsTask());
        await Assert.ThrowsAsync<NatsKVException>(() => kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(.99) }, cancellationToken: cancellationToken).AsTask());

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(2) }, cancellationToken: cancellationToken);

        for (var i = 0; i < 10; i++)
        {
            await store.CreateAsync($"k{i}", $"v{i}", TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
        }

        var state = await store.GetStatusAsync(cancellationToken);
        Assert.Equal(10, state.Info.State.Messages);
        Assert.Equal(1ul, state.Info.State.FirstSeq);
        Assert.Equal(10ul, state.Info.State.LastSeq);

        await Retry.Until(
            reason: "messages are deleted",
            condition: async () =>
            {
                var state1 = await store.GetStatusAsync(cancellationToken);
                _output.WriteLine($"Messages: {state1.Info.State.Messages}");
                _output.WriteLine($"FirstSeq: {state1.Info.State.FirstSeq}");
                _output.WriteLine($"LastSeq: {state1.Info.State.LastSeq}");

                return state1.Info.State is { Messages: 0, FirstSeq: 21, LastSeq: 20 };
            },
            retryDelay: TimeSpan.FromSeconds(2),
            timeout: TimeSpan.FromSeconds(30));

        state = await store.GetStatusAsync(cancellationToken);
        Assert.Equal(0, state.Info.State.Messages);
        Assert.Equal(21ul, state.Info.State.FirstSeq);
        Assert.Equal(20ul, state.Info.State.LastSeq);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.11")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task TestTTLMessageWhenTTLDisabledOnStream(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.Zero }, cancellationToken: cancellationToken);
        var exception = await Assert.ThrowsAsync<NatsKVException>(async () => await store.CreateAsync($"somekey", $"somevalue", TimeSpan.FromSeconds(1), cancellationToken: cancellationToken));
        Assert.Equal("This store does not support TTL", exception.Message);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.11")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task SetsSubjectDeleteMarkerTTL(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(2) }, cancellationToken: cancellationToken);
        var info = await js.GetStreamAsync("KV_kv1");
        Assert.Equal(TimeSpan.FromSeconds(2), info.Info.Config.SubjectDeleteMarkerTTL);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.11")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task SubjectDeleteMarkerTTL_enabled_removals_should_be_interpreted_as_Operation_Purge(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var store = await kv.CreateStoreAsync(
            new NatsKVConfig("kv1")
            {
                LimitMarkerTTL = TimeSpan.FromHours(1),
                MaxAge = TimeSpan.FromSeconds(4),
            },
            cancellationToken: cancellationToken);

        var r1 = await store.CreateAsync("foo", "LOCKED", cancellationToken: cancellationToken);
        Assert.Equal(1ul, r1);

        var create = Task.Run(
            async () =>
            {
                await Task.Delay(6000, cancellationToken);
                Console.WriteLine("6 seconds passed — creating...");
                return await store.CreateAsync("foo", "LOCKED", ttl: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
            },
            cancellationToken);

        var checkOps = new List<NatsKVOperation>();
        await foreach (var entry in store.WatchAsync<string>("foo", opts: new() { IncludeHistory = true }, cancellationToken: cancellationToken))
        {
            checkOps.Add(entry.Operation);
            if (entry.Revision == 3)
                break;
        }

        Assert.Equal(3, checkOps.Count);
        Assert.Equal(NatsKVOperation.Put, checkOps[0]);
        Assert.Equal(NatsKVOperation.Purge, checkOps[1]);
        Assert.Equal(NatsKVOperation.Put, checkOps[2]);

        var r2 = await create;
        Assert.Equal(3ul, r2);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.11")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task TestMessageNeverExpire(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(new NatsKVConfig("kv1") { LimitMarkerTTL = TimeSpan.FromSeconds(2) }, cancellationToken: cancellationToken);

        // The first message we publish is set to "never expire", therefore it won't age out with the MaxAge policy.
        await store.CreateAsync($"k0", $"v0", TimeSpan.MaxValue, cancellationToken: cancellationToken);

        await Task.Delay(1000);

        for (var i = 1; i < 11; i++)
        {
            await store.CreateAsync($"k{i}", $"v{i}", TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
        }

        var state = await store.GetStatusAsync(cancellationToken);
        Assert.Equal(11, state.Info.State.Messages);
        Assert.Equal(1ul, state.Info.State.FirstSeq);
        Assert.Equal(11ul, state.Info.State.LastSeq);

        await Retry.Until(
            reason: "messages are deleted",
            condition: async () =>
            {
                var state1 = await store.GetStatusAsync(cancellationToken);
                return state1.Info.State is { Messages: 1, FirstSeq: 1, LastSeq: 21 };
            },
            retryDelay: TimeSpan.FromSeconds(2),
            timeout: TimeSpan.FromSeconds(30));

        state = await store.GetStatusAsync(cancellationToken);
        Assert.Equal(1, state.Info.State.Messages);
        Assert.Equal(1ul, state.Info.State.FirstSeq);
        Assert.Equal(21ul, state.Info.State.LastSeq);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task History(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Status(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(
            new NatsKVConfig("kv1")
            {
                History = 10,
                Metadata = new Dictionary<string, string> { { "meta1", "value1" } },
            },
            cancellationToken: cancellationToken);

        Assert.Equal("kv1", store.Bucket);

        var status = await store.GetStatusAsync(cancellationToken);

        Assert.Equal("kv1", status.Bucket);
        Assert.Equal("KV_kv1", status.Info.Config.Name);
        Assert.Equal(10, status.Info.Config.MaxMsgsPerSubject);

        if (!nats.ServerInfo!.Version.StartsWith("2.9."))
        {
            _output.WriteLine("Check metadata");
            Assert.Equal("value1", status.Info.Config.Metadata?["meta1"]);
        }
        else
        {
            _output.WriteLine("Metadata is not supported in server versions < 2.10");
        }
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.10")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Compressed_storage(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

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
        Assert.Equal(StreamConfigCompression.None, status1.Info.Config.Compression);

        var status2 = await store2.GetStatusAsync(cancellationToken);
        Assert.Equal("kv2", status2.Bucket);
        Assert.Equal("KV_kv2", status2.Info.Config.Name);
        Assert.Equal(StreamConfigCompression.S2, status2.Info.Config.Compression);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Validate_keys(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        string[] validKeys = [
            "k.1",
            "=",
            "_",
            "-",
            "123",
            "Abc",
        ];

        foreach (var key in validKeys)
        {
            var rev = await store.PutAsync(key, "value1");
            await store.UpdateAsync(key, "value2", rev);
            var entry = await store.GetEntryAsync<string>(key);
            Assert.Equal("value2", entry.Value);
            await store.DeleteAsync(key);
        }

        string[] invalidKeys = [
            null!,
            string.Empty,
            ".k",
            "k.",
            "k$",
            "k%",
            "k*",
            "k>",
            "k\n",
            "k\r",
        ];

        foreach (var key in invalidKeys)
        {
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.CreateAsync(key, "value"));
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.PutAsync(key, "value"));
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.UpdateAsync(key, "value", 1));
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.GetEntryAsync<string>(key));
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.DeleteAsync(key));
            await Assert.ThrowsAsync<NatsKVException>(async () => await store.PurgeAsync(key));
        }
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task TestDirectMessageRepublishedSubject(NatsRequestReplyMode mode)
    {
        var streamBucketName = "sb-" + Nuid.NewNuid();
        var subject = "test";
        var streamSubject = subject + ".>";
        var publishSubject1 = subject + ".one";
        var publishSubject2 = subject + ".two";
        var publishSubject3 = subject + ".three";
        var republishDest = "$KV." + streamBucketName + ".>";

        var streamConfig = new StreamConfig(streamBucketName, new[] { streamSubject }) { Republish = new Republish { Src = ">", Dest = republishDest } };

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });
        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(streamBucketName);
        await js.CreateStreamAsync(streamConfig);

        await nats.PublishAsync<string>(publishSubject1, "uno");
        await js.PublishAsync<string>(publishSubject2, "dos");
        await store.PutAsync(publishSubject3, "tres");

        var kve1 = await store.GetEntryAsync<string>(publishSubject1);
        Assert.Equal(streamBucketName, kve1.Bucket);
        Assert.Equal(publishSubject1, kve1.Key);
        Assert.Equal("uno", kve1.Value);

        var kve2 = await store.GetEntryAsync<string>(publishSubject2);
        Assert.Equal(streamBucketName, kve2.Bucket);
        Assert.Equal(publishSubject2, kve2.Key);
        Assert.Equal("dos", kve2.Value);

        var kve3 = await store.GetEntryAsync<string>(publishSubject3);
        Assert.Equal(streamBucketName, kve3.Bucket);
        Assert.Equal(publishSubject3, kve3.Key);
        Assert.Equal("tres", kve3.Value);
    }

    [SkipIfNatsServerTheory(versionEarlierThan: "2.10")]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Test_CombinedSources(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var storeSource1 = await kv.CreateStoreAsync("source1");
        var storeSource2 = await kv.CreateStoreAsync("source2");

        var storeCombined = await kv.CreateStoreAsync(new NatsKVConfig("combined")
        {
            Sources = [
                new StreamSource { Name = "source1" },
                new StreamSource { Name = "source2" }
            ],
        });

        await storeSource1.PutAsync("ss1_a", "a_fromStore1");
        await storeSource2.PutAsync("ss2_b", "b_fromStore2");

        await Retry.Until(
            "async replication is completed",
            async () =>
            {
                try
                {
                    await storeCombined.GetEntryAsync<string>("ss1_a");
                    await storeCombined.GetEntryAsync<string>("ss2_b");
                }
                catch (NatsKVKeyNotFoundException)
                {
                    return false;
                }

                return true;
            });

        var entryA = await storeCombined.GetEntryAsync<string>("ss1_a");
        var entryB = await storeCombined.GetEntryAsync<string>("ss2_b");

        Assert.Equal("a_fromStore1", entryA.Value);
        Assert.Equal("b_fromStore2", entryB.Value);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Try_Create(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        NatsResult<ulong> result = default;

        result = await store.TryCreateAsync("k1", "v1");
        Assert.True(result.Success);
        Assert.ThrowsAny<InvalidOperationException>(() => result.Error);

        result = await store.TryCreateAsync("k1", "v2");
        Assert.False(result.Success);
        Assert.True(result.Error is NatsKVCreateException);

        var deleteResult = await store.TryDeleteAsync("k1");
        Assert.True(deleteResult.Success);
        Assert.ThrowsAny<InvalidOperationException>(() => deleteResult.Error);

        result = await store.TryCreateAsync("k1", "v3");
        Assert.True(result.Success);
        Assert.ThrowsAny<InvalidOperationException>(() => result.Error);

        var finalValue = await store.TryGetEntryAsync<string>("k1");
        Assert.True(finalValue.Success && finalValue.Value.Value == "v3");
        Assert.ThrowsAny<InvalidOperationException>(() => finalValue.Error);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Try_Delete(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        var putResult = await store.TryPutAsync("k1", "v1");
        Assert.True(putResult.Success);

        var entry = putResult.Value;

        var updateResultFail = await store.TryUpdateAsync("k1", "v2", revision: entry + 1);
        Assert.False(updateResultFail.Success);
        Assert.True(updateResultFail.Error is NatsKVWrongLastRevisionException);

        var updateResultSuccess = await store.TryUpdateAsync("k1", "v2", revision: entry);
        Assert.True(updateResultSuccess.Success);
        Assert.ThrowsAny<InvalidOperationException>(() => updateResultSuccess.Error);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Try_Update(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        var putFailResult = await store.TryPutAsync(".badkeystartingwithperiod", "v1");
        Assert.False(putFailResult.Success);
        Assert.True(putFailResult.Error is NatsKVException);

        var putResult = await store.TryPutAsync("k1", "v1");
        Assert.True(putResult.Success);

        var entry = putResult.Value;

        var deleteResultFail = await store.TryDeleteAsync("k1", new NatsKVDeleteOpts { Revision = entry + 1 });
        Assert.False(deleteResultFail.Success);
        Assert.True(deleteResultFail.Error is NatsKVWrongLastRevisionException);

        var deleteResultSuccess = await store.TryDeleteAsync("k1", new NatsKVDeleteOpts { Revision = entry });
        Assert.True(deleteResultSuccess.Success);
        Assert.ThrowsAny<InvalidOperationException>(() => deleteResultSuccess.Error);
    }

    [Fact]
    public void Test_Keys()
    {
        NatsResult keyTestResult;

        keyTestResult = NatsKVStore.IsValidKey(string.Empty);
        Assert.True(!keyTestResult.Success && keyTestResult.Error is NatsKVException);

        keyTestResult = NatsKVStore.IsValidKey("     ");
        Assert.True(!keyTestResult.Success && keyTestResult.Error is NatsKVException);

        keyTestResult = NatsKVStore.IsValidKey(".a");
        Assert.True(!keyTestResult.Success && keyTestResult.Error is NatsKVException);

        keyTestResult = NatsKVStore.IsValidKey("a 1");
        Assert.True(!keyTestResult.Success && keyTestResult.Error is NatsKVException);

        keyTestResult = NatsKVStore.IsValidKey("k1");
        Assert.True(keyTestResult.Success);

        keyTestResult = NatsKVStore.IsValidKey("k.1");
        Assert.True(keyTestResult.Success);
    }
}

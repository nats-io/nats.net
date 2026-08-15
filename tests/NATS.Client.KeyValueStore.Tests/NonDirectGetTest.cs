using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

public class NonDirectGetTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reads_behave_the_same_with_and_without_direct_get(bool allowDirect)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var bucket = allowDirect ? "direct" : "nondirect";

        INatsKVStore store;
        if (allowDirect)
        {
            store = await kv.CreateStoreAsync(new NatsKVConfig(bucket), cancellationToken);
        }
        else
        {
            // KV always creates buckets with AllowDirect enabled, so build the stream
            // directly to get a legacy bucket that has it turned off.
            await js.CreateStreamAsync(
                new StreamConfig($"KV_{bucket}", new[] { $"$KV.{bucket}.>" })
                {
                    AllowDirect = false,
                    MaxMsgsPerSubject = 10,
                    AllowRollupHdrs = true,
                    DenyDelete = true,
                    Discard = StreamConfigDiscard.New,
                },
                cancellationToken);

            store = await kv.GetStoreAsync(bucket, cancellationToken);
        }

        var stream = await js.GetStreamAsync($"KV_{bucket}", cancellationToken: cancellationToken);
        Assert.Equal(allowDirect, stream.Info.Config.AllowDirect);
        output.WriteLine($"bucket {bucket} AllowDirect={stream.Info.Config.AllowDirect}");

        var revision = await store.PutAsync("k", "v1", cancellationToken: cancellationToken);

        var entry = await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken);
        Assert.Equal("v1", entry.Value);
        Assert.Equal(NatsKVOperation.Put, entry.Operation);

        // The non-direct path used to reject the matching subject here.
        var byRevision = await store.GetEntryAsync<string>("k", revision: revision, cancellationToken: cancellationToken);
        Assert.Equal("v1", byRevision.Value);
        Assert.Equal(revision, byRevision.Revision);

        // Without header decoding a deleted key came back as a live entry with Operation = Put.
        await store.DeleteAsync("k", cancellationToken: cancellationToken);
        await Assert.ThrowsAsync<NatsKVKeyDeletedException>(async () =>
            await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken));

        var recreated = await store.CreateAsync("k", "v2", cancellationToken: cancellationToken);
        Assert.True(recreated > revision);

        var after = await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken);
        Assert.Equal("v2", after.Value);
        Assert.Equal(NatsKVOperation.Put, after.Operation);
    }
}

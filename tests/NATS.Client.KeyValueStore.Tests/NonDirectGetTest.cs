using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

/// <summary>
/// Buckets whose stream has AllowDirect disabled take the non-direct read path.
/// ADR-8 supports reading those legacy buckets, and requires delete and purge
/// markers to be honored regardless of which read method was used, so this path
/// has to behave exactly like the direct one.
/// </summary>
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
            // The KV API always creates buckets with AllowDirect enabled, so build the
            // stream directly to reproduce a legacy bucket that has it turned off.
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

        // Plain get
        var entry = await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken);
        Assert.Equal("v1", entry.Value);
        Assert.Equal(NatsKVOperation.Put, entry.Operation);

        // Get by revision. The non-direct path used to reject the correct subject and
        // always throw NatsKVException("Unexpected subject") here.
        var byRevision = await store.GetEntryAsync<string>("k", revision: revision, cancellationToken: cancellationToken);
        Assert.Equal("v1", byRevision.Value);
        Assert.Equal(revision, byRevision.Revision);

        // Deleted keys must report as deleted, not come back as a live entry. The
        // non-direct path never looked at the headers so it returned Operation = Put.
        await store.DeleteAsync("k", cancellationToken: cancellationToken);
        await Assert.ThrowsAsync<NatsKVKeyDeletedException>(async () =>
            await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken));

        // Re-creating a deleted key must succeed. It failed permanently while the
        // delete marker was mistaken for a live entry.
        var recreated = await store.CreateAsync("k", "v2", cancellationToken: cancellationToken);
        Assert.True(recreated > revision);

        var after = await store.GetEntryAsync<string>("k", cancellationToken: cancellationToken);
        Assert.Equal("v2", after.Value);
        Assert.Equal(NatsKVOperation.Put, after.Operation);
    }
}

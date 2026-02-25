using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

public class GetKeysTest
{
    [Fact]
    public async Task Get_keys_should_not_hang_when_there_are_deleted_keys()
    {
        const string bucket = "b1";
        var config = new NatsKVConfig(bucket) { History = 10 };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats1 = new NatsConnection(new NatsOpts { Url = server.Url });
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var store1 = await kv1.CreateStoreAsync(config, cancellationToken: cancellationToken);

        await store1.PutAsync("k1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("k2", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("k3", 3, cancellationToken: cancellationToken);

        var ks1 = new List<string>();
        await foreach (var k in store1.GetKeysAsync(cancellationToken: cancellationToken))
        {
            ks1.Add(k);
        }

        ks1.Sort();

        Assert.Equal(new List<string> { "k1", "k2", "k3" }, ks1);

        await store1.DeleteAsync("k2", cancellationToken: cancellationToken);

        var ks2 = new List<string>();
        await foreach (var k in store1.GetKeysAsync(cancellationToken: cancellationToken))
        {
            ks2.Add(k);
        }

        ks2.Sort();

        Assert.Equal(new List<string> { "k1", "k3" }, ks2);
    }

    [Fact]
    public async Task Get_keys_when_empty()
    {
        const string bucket = "b1";
        var config = new NatsKVConfig(bucket) { History = 10 };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats1 = new NatsConnection(new NatsOpts { Url = server.Url });
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var store1 = await kv1.CreateStoreAsync(config, cancellationToken: cancellationToken);

        var count = 0;
        await foreach (var k in store1.GetKeysAsync(cancellationToken: cancellationToken))
        {
            count++;
        }

        Assert.Equal(0, count);

        await store1.PutAsync("k1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("k2", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("k3", 3, cancellationToken: cancellationToken);

        await foreach (var k in store1.GetKeysAsync(cancellationToken: cancellationToken))
        {
            count++;
        }

        Assert.Equal(3, count);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Get_filtered_keys()
    {
        const string bucket = "b1";
        var config = new NatsKVConfig(bucket);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats1 = new NatsConnection(new NatsOpts { Url = server.Url });
        var js1 = new NatsJSContext(nats1);
        var kv1 = new NatsKVContext(js1);
        var store1 = await kv1.CreateStoreAsync(config, cancellationToken: cancellationToken);

        await store1.PutAsync("a.1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("a.2", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("b.1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("b.2", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("c.1", 1, cancellationToken: cancellationToken);
        await store1.PutAsync("c.2", 2, cancellationToken: cancellationToken);
        await store1.PutAsync("d", 2, cancellationToken: cancellationToken);

        var ks1 = new List<string>();

        // Multiple keys are only supported in NATS Server 2.10 and later
        await foreach (var k in store1.GetKeysAsync(["d", "a.>", "c.>"], cancellationToken: cancellationToken))
        {
            ks1.Add(k);
        }

        ks1.Sort();

        Assert.Equal(new List<string> { "a.1", "a.2", "c.1", "c.2", "d" }, ks1);
    }
}

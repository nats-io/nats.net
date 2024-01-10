using NATS.Client.Core.Tests;

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

        await using var server = NatsServer.StartJS();
        await using var nats1 = server.CreateClientConnection();
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
}

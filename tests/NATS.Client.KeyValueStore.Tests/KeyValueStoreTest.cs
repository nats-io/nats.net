using NATS.Client.Core.Tests;

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
        var value = await store.GetAsync<string>("k1");
        _output.WriteLine($"got:{value}");
        Assert.Equal("v1", value);
    }
}

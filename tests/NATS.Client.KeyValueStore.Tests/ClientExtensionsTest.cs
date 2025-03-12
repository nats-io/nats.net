using NATS.Net;

namespace NATS.Client.KeyValueStore.Tests;

public class ClientExtensionsTest
{
    [Fact]
    public void Test()
    {
        var opts = new NatsKVOpts { UseDirectGetApiWithKeysInSubject = true };

        var connection = new NatsConnection();
        Assert.IsType<NatsKVContext>(connection.CreateKeyValueStoreContext(), exactMatch: true);
        Assert.IsType<INatsKVContext>(connection.CreateKeyValueStoreContext(), exactMatch: false);
        Assert.IsType<NatsKVContext>(connection.CreateKeyValueStoreContext(opts), exactMatch: true);
        Assert.IsType<INatsKVContext>(connection.CreateKeyValueStoreContext(opts), exactMatch: false);
        Assert.False(connection.CreateKeyValueStoreContext().Opts.UseDirectGetApiWithKeysInSubject);
        Assert.True(connection.CreateKeyValueStoreContext(opts).Opts.UseDirectGetApiWithKeysInSubject);

        var client = new NatsClient();
        Assert.IsType<NatsKVContext>(client.CreateKeyValueStoreContext(), exactMatch: true);
        Assert.IsType<INatsKVContext>(client.CreateKeyValueStoreContext(), exactMatch: false);
        Assert.IsType<NatsKVContext>(client.CreateKeyValueStoreContext(opts), exactMatch: true);
        Assert.IsType<INatsKVContext>(client.CreateKeyValueStoreContext(opts), exactMatch: false);
        Assert.False(client.CreateKeyValueStoreContext().Opts.UseDirectGetApiWithKeysInSubject);
        Assert.True(client.CreateKeyValueStoreContext(opts).Opts.UseDirectGetApiWithKeysInSubject);

        var js = new NatsJSContext(connection);
        Assert.IsType<NatsKVContext>(js.CreateKeyValueStoreContext(), exactMatch: true);
        Assert.IsType<INatsKVContext>(js.CreateKeyValueStoreContext(), exactMatch: false);
        Assert.IsType<NatsKVContext>(js.CreateKeyValueStoreContext(opts), exactMatch: true);
        Assert.IsType<INatsKVContext>(js.CreateKeyValueStoreContext(opts), exactMatch: false);
        Assert.False(js.CreateKeyValueStoreContext().Opts.UseDirectGetApiWithKeysInSubject);
        Assert.True(js.CreateKeyValueStoreContext(opts).Opts.UseDirectGetApiWithKeysInSubject);
    }
}

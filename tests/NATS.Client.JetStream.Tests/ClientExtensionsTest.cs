using NATS.Net;

namespace NATS.Client.JetStream.Tests;

public class ClientExtensionsTest
{
    [Fact]
    public void Test()
    {
        var opts = new NatsJSOpts(new NatsOpts(), apiPrefix: "$TEST");

        var connection = new NatsConnection();
        Assert.IsType<NatsJSContext>(connection.CreateJetStreamContext(), exactMatch: true);
        Assert.IsType<INatsJSContext>(connection.CreateJetStreamContext(), exactMatch: false);
        Assert.IsType<NatsJSContext>(connection.CreateJetStreamContext(opts), exactMatch: true);
        Assert.IsType<INatsJSContext>(connection.CreateJetStreamContext(opts), exactMatch: false);
        Assert.Equal("$JS.API", connection.CreateJetStreamContext().Opts.ApiPrefix);
        Assert.Equal("$TEST", connection.CreateJetStreamContext(opts).Opts.ApiPrefix);

        var client = new NatsClient();
        Assert.IsType<NatsJSContext>(client.CreateJetStreamContext(), exactMatch: true);
        Assert.IsType<INatsJSContext>(client.CreateJetStreamContext(), exactMatch: false);
        Assert.IsType<NatsJSContext>(client.CreateJetStreamContext(opts), exactMatch: true);
        Assert.IsType<INatsJSContext>(client.CreateJetStreamContext(opts), exactMatch: false);
        Assert.Equal("$JS.API", client.CreateJetStreamContext().Opts.ApiPrefix);
        Assert.Equal("$TEST", client.CreateJetStreamContext(opts).Opts.ApiPrefix);
    }
}

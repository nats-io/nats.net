using NATS.Client.Core2.Tests;
using NATS.Client.Serializers.Json;

namespace NATS.Client.Core.Tests;

[Collection("nats-server")]
public class JsonSerializerTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public JsonSerializerTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Serialize_any_type()
    {
        var natsOpts = NatsOpts.Default with
        {
            Url = _server.Url,
            SerializerRegistry = NatsJsonSerializerRegistry.Default,
        };
        await using var nats = new NatsConnection(natsOpts);

        // in local runs server start is taking too long when running
        // the whole suite.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var sub = await nats.SubscribeCoreAsync<SomeTestData>("foo", cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);
        await nats.PublishAsync("foo", new SomeTestData { Name = "bar" }, cancellationToken: cancellationToken);

        var msg = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.Equal("bar", msg.Data?.Name);

        // Default serializer won't work with random types
        await using var nats1 = new NatsConnection(new NatsOpts { Url = _server.Url });

        var exception = await Assert.ThrowsAsync<NatsException>(() => nats1.PublishAsync(
            subject: "would.not.work",
            data: new SomeTestData { Name = "won't work" },
            cancellationToken: cancellationToken).AsTask());

        Assert.Matches(@"Can't serialize .*SomeTestData", exception.Message);
    }

    private class SomeTestData
    {
        public string? Name { get; set; }
    }
}

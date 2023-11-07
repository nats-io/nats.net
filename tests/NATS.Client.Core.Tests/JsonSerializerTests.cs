using NATS.Client.Serializers.Json;

namespace NATS.Client.Core.Tests;

public class JsonSerializerTests
{
    private readonly ITestOutputHelper _output;

    public JsonSerializerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Serialize_any_type()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var natsOpts = NatsOpts.Default with
        {
            SerializerRegistry = NatsJsonSerializerRegistry.Default,
        };

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection(natsOpts);

        await using var sub = await nats.SubscribeInternalAsync<SomeTestData>("foo", cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);
        await nats.PublishAsync("foo", new SomeTestData { Name = "bar" }, cancellationToken: cancellationToken);

        var msg = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.Equal("bar", msg.Data?.Name);

        // Default serializer won't work with random types
        await using var nats1 = server.CreateClientConnection();

        var signal = new WaitSignal<Exception>();

        await nats1.PublishAsync(
            subject: "would.not.work",
            data: new SomeTestData { Name = "won't work" },
            opts: new NatsPubOpts { ErrorHandler = e => signal.Pulse(e) },
            cancellationToken: cancellationToken);

        var exception = await signal;
        Assert.Matches(@"Can't serialize .*SomeTestData", exception.Message);
    }

    private class SomeTestData
    {
        public string? Name { get; set; }
    }
}

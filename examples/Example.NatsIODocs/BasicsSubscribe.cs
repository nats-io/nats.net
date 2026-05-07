using NATS.Net;

[Collection("nats-server")]
public class BasicsSubscribe(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // NATS-DOC-START
        // Subscribe to 'weather.updates' and process messages
        try
        {
            await foreach (var msg in client.SubscribeAsync<string>("weather.updates", cancellationToken: cts.Token))
            {
                Console.WriteLine($"Received: {msg.Data}");
            }
        }
        catch (OperationCanceledException)
        {
        }

        // NATS-DOC-END
    }
}

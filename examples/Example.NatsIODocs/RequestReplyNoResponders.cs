using NATS.Client.Core;
using NATS.Net;

[Collection("nats-server")]
public class RequestReplyNoResponders(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // RequestAsync throws NatsNoRespondersException by default when nobody is listening
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var reply = await client.RequestAsync<string>("no.such.service", cancellationToken: cts.Token);
            Console.WriteLine($"Response: {reply.Data}");
        }
        catch (NatsNoRespondersException)
        {
            Console.WriteLine("No Response: no responders");
        }

        // NATS-DOC-END
    }
}

using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyNoResponders(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // RequestAsync throws NatsNoRespondersException by default when nobody is listening
        try
        {
            var reply = await client.RequestAsync<string, string>(subject: "no.such.service", data: "test");
            output.WriteLine($"Response: {reply.Data}");
        }
        catch (NatsNoRespondersException)
        {
            output.WriteLine("No services available to handle the request");
        }

        // NATS-DOC-END
    }
}

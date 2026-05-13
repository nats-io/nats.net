using NATS.Client.Core;
using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class RequestReplyHeaders(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // Echo service: reflects the request id back as a response id
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("service"))
            {
                var responseHeaders = new NatsHeaders
                {
                    ["X-Response-ID"] = msg.Headers?["X-Request-ID"] ?? string.Empty,
                };
                await msg.ReplyAsync(msg.Data, headers: responseHeaders);
            }
        });

        // Let the subscription register
        await Task.Delay(1000);

        // NATS-DOC-START
        // Send a request with headers
        var requestHeaders = new NatsHeaders
        {
            ["X-Request-ID"] = "123",
            ["X-Priority"] = "high",
        };

        var reply = await client.RequestAsync<string, string>(subject: "service", data: "data", headers: requestHeaders);
        output.WriteLine($"Response: {reply.Data}");
        output.WriteLine($"Response ID: {reply.Headers?["X-Response-ID"]}");

        // NATS-DOC-END
    }
}

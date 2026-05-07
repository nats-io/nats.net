using NATS.Net;

[Collection("nats-server")]
public class QueueGroupsRequestReply(NatsServerFixture fixture)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // NATS-DOC-START
        // Set up 3 instances of the service
        var service1 = Service(1);
        var service2 = Service(2);
        var service3 = Service(3);

        await client.PingAsync(cts.Token);

        // Make requests; messages are balanced among the subscribers in the queue
        for (var x = 0; x < 10; x++)
        {
            try
            {
                var reply = await client.RequestAsync<string>("api.calculate", cancellationToken: cts.Token);
                Console.WriteLine($"{x}) {reply.Data}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"{x}) No Response");
            }
        }

        // NATS-DOC-END
        await cts.CancelAsync();
        await Task.WhenAll(service1, service2, service3);

        async Task Service(int id)
        {
            try
            {
                await foreach (var msg in client.SubscribeAsync<string>("api.calculate", queueGroup: "api-workers-queue", cancellationToken: cts.Token))
                {
                    await msg.ReplyAsync($"Result from service instance {id}", cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}

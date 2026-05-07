using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class SubjectsSingleWildcard(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Subscribe to the shipped orders
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.*.shipped"))
            {
                output.WriteLine($"[orders.*.shipped] {msg.Data,-12} ({msg.Subject})");
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.*.placed"))
            {
                output.WriteLine($"[orders.*.placed]  {msg.Data,-12} ({msg.Subject})");
            }
        });

        // Subscribe to the retail orders
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("orders.retail.*"))
            {
                output.WriteLine($"[orders.retail.*]  {msg.Data,-12} ({msg.Subject})");
            }
        });

        // Let subscription tasks start
        await Task.Delay(1000);

        // Publish to specific subjects
        await client.PublishAsync("orders.wholesale.placed", "Order W73737");
        await client.PublishAsync("orders.retail.placed", "Order R65432");
        await client.PublishAsync("orders.wholesale.shipped", "Order W73001");
        await client.PublishAsync("orders.retail.shipped", "Order R65321");

        // NATS-DOC-END
        await Task.Delay(1000);
    }
}

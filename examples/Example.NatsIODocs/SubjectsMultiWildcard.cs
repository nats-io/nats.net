using NATS.Net;

namespace Example.NatsIODocs;

[Collection("nats-server")]
public class SubjectsMultiWildcard(NatsServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync()
    {
        await using var client = new NatsClient(fixture.Server.Url);

        // NATS-DOC-START
        // Subscribe to all non-critical alarms
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("sensor.alarm.*"))
            {
                output.WriteLine($"[sensor.alarm.*]      {msg.Data,-15} ({msg.Subject})");
            }
        });

        // Subscribe to all critical
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("sensor.*.*.critical"))
            {
                output.WriteLine($"[sensor.*.*.critical] {msg.Data,-15} ({msg.Subject})");
            }
        });

        // Subscribe to everything
        _ = Task.Run(async () =>
        {
            await foreach (var msg in client.SubscribeAsync<string>("sensor.>"))
            {
                output.WriteLine($"[sensor.>]            {msg.Data,-15} ({msg.Subject})");
            }
        });

        // Let subscription tasks start
        await Task.Delay(1000);

        // Publish to specific subjects
        await client.PublishAsync("sensor.alarm.smoke", "kitchen,14:22");
        await client.PublishAsync("sensor.alarm.smoke.critical", "kitchen,14:23");
        await client.PublishAsync("sensor.alarm.water", "basement,16:42");
        await client.PublishAsync("sensor.alarm.water.critical", "basement,16:43");

        // NATS-DOC-END
        await Task.Delay(1000);
    }
}

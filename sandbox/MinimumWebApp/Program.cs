using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", async (INatsConnection command) =>
{
    var subscription = await command.SubscribeAsync<int>("foo");

    _ = Task.Run(async () =>
    {
        await foreach (var msg in subscription.Msgs.ReadAllAsync())
        {
            Console.WriteLine($"Received {msg.Data}");
        }
    });
});

app.MapGet("/publish", async (INatsConnection command) => await command.PublishAsync("foo", 99));

app.Run();

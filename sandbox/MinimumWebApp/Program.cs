using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", (INatsConnection command) =>
{
    _ = Task.Run(async () =>
    {
        await foreach (var msg in command.SubscribeAsync<int>("foo"))
        {
            Console.WriteLine($"Received {msg.Data}");
        }
    });

    return Task.CompletedTask;
});

app.MapGet("/publish", async (INatsConnection command) => await command.PublishAsync("foo", 99));

app.Run();

using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", async (INatsCommand command) => (await command.SubscribeAsync("foo")).Register(x => Console.WriteLine($"received {x.Data}")));
app.MapGet("/publish", async (INatsCommand command) => await command.PublishAsync("foo", 99));

app.Run();

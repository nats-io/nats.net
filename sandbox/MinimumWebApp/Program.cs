using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", (INatsCommand command) => command.SubscribeAsync("foo", (int x) => Console.WriteLine($"received {x}")));
app.MapGet("/publish", (INatsCommand command) => command.PublishAsync("foo", 99));

app.Run();

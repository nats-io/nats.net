using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = ConsoleApp.CreateBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddNats(poolSize: 4, configureOpts: opt => opt with { Url = "localhost:4222", Name = "MyClient" });
});

// create connection(default, connect to nats://localhost:4222)

// var conn = new NatsConnectionPool(1).GetConnection();
await using var conn = new NatsConnection();
conn.OnConnectingAsync = async x =>
{
    var health = await new HttpClient().GetFromJsonAsync<NatsHealth>($"http://{x.Host}:8222/healthz");
    if (health == null || health.Status != "ok")
        throw new Exception();

    return x;
};

// Server
var sub = await conn.SubscribeAsync<int>("foobar");
var replyTask = Task.Run(async () =>
{
    await foreach (var msg in sub.Msgs.ReadAllAsync())
    {
        await msg.ReplyAsync($"Hello {msg.Data}");
    }
});

// Client(response: "Hello 100")
var response = await conn.RequestAsync<int, string>("foobar", 100);

await sub.UnsubscribeAsync();
await replyTask;

// subscribe
var subscription = await conn.SubscribeAsync<Person>("foo");

_ = Task.Run(async () =>
{
    await foreach (var msg in subscription.Msgs.ReadAllAsync())
    {
        Console.WriteLine($"Received {msg.Data}");
    }
});

// publish
await conn.PublishAsync("foo", new Person(30, "bar"));

// Options can configure `with` expression
var options = NatsOpts.Default with
{
    Url = "nats://127.0.0.1:9999",
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Echo = true,
    AuthOpts = NatsAuthOpts.Default with
    {
        Username = "foo",
        Password = "bar",
    },
};

var app = builder.Build();

app.AddCommands<Runner>();
await app.RunAsync();

public record Person(int Age, string Name);

public class Runner : ConsoleAppBase
{
    private readonly INatsConnection _connection;

    public Runner(INatsConnection connection)
    {
        _connection = connection;
    }

    [RootCommand]
    public async Task Run()
    {
        var subscription = await _connection.SubscribeAsync("foo");

        _ = Task.Run(async () =>
        {
            await foreach (var msg in subscription.Msgs.ReadAllAsync())
            {
                Console.WriteLine("Yeah");
            }
        });

        await _connection.PingAsync();
        await _connection.PublishAsync("foo");
    }
}

public record NatsHealth(string Status);

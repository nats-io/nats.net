using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = ConsoleApp.CreateBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddNats(poolSize: 4, configureOptions: opt => opt with { Url = "localhost:4222", Name = "MyClient" });
});

// create connection(default, connect to nats://localhost:4222)

// var conn = new NatsConnectionPool(1).GetConnection();
await using var conn = new NatsConnection();
conn.OnConnectingAsync = async x =>
{
    var health = await new HttpClient().GetFromJsonAsync<NatsHealth>($"http://{x.Host}:8222/healthz");
    if (health == null || health.Status != "ok") throw new Exception();

    return x;
};

// Server
await conn.SubscribeRequestAsync("foobar", (int x) => $"Hello {x}");

// Client(response: "Hello 100")
var response = await conn.RequestAsync<int, string>("foobar", 100);

// subscribe
var subscription = await conn.SubscribeAsync<Person>("foo", x =>
{
    Console.WriteLine($"Received {x}");
});

// publish
await conn.PublishAsync("foo", new Person(30, "bar"));

// Options can configure `with` expression
var options = NatsOptions.Default with
{
    Url = "nats://127.0.0.1:9999",
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Echo = true,
    AuthOptions = NatsAuthOptions.Default with
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
    private readonly INatsCommand _command;

    public Runner(INatsCommand command)
    {
        _command = command;
    }

    [RootCommand]
    public async Task Run()
    {
        await _command.SubscribeAsync("foo", () => Console.WriteLine("Yeah"));
        await _command.PingAsync();
        await _command.PublishAsync("foo");
    }
}

public record NatsHealth(string Status);

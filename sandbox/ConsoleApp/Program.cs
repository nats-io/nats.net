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
var cts = new CancellationTokenSource();
var replyTask = Task.Run(async () =>
{
    await foreach (var msg in conn.SubscribeAsync<int>("foobar", cancellationToken: cts.Token))
    {
        await msg.ReplyAsync($"Hello {msg.Data}");
    }
});

// Client(response: "Hello 100")
var response = await conn.RequestAsync<int, string>("foobar", 100);

cts.Cancel();
await replyTask;

// subscribe
_ = Task.Run(async () =>
{
    await foreach (var msg in conn.SubscribeAsync<Person>("foo"))
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
        _ = Task.Run(async () =>
        {
            await foreach (var msg in _connection.SubscribeAsync<string>("foo"))
            {
                Console.WriteLine("Yeah");
            }
        });

        await _connection.PingAsync();
        await _connection.PublishAsync("foo");
    }
}

public record NatsHealth(string Status);

public class MinimumConsoleLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _logLevel;

    public MinimumConsoleLoggerFactory(LogLevel logLevel) => _logLevel = logLevel;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Logger(_logLevel);

    public void Dispose()
    {
    }

    private class Logger : ILogger
    {
        private readonly LogLevel _logLevel;

        public Logger(LogLevel logLevel) => _logLevel = logLevel;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => _logLevel <= logLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Console.WriteLine(formatter(state, exception));
                if (exception != null)
                {
                    Console.WriteLine(exception.ToString());
                }
            }
        }
    }

    private class NullDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NullDisposable();

        private NullDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}

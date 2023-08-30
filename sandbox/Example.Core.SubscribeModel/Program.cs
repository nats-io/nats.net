using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var subject = "bar.*";
var options = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print($"[SUB] Subscribing to subject '{subject}'...\n");

var sub = await connection.SubscribeAsync<Bar>(subject);

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Print($"[RCV] {msg.Subject}: {msg.Data}\n");
}

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

public record Bar
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

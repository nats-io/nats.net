// > nats pub bar.xyz --count=10 "my_message_{{ Count }}" -H X-Foo:Baz

using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var subject = "bar.*";
var options = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print($"[SUB] Subscribing to subject '{subject}'...\n");

var sub = await connection.SubscribeAsync(subject);

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    Print($"[RCV] {msg.Subject}: {Encoding.UTF8.GetString(msg.Data.Span)}\n");
    if (msg.Headers != null)
    {
        foreach (var (key, values) in msg.Headers)
        {
            foreach (var value in values)
                Print($"  {key}: {value}\n");
        }
    }
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

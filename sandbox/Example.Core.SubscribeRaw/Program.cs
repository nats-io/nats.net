using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print("[SUB] Subscribing to subject 'foo'...\n");

NatsSub sub = await connection.SubscribeAsync("foo");

await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    var data = Encoding.UTF8.GetString(msg.Data);
    Print($"[RCV] {data}\n");
}

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

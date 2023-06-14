// > nats pub foo.xyz --count=10 "my_message_{{ Count }}"
using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var subject = "foo.*";
var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

// ---
// Worker 1
Print("[1][CON] Connecting...\n");
await using var connection1 = new NatsConnection(options);

Print($"[1][SUB] Subscribing to subject '{subject}'...\n");
NatsSub sub1 = await connection1.SubscribeAsync(subject, new NatsSubOpts { QueueGroup = $"My-Workers" });
var task1 = Task.Run(async () =>
{
    await foreach (var msg in sub1.Msgs.ReadAllAsync())
    {
        var data = Encoding.UTF8.GetString(msg.Data.ToArray());
        Print($"[1][RCV] {msg.Subject}: {data}\n");
    }
});

// ---
// Worker 2
Print("[2][CON] Connecting...\n");
await using var connection2 = new NatsConnection(options);

Print($"[2][SUB] Subscribing to subject '{subject}'...\n");
NatsSub sub2 = await connection2.SubscribeAsync(subject, new NatsSubOpts { QueueGroup = $"My-Workers" });
var task2 = Task.Run(async () =>
{
    await foreach (var msg in sub2.Msgs.ReadAllAsync())
    {
        var data = Encoding.UTF8.GetString(msg.Data.ToArray());
        Print($"[2][RCV] {msg.Subject}: {data}\n");
    }
});

Console.ReadLine();

// ---
// Clean-up
Print($"[1][SUB] Unsubscribing '{subject}'...\n");
sub1.Dispose();

Print($"[2][SUB] Unsubscribing '{subject}'...\n");
sub2.Dispose();

await Task.WhenAll(task1, task2);

Print("Bye");

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

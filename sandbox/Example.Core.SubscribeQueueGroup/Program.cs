// > nats pub foo.xyz --count=10 "my_message_{{ Count }}"
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var subject = "foo.*";
var options = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

// ---
// Worker 1
Print("[1][CON] Connecting...\n");
await using var connection1 = new NatsConnection(options);

Print($"[1][SUB] Subscribing to subject '{subject}'...\n");
var sub1 = await connection1.SubscribeAsync<string>(subject, queueGroup: "My-Workers");
var task1 = Task.Run(async () =>
{
    await foreach (var msg in sub1.Msgs.ReadAllAsync())
    {
        Print($"[1][RCV] {msg.Subject}: {msg.Data}\n");
    }
});

// ---
// Worker 2
Print("[2][CON] Connecting...\n");
await using var connection2 = new NatsConnection(options);

Print($"[2][SUB] Subscribing to subject '{subject}'...\n");
var sub2 = await connection2.SubscribeAsync<string>(subject, queueGroup: "My-Workers");
var task2 = Task.Run(async () =>
{
    await foreach (var msg in sub2.Msgs.ReadAllAsync())
    {
        Print($"[2][RCV] {msg.Subject}: {msg.Data}\n");
    }
});

Console.ReadLine();

// ---
// Clean-up
Print($"[1][SUB] Unsubscribing '{subject}'...\n");
await sub1.DisposeAsync();

Print($"[2][SUB] Unsubscribing '{subject}'...\n");
await sub2.DisposeAsync();

await Task.WhenAll(task1, task2);

Print("Bye");

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

// > nats pub foo.xyz --count=10 "my_message_{{ Count }}"

using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

using var tracer = TracingSetup.RunSandboxTracing();

var subject = "foo.*";
var options = NatsOpts.Default with { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) };

// ---
// Worker 1
Print("[1][CON] Connecting...\n");
await using var connection1 = new NatsConnection(options);

Print($"[1][SUB] Subscribing to subject '{subject}'...\n");
var cts1 = new CancellationTokenSource();
var task1 = Task.Run(async () =>
{
    await foreach (var msg in connection1.SubscribeAsync<string>(subject, queueGroup: "My-Workers", cancellationToken: cts1.Token))
    {
        Print($"[1][RCV] {msg.Subject}: {msg.Data}\n");
    }
});

// ---
// Worker 2
Print("[2][CON] Connecting...\n");
await using var connection2 = new NatsConnection(options);

Print($"[2][SUB] Subscribing to subject '{subject}'...\n");
var cts2 = new CancellationTokenSource();
var task2 = Task.Run(async () =>
{
    await foreach (var msg in connection2.SubscribeAsync<string>(subject, queueGroup: "My-Workers", cancellationToken: cts2.Token))
    {
        Print($"[2][RCV] {msg.Subject}: {msg.Data}\n");
    }
});

Console.ReadLine();

// ---
// Clean-up
Print($"[1][SUB] Unsubscribing '{subject}'...\n");
cts1.Cancel();

Print($"[2][SUB] Unsubscribing '{subject}'...\n");
cts2.Cancel();

await Task.WhenAll(task1, task2);

Print("Bye");

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

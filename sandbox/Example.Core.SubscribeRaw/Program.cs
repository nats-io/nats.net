using System.Text;
using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

using var tracer = TracingSetup.RunSandboxTracing();

var subject = "foo.*";
var options = NatsOpts.Default with { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) };

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print($"[SUB] Subscribing to subject '{subject}'...\n");

await foreach (var msg in connection.SubscribeAsync<byte[]>(subject))
{
    var data = Encoding.UTF8.GetString(msg.Data!);
    Print($"[RCV] {msg.Subject}: {data}\n");
}

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

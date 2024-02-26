// > nats sub bar.*

using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

using var tracer = TracingSetup.RunSandboxTracing();

var subject = "bar.xyz";
var options = NatsOpts.Default with
{
    LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()),
    SerializerRegistry = NatsJsonSerializerRegistry.Default,
};

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);
await connection.ConnectAsync();

for (var i = 0; i < 10; i++)
{
    Print($"[PUB] Publishing to subject ({i}) '{subject}'...\n");
    await connection.PublishAsync(subject, new Bar { Id = i, Name = "Baz" });
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

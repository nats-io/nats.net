// > nats pub bar.xyz --count=10 "my_message_{{ Count }}" -H X-Foo:Baz

using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

using var tracer = TracingSetup.RunSandboxTracing();

var subject = "bar.*";
var options = NatsOpts.Default with
{
    LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()),
    SerializerRegistry = NatsJsonSerializerRegistry.Default,
};

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print($"[SUB] Subscribing to subject '{subject}'...\n");

await foreach (var msg in connection.SubscribeAsync<Bar>(subject))
{
    Print($"[RCV] {msg.Subject}: {msg.Data!}\n");
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
